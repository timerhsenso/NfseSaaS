using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Cobranca;

/// <summary>
/// Catálogo de planos no banco: leitura para a cobrança (ICatalogoPlanos) e
/// administração em Plataforma › Planos (IAdministrarPlanosUseCase).
/// Preço muda sempre por uma linha nova em precos_planos (histórico); quem já
/// assina continua com o preço contratado (Assinatura.PrecoMensal).
/// </summary>
public sealed class CatalogoPlanosService : ICatalogoPlanos, IAdministrarPlanosUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public CatalogoPlanosService(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    // ---------- Leitura (cobrança) ----------

    public async Task<IReadOnlyList<PlanoVigente>> PlanosAVendaAsync(PerfilConta perfil, CancellationToken cancellationToken)
    {
        var planos = await _db.Planos.AsNoTracking()
            .Where(p => p.AVenda && (perfil == PerfilConta.Contador ? p.OfertarParaContador : p.OfertarParaEmpresa))
            .OrderBy(p => p.Ordem).ThenBy(p => p.Codigo)
            .ToListAsync(cancellationToken);
        return await ComPrecoVigenteAsync(planos, cancellationToken);
    }

    public async Task<PlanoVigente?> ObterAsync(int codigo, CancellationToken cancellationToken)
    {
        var plano = await _db.Planos.AsNoTracking().FirstOrDefaultAsync(p => p.Codigo == codigo, cancellationToken);
        return plano is null ? null : (await ComPrecoVigenteAsync([plano], cancellationToken)).FirstOrDefault();
    }

    public async Task<IReadOnlyDictionary<int, string>> NomesAsync(CancellationToken cancellationToken) =>
        await _db.Planos.AsNoTracking().ToDictionaryAsync(p => p.Codigo, p => p.Nome, cancellationToken);

    public async Task<int> VagasFundadorRestantesAsync(PublicoPlano publico, CancellationToken cancellationToken)
    {
        var quantidade = await _db.VagasFundador.AsNoTracking()
            .Where(v => v.Publico == publico).Select(v => (int?)v.Quantidade).FirstOrDefaultAsync(cancellationToken) ?? 0;
        return Math.Max(0, quantidade - await FundadoresUsadosAsync(publico, cancellationToken));
    }

    /// <summary>Assinaturas com preço de fundador em planos do grupo (as canceladas também contam: a vaga foi usada).</summary>
    private Task<int> FundadoresUsadosAsync(PublicoPlano publico, CancellationToken cancellationToken)
    {
        var codigos = _db.Planos.Where(p => p.Publico == publico).Select(p => (int?)p.Codigo);
        return _db.Assinaturas.AsNoTracking().CountAsync(a => a.Fundador && codigos.Contains(a.Plano), cancellationToken);
    }

    private async Task<IReadOnlyList<PlanoVigente>> ComPrecoVigenteAsync(IReadOnlyList<Plano> planos, CancellationToken cancellationToken)
    {
        var hoje = DataBrasilia.Hoje();
        var ids = planos.Select(p => p.Id).ToList();
        var precos = (await _db.PrecosPlanos.AsNoTracking()
                .Where(p => ids.Contains(p.PlanoId) && p.VigenteDesde <= hoje)
                .ToListAsync(cancellationToken))
            .ToLookup(p => p.PlanoId);

        return planos
            .Select(p => (Plano: p, Preco: RegrasCatalogoPlanos.PrecoVigente(precos[p.Id], hoje)))
            .Where(x => x.Preco is not null) // plano sem preço vigente ainda não é vendido
            .Select(x => new PlanoVigente(
                x.Plano.Codigo, x.Plano.Nome, x.Plano.Descricao, x.Plano.Publico, x.Plano.OfertarParaEmpresa, x.Plano.OfertarParaContador,
                x.Plano.MaxEmpresas, x.Plano.NotasPorMes, x.Plano.AVenda, x.Preco!.PrecoMensal, x.Preco.PrecoFundador))
            .ToList();
    }

    // ---------- Plataforma › Planos ----------

    public async Task<IReadOnlyList<PlanoAdminResponse>> ListarAsync(CancellationToken cancellationToken)
    {
        var planos = await _db.Planos.AsNoTracking().OrderBy(p => p.Ordem).ThenBy(p => p.Codigo).ToListAsync(cancellationToken);
        var precos = (await _db.PrecosPlanos.AsNoTracking().ToListAsync(cancellationToken)).ToLookup(p => p.PlanoId);
        var assinantes = await _db.Assinaturas.AsNoTracking()
            .Where(a => a.Plano != null && a.Status != StatusAssinatura.Cancelada)
            .GroupBy(a => a.Plano!.Value)
            .Select(g => new { Codigo = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.Codigo, x => x.Total, cancellationToken);

        var hoje = DataBrasilia.Hoje();
        return planos.Select(p => Montar(p, precos[p.Id], assinantes.GetValueOrDefault(p.Codigo), hoje)).ToList();
    }

    public async Task<PlanoAdminResponse> CriarAsync(SalvarPlanoRequest request, Guid? operadorId, CancellationToken cancellationToken)
    {
        RegrasCatalogoPlanos.ValidarPlano(request, criacao: true);

        var codigo = (await _db.Planos.MaxAsync(p => (int?)p.Codigo, cancellationToken) ?? 0) + 1;
        var plano = new Plano { Codigo = codigo };
        Aplicar(plano, request);
        var preco = new PrecoPlano
        {
            PlanoId = plano.Id,
            PrecoMensal = request.PrecoMensal!.Value,
            PrecoFundador = request.PrecoFundador!.Value,
            VigenteDesde = DataBrasilia.Hoje(),
            Motivo = "Plano criado",
            AlteradoPorUsuarioId = operadorId
        };
        _db.Planos.Add(plano);
        _db.PrecosPlanos.Add(preco);
        _auditLogWriter.Registrar("CriarPlano", "Plano", plano.Id, new { plano.Codigo, plano.Nome, preco.PrecoMensal, preco.PrecoFundador });
        await _db.SaveChangesAsync(cancellationToken);

        return Montar(plano, [preco], 0, DataBrasilia.Hoje());
    }

    public async Task<PlanoAdminResponse> AlterarAsync(Guid id, SalvarPlanoRequest request, Guid? operadorId, CancellationToken cancellationToken)
    {
        RegrasCatalogoPlanos.ValidarPlano(request, criacao: false);

        var plano = await _db.Planos.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new RecursoNaoEncontradoException("Plano não encontrado.");

        // Mudar o grupo mudaria a conta das vagas de fundador já usadas.
        if (request.Publico != plano.Publico
            && await _db.Assinaturas.AnyAsync(a => a.Plano == plano.Codigo, cancellationToken))
            throw new RegraNegocioException("Este plano já tem assinaturas: o grupo de fundador não pode mais mudar.");

        var antes = new { plano.Nome, plano.Descricao, plano.Publico, plano.OfertarParaEmpresa, plano.OfertarParaContador, plano.MaxEmpresas, plano.NotasPorMes, plano.AVenda, plano.Ordem };
        Aplicar(plano, request);
        var depois = new { plano.Nome, plano.Descricao, plano.Publico, plano.OfertarParaEmpresa, plano.OfertarParaContador, plano.MaxEmpresas, plano.NotasPorMes, plano.AVenda, plano.Ordem };
        _auditLogWriter.Registrar("AlterarPlano", "Plano", plano.Id, new { plano.Codigo, antes, depois });
        await _db.SaveChangesAsync(cancellationToken);

        return (await ListarAsync(cancellationToken)).First(p => p.Id == id);
    }

    public async Task<IReadOnlyList<PrecoPlanoResponse>> HistoricoPrecosAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await _db.Planos.AnyAsync(p => p.Id == id, cancellationToken))
            throw new RecursoNaoEncontradoException("Plano não encontrado.");

        var precos = await _db.PrecosPlanos.AsNoTracking().Where(p => p.PlanoId == id).ToListAsync(cancellationToken);
        var usuarios = precos.Where(p => p.AlteradoPorUsuarioId is not null).Select(p => p.AlteradoPorUsuarioId!.Value).Distinct().ToList();
        var emails = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => usuarios.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        var hoje = DataBrasilia.Hoje();
        var vigente = RegrasCatalogoPlanos.PrecoVigente(precos, hoje);
        return precos
            .OrderByDescending(p => p.VigenteDesde)
            .Select(p => new PrecoPlanoResponse(
                p.Id, p.PrecoMensal, p.PrecoFundador, p.VigenteDesde, p.Motivo, p.UpdatedAt ?? p.CreatedAt,
                p.AlteradoPorUsuarioId is { } u ? emails.GetValueOrDefault(u) : null,
                RegrasCatalogoPlanos.Situacao(p, vigente, hoje)))
            .ToList();
    }

    public async Task AdicionarPrecoAsync(Guid id, NovoPrecoPlanoRequest request, Guid? operadorId, CancellationToken cancellationToken)
    {
        RegrasCatalogoPlanos.ValidarNovoPreco(request, DataBrasilia.Hoje());

        var plano = await _db.Planos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new RecursoNaoEncontradoException("Plano não encontrado.");
        var motivo = string.IsNullOrWhiteSpace(request.Motivo) ? null : request.Motivo.Trim();

        // Mesma data de um preço já cadastrado (hoje ou agendado) = correção dele.
        var existente = await _db.PrecosPlanos.FirstOrDefaultAsync(p => p.PlanoId == id && p.VigenteDesde == request.VigenteDesde, cancellationToken);
        if (existente is not null)
        {
            var antes = new { existente.PrecoMensal, existente.PrecoFundador, existente.Motivo };
            existente.PrecoMensal = request.PrecoMensal;
            existente.PrecoFundador = request.PrecoFundador;
            existente.Motivo = motivo;
            existente.AlteradoPorUsuarioId = operadorId;
            _auditLogWriter.Registrar("CorrigirPrecoPlano", "Plano", id,
                new { plano.Codigo, plano.Nome, request.VigenteDesde, antes, depois = new { request.PrecoMensal, request.PrecoFundador, Motivo = motivo } });
        }
        else
        {
            _db.PrecosPlanos.Add(new PrecoPlano
            {
                PlanoId = id,
                PrecoMensal = request.PrecoMensal,
                PrecoFundador = request.PrecoFundador,
                VigenteDesde = request.VigenteDesde,
                Motivo = motivo,
                AlteradoPorUsuarioId = operadorId
            });
            _auditLogWriter.Registrar("NovoPrecoPlano", "Plano", id,
                new { plano.Codigo, plano.Nome, request.VigenteDesde, request.PrecoMensal, request.PrecoFundador, Motivo = motivo });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VagasFundadorResponse>> ListarVagasAsync(CancellationToken cancellationToken)
    {
        var vagas = await _db.VagasFundador.AsNoTracking().OrderBy(v => v.Publico).ToListAsync(cancellationToken);
        var resposta = new List<VagasFundadorResponse>();
        foreach (var v in vagas)
        {
            var usadas = await FundadoresUsadosAsync(v.Publico, cancellationToken);
            resposta.Add(new VagasFundadorResponse(v.Publico, v.Quantidade, usadas, Math.Max(0, v.Quantidade - usadas)));
        }
        return resposta;
    }

    public async Task AlterarVagasAsync(AlterarVagasFundadorRequest request, Guid? operadorId, CancellationToken cancellationToken)
    {
        RegrasCatalogoPlanos.ValidarVagas(request);

        var vagas = await _db.VagasFundador.FirstOrDefaultAsync(v => v.Publico == request.Publico, cancellationToken);
        if (vagas is null)
        {
            vagas = new VagasFundador { Publico = request.Publico };
            _db.VagasFundador.Add(vagas);
        }

        var antes = vagas.Quantidade;
        vagas.Quantidade = request.Quantidade;
        vagas.UpdatedAt = DateTimeOffset.UtcNow;
        _auditLogWriter.Registrar("AlterarVagasFundador", "VagasFundador", null,
            new { request.Publico, antes, depois = request.Quantidade, Motivo = string.IsNullOrWhiteSpace(request.Motivo) ? null : request.Motivo.Trim() });
        await _db.SaveChangesAsync(cancellationToken);
    }

    // ---------- Apoio ----------

    private static void Aplicar(Plano plano, SalvarPlanoRequest r)
    {
        plano.Nome = r.Nome.Trim();
        plano.Descricao = (r.Descricao ?? string.Empty).Trim();
        plano.Publico = r.Publico;
        plano.OfertarParaEmpresa = r.OfertarParaEmpresa;
        plano.OfertarParaContador = r.OfertarParaContador;
        plano.MaxEmpresas = r.MaxEmpresas;
        plano.NotasPorMes = r.NotasPorMes;
        plano.AVenda = r.AVenda;
        plano.Ordem = r.Ordem;
    }

    private static PlanoAdminResponse Montar(Plano p, IEnumerable<PrecoPlano> precos, int assinantes, DateOnly hoje)
    {
        var lista = precos.ToList();
        var vigente = RegrasCatalogoPlanos.PrecoVigente(lista, hoje);
        var proximo = RegrasCatalogoPlanos.ProximoPreco(lista, hoje);
        return new PlanoAdminResponse(
            p.Id, p.Codigo, p.Nome, p.Descricao, p.Publico, p.OfertarParaEmpresa, p.OfertarParaContador,
            p.MaxEmpresas, p.NotasPorMes, p.AVenda, p.Ordem,
            vigente?.PrecoMensal, vigente?.PrecoFundador, vigente?.VigenteDesde,
            proximo?.PrecoMensal, proximo?.PrecoFundador, proximo?.VigenteDesde,
            assinantes);
    }
}
