using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Application.UseCases.Plataforma;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Cobranca;

/// <summary>
/// Exclusão de um plano (Plataforma › Planos) em dois passos, com o mesmo
/// código de 6 dígitos da exclusão de conta (<see cref="CodigoExclusaoConta"/>):
/// vai direto pelo SMTP ao e-mail do operador logado, vale 10 minutos, uso
/// único, 5 tentativas; em memória fica só o hash.
///
/// Regra (conferida nos dois passos): o plano nunca foi assinado (nem
/// assinatura cancelada, que guarda o código dele). Já assinado: "Fora de
/// venda". Plano inicial excluído não volta (o PlanosSeeder só semeia catálogo vazio).
/// Apaga o plano e o histórico de preços dele; fica o registro ExcluirPlano
/// no Histórico do operador, com os preços que existiam.
/// </summary>
public sealed class ExclusaoPlanoService : ISolicitarExclusaoPlanoUseCase, IConfirmarExclusaoPlanoUseCase
{
    private sealed record CodigoPendente(byte[] Hash, DateTimeOffset ExpiraEm)
    {
        public int Tentativas { get; set; }
    }

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IEmailSender _emailSender;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly ILogger<ExclusaoPlanoService> _logger;

    public ExclusaoPlanoService(
        AppDbContext db, IMemoryCache cache, IEmailSender emailSender, IAuditLogWriter auditLogWriter, ILogger<ExclusaoPlanoService> logger)
    {
        _db = db;
        _cache = cache;
        _emailSender = emailSender;
        _auditLogWriter = auditLogWriter;
        _logger = logger;
    }

    private static string ChaveCache(Guid operadorId, Guid planoId) => $"exclusao-plano:{operadorId:N}:{planoId:N}";

    async Task<SolicitacaoExclusaoPlanoResponse> ISolicitarExclusaoPlanoUseCase.ExecutarAsync(Guid planoId, Guid operadorId, CancellationToken cancellationToken)
    {
        var plano = await ValidarPodeExcluirAsync(planoId, cancellationToken);

        var emailOperador = await _db.Users.AsNoTracking()
            .Where(u => u.Id == operadorId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(emailOperador))
            throw new RegraNegocioException("Seu usuário não tem e-mail cadastrado para receber o código.");

        var codigo = CodigoExclusaoConta.Gerar();
        var corpo = $"""
            <p>Você pediu para <strong>excluir</strong> o plano <strong>{WebUtility.HtmlEncode(plano.Nome)}</strong> (código {plano.Codigo}) no micoempresa.</p>
            <p style="font-size:28px;letter-spacing:6px;font-weight:bold;margin:16px 0">{codigo}</p>
            <p>O código vale por {CodigoExclusaoConta.MinutosValidade} minutos e só pode ser usado uma vez.</p>
            <p style="color:#64748b;font-size:12px">Se não foi você, ignore este e-mail e troque sua senha: sem o código, nada é excluído.</p>
            """;

        // Direto pelo SMTP (sem fila): o código não fica gravado no EmailLog.
        var envio = await _emailSender.EnviarAsync(emailOperador, $"Código para excluir o plano {plano.Nome}", corpo, cancellationToken);
        if (!envio.Sucesso)
        {
            _logger.LogError("Falha ao enviar o código de exclusão do plano {PlanoId}: {Erro}", planoId, envio.ErroDetalhe);
            throw new RegraNegocioException("Não foi possível enviar o e-mail com o código. Confira a configuração de e-mail e tente de novo.");
        }

        // Pedir de novo substitui o código anterior.
        _cache.Set(ChaveCache(operadorId, planoId),
            new CodigoPendente(CodigoExclusaoConta.Hash(codigo), DateTimeOffset.UtcNow.AddMinutes(CodigoExclusaoConta.MinutosValidade)),
            TimeSpan.FromMinutes(CodigoExclusaoConta.MinutosValidade));

        var precos = await _db.PrecosPlanos.CountAsync(p => p.PlanoId == planoId, cancellationToken);
        return new SolicitacaoExclusaoPlanoResponse(
            CodigoExclusaoConta.MascararEmail(emailOperador), CodigoExclusaoConta.MinutosValidade, plano.Nome, precos);
    }

    async Task IConfirmarExclusaoPlanoUseCase.ExecutarAsync(Guid planoId, Guid operadorId, string codigo, CancellationToken cancellationToken)
    {
        var chave = ChaveCache(operadorId, planoId);
        if (!_cache.TryGetValue(chave, out CodigoPendente? pendente) || pendente is null || pendente.ExpiraEm <= DateTimeOffset.UtcNow)
        {
            _cache.Remove(chave);
            throw new RegraNegocioException("O código expirou ou não foi pedido. Clique em excluir de novo para receber outro.");
        }

        if (!CodigoExclusaoConta.Confere(pendente.Hash, codigo))
        {
            pendente.Tentativas++;
            if (pendente.Tentativas >= CodigoExclusaoConta.MaxTentativas)
            {
                _cache.Remove(chave);
                throw new RegraNegocioException("Código errado muitas vezes. Por segurança ele foi cancelado: peça um novo.");
            }
            throw new RegraNegocioException($"Código errado. Restam {CodigoExclusaoConta.MaxTentativas - pendente.Tentativas} tentativa(s).");
        }

        // Uso único, mesmo que algo falhe daqui em diante.
        _cache.Remove(chave);

        var plano = await ValidarPodeExcluirAsync(planoId, cancellationToken);
        var precos = await _db.PrecosPlanos.Where(p => p.PlanoId == planoId).ToListAsync(cancellationToken);

        _db.PrecosPlanos.RemoveRange(precos);
        _db.Planos.Remove(plano);
        _auditLogWriter.Registrar("ExcluirPlano", "Plano", plano.Id, new
        {
            plano.Codigo,
            plano.Nome,
            precos = precos.OrderBy(p => p.VigenteDesde).Select(p => new { p.VigenteDesde, p.PrecoMensal, p.PrecoFundador, p.Motivo })
        });
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("Plano {Codigo} ({Nome}) excluído pelo operador {OperadorId}.", plano.Codigo, plano.Nome, operadorId);
    }

    private async Task<Plano> ValidarPodeExcluirAsync(Guid planoId, CancellationToken cancellationToken)
    {
        var plano = await _db.Planos.FirstOrDefaultAsync(p => p.Id == planoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException("Plano não encontrado.");
        var assinaturas = await _db.Assinaturas.CountAsync(a => a.Plano == plano.Codigo, cancellationToken);
        if (RegrasCatalogoPlanos.MotivoNaoPodeExcluir(assinaturas) is { } motivo)
            throw new RegraNegocioException(motivo);
        return plano;
    }
}
