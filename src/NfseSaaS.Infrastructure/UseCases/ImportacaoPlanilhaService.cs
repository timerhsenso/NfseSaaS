using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.Fiscal.Retencoes;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Application.UseCases.Consultas;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Application.UseCases.Importacao;
using NfseSaaS.Application.UseCases.Recursos;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Importacao;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// IM1: importa clientes e contratos de uma planilha na empresa selecionada.
///
/// Conferir: lê o arquivo, entende cada linha e diz o que vai acontecer
/// (nova, atualiza, já existe, sem mudança, erro), sem gravar nada.
/// Gravar: confere de novo o MESMO arquivo (o servidor não guarda nada entre
/// as duas chamadas) e grava as linhas sem erro pelos casos de uso das telas
/// (Cadastrar/AtualizarCliente, CadastrarContrato): mesmas regras, mesma
/// auditoria, mesmo histórico de valores e mesmo limite do plano. Cada linha
/// grava sozinha; uma que falha não desfaz as outras e volta como erro.
/// </summary>
public sealed class ImportacaoPlanilhaService : IImportacaoPlanilhaUseCase
{
    private const string AbaClientes = "clientes";
    private const string AbaContratos = "contratos";
    private const int ConsultasEmParalelo = 4;
    private static readonly TimeSpan TempoCacheConsulta = TimeSpan.FromMinutes(30);
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly IRecursosConta _recursos;
    private readonly ICadastrarClienteUseCase _cadastrarCliente;
    private readonly IAtualizarClienteUseCase _atualizarCliente;
    private readonly ICadastrarContratoUseCase _cadastrarContrato;
    private readonly IValidator<CadastrarClienteRequest> _validarNovoCliente;
    private readonly IValidator<AtualizarClienteRequest> _validarAtualizacaoCliente;
    private readonly IValidator<CadastrarContratoRequest> _validarContrato;
    private readonly IConsultarCnpjUseCase _consultarCnpj;
    private readonly IConsultarCepUseCase _consultarCep;
    private readonly IMemoryCache _cache;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly ILogger<ImportacaoPlanilhaService> _logger;

    public ImportacaoPlanilhaService(
        AppDbContext db,
        ICurrentTenant currentTenant,
        IRecursosConta recursos,
        ICadastrarClienteUseCase cadastrarCliente,
        IAtualizarClienteUseCase atualizarCliente,
        ICadastrarContratoUseCase cadastrarContrato,
        IValidator<CadastrarClienteRequest> validarNovoCliente,
        IValidator<AtualizarClienteRequest> validarAtualizacaoCliente,
        IValidator<CadastrarContratoRequest> validarContrato,
        IConsultarCnpjUseCase consultarCnpj,
        IConsultarCepUseCase consultarCep,
        IMemoryCache cache,
        IAuditLogWriter auditLogWriter,
        ILogger<ImportacaoPlanilhaService> logger)
    {
        _db = db;
        _currentTenant = currentTenant;
        _recursos = recursos;
        _cadastrarCliente = cadastrarCliente;
        _atualizarCliente = atualizarCliente;
        _cadastrarContrato = cadastrarContrato;
        _validarNovoCliente = validarNovoCliente;
        _validarAtualizacaoCliente = validarAtualizacaoCliente;
        _validarContrato = validarContrato;
        _consultarCnpj = consultarCnpj;
        _consultarCep = consultarCep;
        _cache = cache;
        _auditLogWriter = auditLogWriter;
        _logger = logger;
    }

    // =====================================================================
    // Modelo e relatório
    // =====================================================================

    public async Task<byte[]> ModeloAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas.AsNoTracking().Where(e => e.Id == empresaId)
            .Select(e => new { e.NomeFantasia, e.RazaoSocial })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        var servicos = await _db.Servicos.AsNoTracking()
            .Where(s => s.EmpresaId == empresaId && s.Ativo)
            .OrderBy(s => s.Descricao)
            .Select(s => s.Descricao)
            .ToListAsync(cancellationToken);

        var nome = string.IsNullOrWhiteSpace(empresa.NomeFantasia) ? empresa.RazaoSocial : empresa.NomeFantasia!;
        return GeradorPlanilhaImportacao.Modelo(nome, servicos.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    public byte[] RelatorioErros(RelatorioErrosImportacaoRequest request) => GeradorPlanilhaImportacao.RelatorioErros(request);

    // =====================================================================
    // Conferir e gravar
    // =====================================================================

    public Task<ResultadoImportacaoResponse> ConferirAsync(
        Guid empresaId, ArquivoImportacao arquivo, OpcoesImportacao opcoes, PermissoesImportacao permissoes, CancellationToken cancellationToken) =>
        ProcessarAsync(empresaId, arquivo, opcoes, permissoes, gravar: false, cancellationToken);

    public Task<ResultadoImportacaoResponse> GravarAsync(
        Guid empresaId, ArquivoImportacao arquivo, OpcoesImportacao opcoes, PermissoesImportacao permissoes, CancellationToken cancellationToken) =>
        ProcessarAsync(empresaId, arquivo, opcoes, permissoes, gravar: true, cancellationToken);

    private async Task<ResultadoImportacaoResponse> ProcessarAsync(
        Guid empresaId, ArquivoImportacao arquivo, OpcoesImportacao opcoes, PermissoesImportacao permissoes, bool gravar, CancellationToken cancellationToken)
    {
        var tenantId = _currentTenant.TenantId ?? throw new RegraNegocioException("Conta não identificada.");
        if (!await _db.Empresas.AsNoTracking().AnyAsync(e => e.Id == empresaId, cancellationToken))
            throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        var planilha = LeitorPlanilha.Ler(arquivo.NomeArquivo, arquivo.Conteudo);
        var avisos = new List<string>();

        AbaLida? abaClientes = null;
        AbaLida? abaContratos = null;
        foreach (var aba in planilha.Abas)
        {
            var tipo = ColunasImportacao.TipoDaAba(aba.Nome, aba.Cabecalhos);
            if (tipo == TipoAbaImportacao.Clientes && abaClientes is null)
                abaClientes = aba;
            else if (tipo == TipoAbaImportacao.Contratos && abaContratos is null)
                abaContratos = aba;
            else if (aba.Linhas.Count > 0 && !EhAbaDeApoio(aba.Nome))
                avisos.Add(tipo == TipoAbaImportacao.Ignorada
                    ? $"A aba \"{aba.Nome}\" foi ignorada: não reconheci as colunas."
                    : $"A aba \"{aba.Nome}\" foi ignorada: já usei outra aba para {(tipo == TipoAbaImportacao.Clientes ? "clientes" : "contratos")}.");
        }

        if (abaClientes is null && abaContratos is null)
            throw new RegraNegocioException("Não encontrei as colunas de clientes nem de contratos nesta planilha. Baixe o modelo e use os mesmos títulos de coluna.");

        var totalLinhas = (abaClientes?.Linhas.Count ?? 0) + (abaContratos?.Linhas.Count ?? 0);
        if (totalLinhas == 0)
            throw new RegraNegocioException("A planilha não tem nenhuma linha preenchida abaixo do cabeçalho.");
        if (totalLinhas > RegrasImportacao.MaximoLinhas)
            throw new RegraNegocioException(RegrasImportacao.MensagemMuitasLinhas(totalLinhas));

        var contexto = new Contexto(empresaId, tenantId, opcoes, permissoes);

        var abasResposta = new List<AbaImportacaoResponse>();
        List<LinhaTrabalho> linhasClientes = [];
        List<GrupoContrato> gruposContratos = [];

        if (abaClientes is not null)
            linhasClientes = await ConferirClientesAsync(abaClientes, contexto, avisos, cancellationToken);
        if (abaContratos is not null)
            gruposContratos = await ConferirContratosAsync(abaContratos, contexto, linhasClientes, cancellationToken);

        if (gravar)
            await GravarTudoAsync(arquivo.NomeArquivo, contexto, linhasClientes, gruposContratos, cancellationToken);

        if (abaClientes is not null)
            abasResposta.Add(MontarAba(AbaClientes, abaClientes, contexto.MapaClientes!, ColunasImportacao.Clientes, linhasClientes, gravar));
        if (abaContratos is not null)
            abasResposta.Add(MontarAba(AbaContratos, abaContratos, contexto.MapaContratos!, ColunasImportacao.Contratos, gruposContratos.SelectMany(g => g.Linhas).OrderBy(l => l.Linha).ToList(), gravar));

        return new ResultadoImportacaoResponse(
            gravar,
            arquivo.NomeArquivo,
            abasResposta,
            Totais(linhasClientes),
            Totais(gruposContratos.Select(g => g.Linhas[0]).ToList()),
            avisos);
    }

    private static bool EhAbaDeApoio(string nome)
    {
        var chave = ColunasImportacao.ChaveCabecalho(nome);
        return chave.Contains("instruc") || chave.Contains("lista") || chave.Contains("ajuda");
    }

    // =====================================================================
    // Clientes
    // =====================================================================

    private async Task<List<LinhaTrabalho>> ConferirClientesAsync(AbaLida aba, Contexto ctx, List<string> avisosGerais, CancellationToken ct)
    {
        var mapa = ColunasImportacao.Mapear(aba.Cabecalhos, ColunasImportacao.Clientes);
        ctx.MapaClientes = mapa;
        var linhas = new List<LinhaTrabalho>();

        var semDocumento = !mapa.Tem(CampoCliente.CpfCnpj);

        var existentes = (await ClientesDaEmpresa.Visiveis(_db, ctx.EmpresaId).AsNoTracking().ToListAsync(ct))
            .GroupBy(c => c.CpfCnpj)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.EmpresaId.HasValue).First());
        ctx.ClientesExistentes = existentes;

        var disponibilidade = await ObterDisponibilidadeClienteUseCase.CalcularAsync(_db, ctx.TenantId, ct);
        var limite = await _recursos.ObterAsync(ctx.TenantId, CatalogoRecursos.ClientesPorEmpresa, ct);
        var ativos = existentes.Values.Count(c => c.Ativo);

        var saidas = ctx.Opcoes.AtualizarExistentes
            ? (await _db.SaidasCopiaEmail.AsNoTracking()
                .Where(s => s.Status == StatusSaidaCopia.Confirmada)
                .Select(s => new { s.ClienteId, s.Email })
                .ToListAsync(ct))
                .Select(s => (s.ClienteId, s.Email.ToLowerInvariant()))
                .ToHashSet()
            : [];

        // 1ª passada: documentos, repetidos e o que precisa de consulta externa.
        var primeiraLinhaDoDocumento = new Dictionary<string, int>(StringComparer.Ordinal);
        var precisaCnpj = new List<string>();
        var precisaCep = new List<string>();
        foreach (var lida in aba.Linhas)
        {
            var doc = RegrasImportacao.LerDocumento(mapa.Celula(lida, CampoCliente.CpfCnpj));
            if (doc.Valor is not { } documento || existentes.ContainsKey(documento) || primeiraLinhaDoDocumento.ContainsKey(documento))
            {
                if (doc.Valor is { } d)
                    primeiraLinhaDoDocumento.TryAdd(d, lida.Numero);
                continue;
            }
            primeiraLinhaDoDocumento[documento] = lida.Numero;

            var nomeVazio = RegrasImportacao.LerTexto(mapa.Celula(lida, CampoCliente.Nome)) is null;
            var enderecoVazio = EnderecoVazio(mapa, lida);
            if (documento.Length == 14 && (nomeVazio || enderecoVazio))
                precisaCnpj.Add(documento);
            else if (!enderecoVazio
                     && RegrasImportacao.LerCep(mapa.Celula(lida, CampoCliente.Cep)).Valor is { } cep
                     && RegrasImportacao.LerCodigoMunicipio(mapa.Celula(lida, CampoCliente.CodigoMunicipio)).Valor is null
                     && RegrasImportacao.LerTexto(mapa.Celula(lida, CampoCliente.Municipio)) is null)
                precisaCep.Add(cep);
        }

        var receita = await ConsultarCnpjsAsync(precisaCnpj, ct);
        var ceps = await ConsultarCepsAsync(precisaCep.Distinct().Take(Math.Max(0, RegrasImportacao.MaximoConsultasCnpj - receita.Count)).ToList(), ct);
        if (precisaCnpj.Count > RegrasImportacao.MaximoConsultasCnpj)
            avisosGerais.Add($"Buscamos na Receita só os {RegrasImportacao.MaximoConsultasCnpj} primeiros CNPJs sem nome ou endereço. Preencha os outros na planilha ou importe em partes.");

        var municipios = await CarregarMunicipiosAsync(ct);
        var apelidosDaPlanilha = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documentosVistos = new Dictionary<string, int>(StringComparer.Ordinal);

        // 2ª passada: cada linha.
        foreach (var lida in aba.Linhas)
        {
            var linha = new LinhaTrabalho(AbaClientes, lida.Numero, Valores(lida, aba.Cabecalhos.Count));
            linhas.Add(linha);

            if (semDocumento)
            {
                linha.Erro("A planilha não tem a coluna CPF/CNPJ.");
                continue;
            }

            var doc = RegrasImportacao.LerDocumento(mapa.Celula(lida, CampoCliente.CpfCnpj));
            if (!doc.Ok)
            {
                linha.Chave = mapa.Celula(lida, CampoCliente.CpfCnpj).Texto;
                linha.Erro(doc.Erro!);
                continue;
            }
            if (doc.Valor is not { } documento)
            {
                linha.Erro("Falta o CPF/CNPJ.");
                continue;
            }

            linha.Documento = documento;
            linha.Chave = RegrasImportacao.FormatarDocumento(documento);
            if (documentosVistos.TryGetValue(documento, out var primeira))
            {
                linha.Erro($"CPF/CNPJ repetido: já está na linha {primeira}.");
                continue;
            }
            documentosVistos[documento] = lida.Numero;

            var dados = LerDadosCliente(mapa, lida, linha);

            if (existentes.TryGetValue(documento, out var existente))
            {
                linha.ClienteExistenteId = existente.Id;
                linha.Descricao = ApelidoENome(existente.Apelido, existente.Nome);
                if (!ctx.Opcoes.AtualizarExistentes)
                {
                    linha.Erros.Clear();
                    linha.Avisos.Clear();
                    linha.Situacao = SituacaoLinhaImportacao.JaExiste;
                    continue;
                }
                await ConferirAtualizacaoAsync(linha, existente, dados, ctx, disponibilidade, municipios, saidas, apelidosDaPlanilha, ct);
                continue;
            }

            if (linha.TemErro)
                continue;
            if (!ctx.Permissoes.IncluirClientes)
            {
                linha.Erro("Você não tem permissão para incluir clientes.");
                continue;
            }

            receita.TryGetValue(documento, out var consulta);
            var novo = await MontarNovoClienteAsync(linha, documento, dados, consulta, ctx, disponibilidade, municipios, ceps, apelidosDaPlanilha, ct);
            if (novo is null || linha.TemErro)
                continue;

            if (!RegrasRecursos.CabeMaisUm(limite.Limite, ativos))
            {
                linha.Erro(RegrasRecursos.MensagemLimite(limite, ativos));
                continue;
            }

            AdicionarErros(linha, await ValidarAsync(_validarNovoCliente, novo, ct));
            if (linha.TemErro)
                continue;

            ativos++;
            linha.NovoCliente = novo;
            linha.Situacao = SituacaoLinhaImportacao.Nova;
            linha.Descricao = ApelidoENome(novo.Apelido, novo.Nome);
        }

        return linhas;
    }

    private sealed record DadosCliente(
        string? Nome, string? Apelido, string? Email, string? Telefone,
        string? Cep, string? Logradouro, string? Numero, string? Complemento, string? Bairro,
        string? Municipio, string? Uf, string? CodigoMunicipio,
        bool? RetemIss, bool? EnviarPorEmail, IReadOnlyList<string> Copias, bool? Compartilhado,
        PercentuaisRetencao? Retencoes)
    {
        public bool TemEndereco => new[] { Cep, Logradouro, Numero, Complemento, Bairro, Municipio, CodigoMunicipio }.Any(v => v is not null);
    }

    private static bool EnderecoVazio(MapaColunas<CampoCliente> mapa, LinhaLida lida) =>
        new[] { CampoCliente.Cep, CampoCliente.Logradouro, CampoCliente.Numero, CampoCliente.Complemento, CampoCliente.Bairro, CampoCliente.Municipio, CampoCliente.CodigoMunicipio }
            .All(c => mapa.Celula(lida, c).EstaVazia);

    private static DadosCliente LerDadosCliente(MapaColunas<CampoCliente> mapa, LinhaLida lida, LinhaTrabalho linha)
    {
        CelulaLida C(CampoCliente campo) => mapa.Celula(lida, campo);
        T? Ok<T>(Lido<T> lido)
        {
            if (!lido.Ok)
                linha.Erros.Add(lido.Erro!);
            return lido.Valor;
        }

        var telefone = RegrasImportacao.LerSoNumeros(C(CampoCliente.Telefone));
        if (telefone is { Length: > 20 })
            linha.Erros.Add("Telefone com mais de 20 números.");

        decimal? Percentual(CampoCliente campo)
        {
            var valor = Ok(RegrasImportacao.LerPercentual(C(campo)));
            return valor is 0m ? null : valor;
        }

        var irrf = Percentual(CampoCliente.Irrf);
        var pis = Percentual(CampoCliente.Pis);
        var cofins = Percentual(CampoCliente.Cofins);
        var csll = Percentual(CampoCliente.Csll);
        var inss = Percentual(CampoCliente.Inss);
        var temRetencao = new[] { CampoCliente.Irrf, CampoCliente.Pis, CampoCliente.Cofins, CampoCliente.Csll, CampoCliente.Inss }
            .Any(c => !C(c).EstaVazia);

        var email = RegrasImportacao.LerTexto(C(CampoCliente.Email));
        if (email is not null)
        {
            var emails = RegrasImportacao.LerEmails(C(CampoCliente.Email));
            if (emails.Count > 1)
            {
                // "a@x.com; b@x.com" na coluna do e-mail: o primeiro é o principal, os outros viram cópia.
                email = emails[0];
                linha.Avisos.Add($"Mais de um e-mail na coluna E-mail: {emails[0]} ficou como principal e os outros foram para as cópias.");
            }
        }

        var copias = RegrasImportacao.LerEmails(C(CampoCliente.EmailsCopia)).ToList();
        if (email is not null)
        {
            var extras = RegrasImportacao.LerEmails(C(CampoCliente.Email)).Skip(1);
            copias.InsertRange(0, extras.Where(e => !copias.Contains(e, StringComparer.OrdinalIgnoreCase)));
        }

        return new DadosCliente(
            RegrasImportacao.LerTexto(C(CampoCliente.Nome), 200),
            RegrasImportacao.LerTexto(C(CampoCliente.Apelido)),
            email,
            telefone,
            Ok(RegrasImportacao.LerCep(C(CampoCliente.Cep))),
            RegrasImportacao.LerTexto(C(CampoCliente.Logradouro)),
            RegrasImportacao.LerTexto(C(CampoCliente.Numero)),
            RegrasImportacao.LerTexto(C(CampoCliente.Complemento)),
            RegrasImportacao.LerTexto(C(CampoCliente.Bairro)),
            RegrasImportacao.LerTexto(C(CampoCliente.Municipio)),
            Ok(RegrasImportacao.LerUf(C(CampoCliente.Uf))),
            Ok(RegrasImportacao.LerCodigoMunicipio(C(CampoCliente.CodigoMunicipio))),
            Ok(RegrasImportacao.LerSimNao(C(CampoCliente.RetemIss))),
            Ok(RegrasImportacao.LerSimNao(C(CampoCliente.EnviarNotaPorEmail))),
            copias.Where(e => !string.Equals(e, email, StringComparison.OrdinalIgnoreCase)).ToList(),
            Ok(RegrasImportacao.LerDisponivelPara(C(CampoCliente.DisponivelPara))),
            temRetencao ? new PercentuaisRetencao(irrf, pis, cofins, csll, inss) : null);
    }

    private async Task<CadastrarClienteRequest?> MontarNovoClienteAsync(
        LinhaTrabalho linha, string documento, DadosCliente d, ResultadoConsulta<ConsultaCnpjResponse>? receita, Contexto ctx,
        DisponibilidadeClienteResponse disponibilidade, Municipios municipios, IReadOnlyDictionary<string, ResultadoConsulta<ConsultaCepResponse>> ceps,
        HashSet<string> apelidosDaPlanilha, CancellationToken ct)
    {
        var ehCnpj = documento.Length == 14;
        var nome = d.Nome;
        string? fantasia = null;
        string? cep = d.Cep, logradouro = d.Logradouro, numero = d.Numero, complemento = d.Complemento, bairro = d.Bairro;
        string? uf = d.Uf, codigoMunicipio = null;

        if (ehCnpj && (nome is null || !d.TemEndereco))
        {
            if (receita?.Valor is { } r)
            {
                fantasia = string.IsNullOrWhiteSpace(r.NomeFantasia) ? null : r.NomeFantasia.Trim();
                var veio = new List<string>();
                if (nome is null)
                {
                    nome = r.RazaoSocial.Trim();
                    veio.Add("o nome");
                }
                if (!d.TemEndereco && !string.IsNullOrWhiteSpace(r.CodigoMunicipio))
                {
                    cep = r.Cep is null ? null : new string(r.Cep.Where(char.IsDigit).ToArray());
                    logradouro = r.Logradouro?.Trim();
                    numero = string.IsNullOrWhiteSpace(r.Numero) ? "S/N" : r.Numero.Trim();
                    complemento = string.IsNullOrWhiteSpace(r.Complemento) ? null : r.Complemento.Trim();
                    bairro = r.Bairro?.Trim();
                    codigoMunicipio = r.CodigoMunicipio;
                    uf = r.Uf?.Trim().ToUpperInvariant();
                    veio.Add("o endereço");
                }
                if (veio.Count > 0)
                    linha.Avisos.Add($"Buscamos {string.Join(" e ", veio)} na Receita.");
                if (!r.Ativa)
                    linha.Avisos.Add($"Na Receita, este CNPJ está com a situação {r.SituacaoCadastral}.");
            }
            else if (receita?.Erro is { } erroConsulta)
            {
                linha.Erro($"{(nome is null ? "Falta o nome" : "Falta o endereço")}, e não consegui buscar na Receita: {erroConsulta}");
                return null;
            }
            else
            {
                linha.Erro($"{(nome is null ? "Falta o nome" : "Falta o endereço")}. Passou do limite de {RegrasImportacao.MaximoConsultasCnpj} buscas na Receita por importação: preencha na planilha ou importe em partes.");
                return null;
            }
        }

        if (nome is null)
        {
            linha.Erro("Falta o nome.");
            return null;
        }

        if (codigoMunicipio is null && d.TemEndereco)
        {
            codigoMunicipio = ResolverMunicipio(linha, d, municipios, ceps, out var ufDoMunicipio);
            if (linha.TemErro)
                return null;
            uf ??= ufDoMunicipio;
        }

        if (!string.IsNullOrWhiteSpace(logradouro) && numero is null)
        {
            numero = "S/N";
            linha.Avisos.Add("Endereço sem número: gravamos S/N.");
        }

        bool? compartilhado = disponibilidade.MostrarCampo ? d.Compartilhado : null;
        var vaiSerCompartilhado = compartilhado ?? disponibilidade.PadraoCompartilhado;
        Guid? escopoApelido = vaiSerCompartilhado ? null : ctx.EmpresaId;

        var baseApelido = d.Apelido ?? fantasia ?? nome;
        var apelido = await EscolherApelidoAsync(escopoApelido, baseApelido, documento, apelidosDaPlanilha, ct);
        if (apelido is null)
        {
            linha.Erro($"Não foi possível gerar um apelido único a partir de \"{baseApelido}\".");
            return null;
        }
        if (d.Apelido is not null && !string.Equals(apelido, d.Apelido, StringComparison.OrdinalIgnoreCase))
            linha.Avisos.Add($"O apelido \"{d.Apelido}\" já existe; usamos \"{apelido}\".");
        apelidosDaPlanilha.Add(apelido);

        if (d.Retencoes is { } retencoes)
        {
            try
            {
                RegrasRetencoesFederais.ValidarPercentuais(retencoes);
            }
            catch (RegraNegocioException ex)
            {
                linha.Erro(ex.Message);
                return null;
            }
        }

        return new CadastrarClienteRequest(
            ctx.EmpresaId, documento, nome, apelido, d.Email, d.Telefone,
            codigoMunicipio, cep, logradouro, numero, complemento, bairro, uf,
            RetemIss: d.RetemIss ?? false,
            EnviarNfsePorEmail: d.EnviarPorEmail ?? d.Email is not null,
            EmailsCopia: d.Copias,
            CopiaOculta: false,
            Compartilhado: compartilhado,
            RetencoesFederais: d.Retencoes);
    }

    private async Task ConferirAtualizacaoAsync(
        LinhaTrabalho linha, Cliente ex, DadosCliente d, Contexto ctx, DisponibilidadeClienteResponse disponibilidade,
        Municipios municipios, HashSet<(Guid, string)> saidas, HashSet<string> apelidosDaPlanilha, CancellationToken ct)
    {
        if (linha.TemErro)
            return;
        if (!ctx.Permissoes.AlterarClientes)
        {
            linha.Erro("Você não tem permissão para alterar clientes.");
            return;
        }

        string? codigoMunicipio = ex.CodigoMunicipio;
        string? uf = d.Uf ?? NuloSeVazio(ex.Uf);
        if (d.CodigoMunicipio is not null || d.Municipio is not null)
        {
            codigoMunicipio = ResolverMunicipio(linha, d, municipios, new Dictionary<string, ResultadoConsulta<ConsultaCepResponse>>(), out var ufDoMunicipio);
            if (linha.TemErro)
                return;
            uf = d.Uf ?? ufDoMunicipio;
        }

        var apelido = ex.Apelido;
        if (d.Apelido is not null && !string.Equals(d.Apelido, ex.Apelido, StringComparison.OrdinalIgnoreCase))
        {
            if (apelidosDaPlanilha.Contains(d.Apelido) || await ApelidoCliente.EmUsoAsync(_db, ex.EmpresaId, d.Apelido, ex.CpfCnpj, ct))
            {
                linha.Erro($"Já existe outro cliente com o apelido \"{d.Apelido}\".");
                return;
            }
            apelido = d.Apelido;
        }
        apelidosDaPlanilha.Add(apelido);

        // Cópias: as novas entram junto das que já existem; quem pediu para sair não volta pela planilha.
        var copiasAtuais = EmailsCopiaCliente.Separar(ex.EmailsCopia);
        var email = d.Email ?? ex.Email;
        var copias = copiasAtuais.ToList();
        foreach (var copia in d.Copias)
        {
            if (copias.Contains(copia, StringComparer.OrdinalIgnoreCase) || string.Equals(copia, email, StringComparison.OrdinalIgnoreCase))
                continue;
            if (saidas.Contains((ex.Id, copia.ToLowerInvariant())))
            {
                linha.Avisos.Add($"{copia} pediu para sair das cópias deste cliente e não foi colocado de volta (para colocar, use o cadastro do cliente).");
                continue;
            }
            copias.Add(copia);
        }
        copias.RemoveAll(c => string.Equals(c, email, StringComparison.OrdinalIgnoreCase));

        var retencoesAtuais = RetencoesCliente.De(ex);
        PercentuaisRetencao? retencoes = d.Retencoes is { } r
            ? new PercentuaisRetencao(r.Irrf ?? retencoesAtuais.Irrf, r.Pis ?? retencoesAtuais.Pis, r.Cofins ?? retencoesAtuais.Cofins,
                r.Csll ?? retencoesAtuais.Csll, r.Inss ?? retencoesAtuais.Inss)
            : null;
        if (retencoes is not null)
        {
            try
            {
                RegrasRetencoesFederais.ValidarPercentuais(retencoes);
            }
            catch (RegraNegocioException erro)
            {
                linha.Erro(erro.Message);
                return;
            }
        }

        bool? compartilhado = disponibilidade.MostrarCampo && d.Compartilhado is { } c && c != ex.Compartilhado ? c : null;

        var numero = d.Numero ?? NuloSeVazio(ex.Numero);
        if (d.Logradouro is not null && numero is null)
            numero = "S/N";

        var pedido = new AtualizarClienteRequest(
            d.Nome ?? ex.Nome,
            apelido,
            email,
            d.Telefone ?? ex.Telefone,
            NuloSeVazio(codigoMunicipio),
            d.Cep ?? NuloSeVazio(ex.Cep),
            d.Logradouro ?? NuloSeVazio(ex.Logradouro),
            numero,
            d.Complemento ?? ex.Complemento,
            d.Bairro ?? NuloSeVazio(ex.Bairro),
            uf,
            RetemIss: d.RetemIss ?? ex.RetemIss,
            EnviarNfsePorEmail: d.EnviarPorEmail ?? (ex.EnviarNfsePorEmail || (ex.Email is null && d.Email is not null)),
            EmailsCopia: copias,
            CopiaOculta: ex.CopiaOculta,
            Compartilhado: compartilhado,
            EmpresaId: ctx.EmpresaId,
            RetencoesFederais: retencoes);

        AdicionarErros(linha, await ValidarAsync(_validarAtualizacaoCliente, pedido, ct));
        if (linha.TemErro)
            return;

        var mudancas = linha.Mudancas;
        void Comparar(string campo, string? antes, string? depois)
        {
            if (!string.Equals(NuloSeVazio(antes), NuloSeVazio(depois), StringComparison.Ordinal))
                mudancas.Add($"{campo}: {Mostrar(antes)} → {Mostrar(depois)}");
        }
        string Sn(bool v) => v ? "Sim" : "Não";
        string? Pct(decimal? v) => v?.ToString("0.##", PtBr);

        Comparar("Nome", ex.Nome, pedido.Nome);
        Comparar("Apelido", ex.Apelido, pedido.Apelido);
        Comparar("E-mail", ex.Email, pedido.Email);
        Comparar("Telefone", ex.Telefone, pedido.Telefone);
        Comparar("CEP", ex.Cep, pedido.Cep);
        Comparar("Logradouro", ex.Logradouro, pedido.Logradouro);
        Comparar("Número", ex.Numero, pedido.Numero);
        Comparar("Complemento", ex.Complemento, pedido.Complemento);
        Comparar("Bairro", ex.Bairro, pedido.Bairro);
        Comparar("Município (IBGE)", ex.CodigoMunicipio, pedido.CodigoMunicipio);
        Comparar("UF", ex.Uf, pedido.Uf);
        Comparar("Retém ISS", Sn(ex.RetemIss), Sn(pedido.RetemIss));
        Comparar("Enviar nota por e-mail", Sn(ex.EnviarNfsePorEmail), Sn(pedido.EnviarNfsePorEmail));
        Comparar("E-mails em cópia", string.Join("; ", copiasAtuais), string.Join("; ", copias));
        if (compartilhado is { } novoCompartilhado)
            Comparar("Disponível para", RegrasClienteCompartilhado.Texto(ex.Compartilhado), RegrasClienteCompartilhado.Texto(novoCompartilhado));
        if (retencoes is not null)
        {
            Comparar("IRRF %", Pct(retencoesAtuais.Irrf), Pct(retencoes.Irrf));
            Comparar("PIS %", Pct(retencoesAtuais.Pis), Pct(retencoes.Pis));
            Comparar("COFINS %", Pct(retencoesAtuais.Cofins), Pct(retencoes.Cofins));
            Comparar("CSLL %", Pct(retencoesAtuais.Csll), Pct(retencoes.Csll));
            Comparar("INSS %", Pct(retencoesAtuais.Inss), Pct(retencoes.Inss));
        }

        linha.Atualizacao = pedido;
        linha.Situacao = mudancas.Count == 0 ? SituacaoLinhaImportacao.SemMudanca : SituacaoLinhaImportacao.Atualiza;
        linha.Descricao = ApelidoENome(pedido.Apelido, pedido.Nome);
    }

    /// <summary>"Bom Pão — PADARIA BOM PÃO LTDA"; só o nome quando o apelido é o próprio nome.</summary>
    private static string ApelidoENome(string apelido, string nome) =>
        string.Equals(apelido.Trim(), nome.Trim(), StringComparison.OrdinalIgnoreCase) ? nome : $"{apelido} — {nome}";

    private static string Mostrar(string? valor) => string.IsNullOrWhiteSpace(valor) ? "(vazio)" : valor;

    private static string? NuloSeVazio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor;

    /// <summary>Código IBGE pela coluna do código, pelo nome + UF (tabela local) ou pelo CEP (ViaCEP).</summary>
    private static string? ResolverMunicipio(
        LinhaTrabalho linha, DadosCliente d, Municipios municipios, IReadOnlyDictionary<string, ResultadoConsulta<ConsultaCepResponse>> ceps, out string? uf)
    {
        uf = null;
        if (d.CodigoMunicipio is { } codigo)
        {
            if (!municipios.PorCodigo.TryGetValue(codigo, out var m))
            {
                linha.Erro($"O código IBGE {codigo} não existe.");
                return null;
            }
            if (d.Uf is not null && !string.Equals(d.Uf, m.Uf, StringComparison.OrdinalIgnoreCase))
            {
                linha.Erro($"O código IBGE {codigo} é de {m.Nome}/{m.Uf}, não de {d.Uf}.");
                return null;
            }
            uf = m.Uf;
            return codigo;
        }

        if (d.Municipio is { } nomeMunicipio)
        {
            var (nome, ufInformada) = RegrasImportacao.SepararMunicipio(nomeMunicipio, d.Uf);
            var candidatos = municipios.PorNome.TryGetValue(RegrasImportacao.ChaveMunicipio(nome), out var lista)
                ? lista.Where(m => ufInformada is null || string.Equals(m.Uf, ufInformada, StringComparison.OrdinalIgnoreCase)).ToList()
                : [];
            if (candidatos.Count == 1)
            {
                uf = candidatos[0].Uf;
                return candidatos[0].Codigo;
            }
            linha.Erro(candidatos.Count == 0
                ? $"Município não encontrado: {nomeMunicipio}{(ufInformada is null ? string.Empty : "/" + ufInformada)}. Confira o nome ou use o código IBGE."
                : $"Há mais de um município chamado {nome}: informe a UF.");
            return null;
        }

        if (d.Cep is { } cep && ceps.TryGetValue(cep, out var consulta))
        {
            if (consulta.Valor is { } endereco)
            {
                uf = endereco.Uf;
                linha.Avisos.Add($"Município pelo CEP: {endereco.Municipio}/{endereco.Uf}.");
                return endereco.CodigoMunicipio;
            }
            linha.Erro($"Falta o município, e não consegui buscar pelo CEP: {consulta.Erro}");
            return null;
        }

        linha.Erro("Falta o município (nome ou código IBGE).");
        return null;
    }

    private async Task<string?> EscolherApelidoAsync(Guid? escopo, string baseApelido, string documento, HashSet<string> daPlanilha, CancellationToken ct)
    {
        foreach (var candidato in RegrasImportacao.CandidatosApelido(baseApelido, documento, Cliente.TamanhoMaximoApelido))
        {
            if (candidato.Length == 0 || daPlanilha.Contains(candidato))
                continue;
            if (!await ApelidoCliente.EmUsoAsync(_db, escopo, candidato, documento, ct))
                return candidato;
        }
        return null;
    }

    // =====================================================================
    // Contratos
    // =====================================================================

    private sealed class GrupoContrato
    {
        public List<LinhaTrabalho> Linhas { get; } = [];
        public string? Numero { get; set; }
        public string? Documento { get; set; }
        public Guid? ClienteId { get; set; }
        public LinhaTrabalho? LinhaCliente { get; set; }
        public string ClienteTexto { get; set; } = string.Empty;
        public string? Nome { get; set; }
        public TipoCobrancaContrato Cobranca { get; set; } = TipoCobrancaContrato.Mensal;
        public DateOnly? Inicio { get; set; }
        public DateOnly? Fim { get; set; }
        public IndiceReajusteContrato? Indice { get; set; }
        public int Periodicidade { get; set; }
        public DateOnly? UltimoReajuste { get; set; }
        public StatusContrato Situacao { get; set; } = StatusContrato.Ativo;
        public string? Observacao { get; set; }
        public List<(Guid Id, string Descricao, decimal Quantidade, decimal Valor, bool Variavel)> Servicos { get; } = [];
        public CadastrarContratoRequest? Pedido { get; set; }
        public SituacaoLinhaImportacao Situacao_ { get; set; } = SituacaoLinhaImportacao.Nova;
    }

    private async Task<List<GrupoContrato>> ConferirContratosAsync(AbaLida aba, Contexto ctx, List<LinhaTrabalho> linhasClientes, CancellationToken ct)
    {
        var mapa = ColunasImportacao.Mapear(aba.Cabecalhos, ColunasImportacao.Contratos);
        ctx.MapaContratos = mapa;
        var hoje = DataBrasilia.Hoje();

        var existentes = ctx.ClientesExistentes
            ?? (await ClientesDaEmpresa.Visiveis(_db, ctx.EmpresaId).AsNoTracking().ToListAsync(ct))
                .GroupBy(c => c.CpfCnpj).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.EmpresaId.HasValue).First());
        var clientesDaPlanilha = linhasClientes.Where(l => l.Documento is not null)
            .GroupBy(l => l.Documento!).ToDictionary(g => g.Key, g => g.First());

        var servicos = (await _db.Servicos.AsNoTracking()
                .Where(s => s.EmpresaId == ctx.EmpresaId && s.Ativo)
                .Select(s => new { s.Id, s.Descricao })
                .ToListAsync(ct))
            .GroupBy(s => RegrasImportacao.ChaveServico(s.Descricao))
            .ToDictionary(g => g.Key, g => g.Select(s => (s.Id, s.Descricao)).ToList());

        var contratosDaEmpresa = await _db.Contratos.AsNoTracking()
            .Where(c => c.EmpresaId == ctx.EmpresaId)
            .Select(c => new { c.Id, c.Numero, c.ClienteId, c.Status })
            .ToListAsync(ct);
        var numerosExistentes = contratosDaEmpresa.Where(c => c.Numero != null)
            .Select(c => c.Numero!.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
        var idsVigentes = contratosDaEmpresa.Where(c => c.Status is StatusContrato.Ativo or StatusContrato.Suspenso).Select(c => c.Id).ToList();
        var composicoes = (await _db.ContratoServicos.AsNoTracking()
                .Where(cs => idsVigentes.Contains(cs.ContratoId))
                .Select(cs => new { cs.ContratoId, cs.ServicoId })
                .ToListAsync(ct))
            .GroupBy(cs => cs.ContratoId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ServicoId).ToHashSet());
        var vigentesPorCliente = contratosDaEmpresa
            .Where(c => c.Status is StatusContrato.Ativo or StatusContrato.Suspenso)
            .GroupBy(c => c.ClienteId)
            .ToDictionary(g => g.Key, g => g.Select(c => (c.Numero, Servicos: composicoes.GetValueOrDefault(c.Id) ?? [])).ToList());

        var faltando = mapa.Faltando;
        var grupos = new List<GrupoContrato>();
        var porNumero = new Dictionary<string, GrupoContrato>(StringComparer.Ordinal);

        foreach (var lida in aba.Linhas)
        {
            var linha = new LinhaTrabalho(AbaContratos, lida.Numero, Valores(lida, aba.Cabecalhos.Count));
            CelulaLida C(CampoContrato campo) => mapa.Celula(lida, campo);
            T? Ok<T>(Lido<T> lido)
            {
                if (!lido.Ok)
                    linha.Erros.Add(lido.Erro!);
                return lido.Valor;
            }

            var numero = NumeroContratoRegras.Normalizar(RegrasImportacao.LerTexto(C(CampoContrato.Numero)));
            if (numero is { Length: > NumeroContratoRegras.TamanhoMaximo })
                linha.Erros.Add($"Nº do contrato com mais de {NumeroContratoRegras.TamanhoMaximo} letras.");

            GrupoContrato grupo;
            var continuacao = false;
            if (numero is not null && porNumero.TryGetValue(numero.ToUpperInvariant(), out var jaVisto))
            {
                grupo = jaVisto;
                continuacao = true;
            }
            else
            {
                grupo = new GrupoContrato { Numero = numero };
                grupos.Add(grupo);
                if (numero is not null)
                    porNumero[numero.ToUpperInvariant()] = grupo;
            }
            grupo.Linhas.Add(linha);
            linha.Chave = numero ?? "(automático)";

            if (faltando.Count > 0)
            {
                linha.Erro($"Falta na planilha a coluna {string.Join(", ", faltando)}.");
                continue;
            }

            var doc = Ok(RegrasImportacao.LerDocumento(C(CampoContrato.CpfCnpjCliente)));
            var nome = RegrasImportacao.LerTexto(C(CampoContrato.Nome), 200);
            var cobranca = Ok(RegrasImportacao.LerCobranca(C(CampoContrato.Cobranca)));
            var inicio = Ok(RegrasImportacao.LerData(C(CampoContrato.Inicio)));
            var fim = Ok(RegrasImportacao.LerData(C(CampoContrato.Fim)));
            var indice = Ok(RegrasImportacao.LerIndice(C(CampoContrato.Indice)));
            var periodicidade = Ok(RegrasImportacao.LerInteiro(C(CampoContrato.Periodicidade)));
            var ultimo = Ok(RegrasImportacao.LerData(C(CampoContrato.UltimoReajuste)));
            var situacao = Ok(RegrasImportacao.LerSituacao(C(CampoContrato.Situacao)));
            var observacao = RegrasImportacao.LerTexto(C(CampoContrato.Observacao), 2000);

            var servicoTexto = RegrasImportacao.LerTexto(C(CampoContrato.Servico));
            var quantidade = Ok(RegrasImportacao.LerDecimal(C(CampoContrato.Quantidade))) ?? 1m;
            var valor = Ok(RegrasImportacao.LerDecimal(C(CampoContrato.ValorUnitario)));
            var variavel = Ok(RegrasImportacao.LerSimNao(C(CampoContrato.ValorVariavel))) ?? false;

            if (continuacao)
            {
                var primeira = grupo.Linhas[0].Linha;
                linha.Descricao = $"Mais um serviço do contrato da linha {primeira}";
                void Conferir(string campo, bool diferente)
                {
                    if (diferente)
                        linha.Erros.Add($"{campo} diferente da linha {primeira} (mesmo nº de contrato).");
                }
                Conferir("CPF/CNPJ do cliente", doc is not null && doc != grupo.Documento);
                Conferir("Início", inicio is not null && inicio != grupo.Inicio);
                Conferir("Cobrança", cobranca is not null && cobranca != grupo.Cobranca);
                Conferir("Índice", indice is not null && grupo.Indice is not null && indice != grupo.Indice);
            }
            else
            {
                if (doc is null && C(CampoContrato.CpfCnpjCliente).EstaVazia)
                    linha.Erros.Add("Falta o CPF/CNPJ do cliente.");
                if (inicio is null && C(CampoContrato.Inicio).EstaVazia)
                    linha.Erros.Add("Falta a data de início.");

                grupo.Documento = doc;
                grupo.Nome = nome;
                grupo.Cobranca = cobranca ?? TipoCobrancaContrato.Mensal;
                grupo.Inicio = inicio;
                grupo.Fim = fim;
                grupo.Situacao = situacao ?? StatusContrato.Ativo;
                grupo.Observacao = observacao;

                if (grupo.Cobranca == TipoCobrancaContrato.Mensal)
                {
                    grupo.Indice = indice ?? IndiceReajusteContrato.Ipca;
                    if (indice is null)
                        linha.Avisos.Add("Sem índice de reajuste: usamos IPCA.");
                    grupo.Periodicidade = periodicidade ?? 12;
                    if (grupo.Periodicidade is <= 0 or > 60)
                        linha.Erros.Add("Reajuste a cada (meses): de 1 a 60.");
                    grupo.UltimoReajuste = ultimo;
                }
                else
                {
                    if (indice is not null || periodicidade is > 0 || ultimo is not null)
                        linha.Avisos.Add("Contrato avulso não tem reajuste: ignoramos índice, periodicidade e último reajuste.");
                    grupo.Indice = null;
                    grupo.Periodicidade = 0;
                    grupo.UltimoReajuste = null;
                }

                if (grupo.Inicio is { } i && grupo.Fim is { } f && f < i)
                    linha.Erros.Add("O fim é antes do início.");

                // Cliente: já cadastrado ou na aba Clientes desta planilha.
                if (doc is not null)
                {
                    if (clientesDaPlanilha.TryGetValue(doc, out var linhaCliente))
                    {
                        grupo.LinhaCliente = linhaCliente;
                        grupo.ClienteId = linhaCliente.ClienteExistenteId;
                        grupo.ClienteTexto = linhaCliente.NovoCliente?.Apelido ?? linhaCliente.Atualizacao?.Apelido
                            ?? (linhaCliente.ClienteExistenteId is { } idExistente && existentes.Values.FirstOrDefault(c => c.Id == idExistente) is { } existente
                                ? existente.Apelido
                                : string.Empty);
                        if (linhaCliente.Situacao == SituacaoLinhaImportacao.Erro)
                            linha.Erros.Add($"O cliente tem erro na aba Clientes (linha {linhaCliente.Linha}).");
                    }
                    else if (existentes.TryGetValue(doc, out var cliente))
                    {
                        grupo.ClienteId = cliente.Id;
                        grupo.ClienteTexto = cliente.Apelido;
                    }
                    else
                    {
                        linha.Erros.Add($"Cliente {RegrasImportacao.FormatarDocumento(doc)} não encontrado: cadastre ou inclua na aba Clientes.");
                    }
                }
            }

            // Serviço da linha.
            if (servicoTexto is null)
            {
                linha.Erros.Add("Falta o serviço.");
            }
            else if (!servicos.TryGetValue(RegrasImportacao.ChaveServico(servicoTexto), out var achados))
            {
                linha.Erros.Add($"Serviço \"{servicoTexto}\" não encontrado nesta empresa. Cadastre em Serviços (ou escolha na lista do modelo).");
            }
            else if (achados.Count > 1)
            {
                linha.Erros.Add($"Há {achados.Count} serviços com a descrição \"{servicoTexto}\": deixe só um ativo em Serviços.");
            }
            else if (grupo.Servicos.Any(s => s.Id == achados[0].Id))
            {
                linha.Erros.Add($"O serviço \"{achados[0].Descricao}\" já está neste contrato (em outra linha).");
            }
            else if (valor is null)
            {
                if (C(CampoContrato.ValorUnitario).EstaVazia)
                    linha.Erros.Add("Falta o valor unitário.");
            }
            else if (valor <= 0)
            {
                linha.Erros.Add("O valor unitário tem que ser maior que zero.");
            }
            else if (quantidade <= 0)
            {
                linha.Erros.Add("A quantidade tem que ser maior que zero.");
            }
            else if (linha.Erros.Count == 0)
            {
                grupo.Servicos.Add((achados[0].Id, achados[0].Descricao, quantidade, decimal.Round(valor.Value, 2), variavel));
            }

            if (!continuacao)
                linha.Descricao = $"{grupo.ClienteTexto}{(grupo.ClienteTexto.Length > 0 ? " — " : string.Empty)}{servicoTexto}";
        }

        // Cada contrato (grupo) inteiro.
        foreach (var grupo in grupos)
        {
            var primeira = grupo.Linhas[0];
            if (!ctx.Permissoes.IncluirContratos && grupo.Linhas.All(l => !l.TemErro))
                primeira.Erro("Você não tem permissão para incluir contratos.");

            if (grupo.Linhas.All(l => !l.TemErro))
            {
                if (grupo.Numero is not null && numerosExistentes.Contains(grupo.Numero.ToUpperInvariant()))
                {
                    MarcarGrupo(grupo, SituacaoLinhaImportacao.JaExiste, $"Já existe o contrato nº {grupo.Numero} nesta empresa.");
                    continue;
                }
                if (grupo.Numero is null && grupo.ClienteId is { } clienteId
                    && vigentesPorCliente.TryGetValue(clienteId, out var vigentes)
                    && vigentes.Where(v => v.Servicos.Count > 0 && v.Servicos.SetEquals(grupo.Servicos.Select(s => s.Id)))
                        .Select(v => v.Numero ?? "sem número").FirstOrDefault() is { } numeroIgual)
                {
                    MarcarGrupo(grupo, SituacaoLinhaImportacao.JaExiste, $"Parece já cadastrado: o contrato nº {numeroIgual} tem o mesmo cliente e os mesmos serviços.");
                    continue;
                }

                if (grupo.Cobranca == TipoCobrancaContrato.Mensal && grupo.UltimoReajuste is null && grupo.Inicio is { } inicio
                    && RegrasImportacao.UltimoAniversario(inicio, grupo.Periodicidade, hoje) is { } aniversario)
                {
                    grupo.UltimoReajuste = aniversario;
                    if (grupo.Situacao is StatusContrato.Ativo or StatusContrato.Suspenso)
                        primeira.Avisos.Add($"Sem a data do último reajuste: consideramos o valor já reajustado em {aniversario:dd/MM/yyyy} (último aniversário). Se não foi, informe a coluna Último reajuste.");
                }

                var pedido = new CadastrarContratoRequest(
                    ctx.EmpresaId,
                    grupo.ClienteId ?? Guid.NewGuid(), // cliente novo da planilha: o id real entra na gravação
                    grupo.Nome,
                    grupo.Servicos.Select(s => new ContratoServicoRequest(s.Id, s.Quantidade, s.Valor, s.Variavel)).ToList(),
                    grupo.Inicio ?? hoje,
                    grupo.Periodicidade,
                    grupo.Indice,
                    null,
                    grupo.Observacao,
                    grupo.Situacao,
                    grupo.Fim,
                    grupo.Cobranca,
                    PermitirAlterarValorNaEmissao: true,
                    Numero: grupo.Numero,
                    DataUltimoReajuste: grupo.UltimoReajuste);

                AdicionarErros(primeira, await ValidarAsync(_validarContrato, pedido, ct));
                if (!primeira.TemErro)
                {
                    grupo.Pedido = pedido;
                    var total = grupo.Servicos.Sum(s => s.Quantidade * s.Valor);
                    var servicosTexto = string.Join(" + ", grupo.Servicos.Select(s => s.Descricao));
                    primeira.Descricao = $"{grupo.ClienteTexto} — {servicosTexto} — {total.ToString("C", PtBr)} {(grupo.Cobranca == TipoCobrancaContrato.Mensal ? "por mês" : "avulso")}";
                    MarcarGrupo(grupo, SituacaoLinhaImportacao.Nova, null);
                    continue;
                }
            }

            var comErro = grupo.Linhas.First(l => l.TemErro);
            foreach (var outra in grupo.Linhas.Where(l => !l.TemErro))
                outra.Erro($"Contrato não importado: corrija a linha {comErro.Linha}.");
            foreach (var l in grupo.Linhas)
                l.Situacao = SituacaoLinhaImportacao.Erro;
        }

        return grupos;
    }

    private static void MarcarGrupo(GrupoContrato grupo, SituacaoLinhaImportacao situacao, string? aviso)
    {
        grupo.Situacao_ = situacao;
        foreach (var linha in grupo.Linhas)
        {
            linha.Situacao = situacao;
            if (aviso is not null && linha == grupo.Linhas[0])
            {
                linha.Avisos.Clear();
                linha.Avisos.Add(aviso);
            }
        }
    }

    // =====================================================================
    // Gravação
    // =====================================================================

    private async Task GravarTudoAsync(string arquivo, Contexto ctx, List<LinhaTrabalho> clientes, List<GrupoContrato> contratos, CancellationToken ct)
    {
        var idsNovos = new Dictionary<string, Guid>(StringComparer.Ordinal);
        int cadastrados = 0, atualizados = 0, contratosCadastrados = 0, erros = 0;

        foreach (var linha in clientes)
        {
            try
            {
                if (linha.Situacao == SituacaoLinhaImportacao.Nova && linha.NovoCliente is { } novo)
                {
                    idsNovos[novo.CpfCnpj] = await _cadastrarCliente.ExecutarAsync(novo, ct);
                    cadastrados++;
                }
                else if (linha.Situacao == SituacaoLinhaImportacao.Atualiza && linha.Atualizacao is { } pedido && linha.ClienteExistenteId is { } id)
                {
                    await _atualizarCliente.ExecutarAsync(id, pedido, ct);
                    atualizados++;
                }
            }
            catch (Exception ex) when (ErroDeGravacao(ex))
            {
                _db.ChangeTracker.Clear();
                linha.Situacao = SituacaoLinhaImportacao.Erro;
                linha.Erro(MensagemDe(ex));
                erros++;
            }
        }

        foreach (var grupo in contratos.Where(g => g.Situacao_ == SituacaoLinhaImportacao.Nova && g.Pedido is not null))
        {
            var primeira = grupo.Linhas[0];
            var clienteId = grupo.LinhaCliente is { ClienteExistenteId: null, Documento: { } doc }
                ? idsNovos.TryGetValue(doc, out var novoId) ? novoId : (Guid?)null
                : grupo.ClienteId;
            if (clienteId is null)
            {
                MarcarErroGrupo(grupo, $"O cliente não foi cadastrado (veja a linha {grupo.LinhaCliente?.Linha} da aba Clientes).");
                erros++;
                continue;
            }

            try
            {
                var id = await _cadastrarContrato.ExecutarAsync(grupo.Pedido! with { ClienteId = clienteId.Value }, ct);
                var numero = await _db.Contratos.AsNoTracking().Where(c => c.Id == id).Select(c => c.Numero).FirstAsync(ct);
                grupo.Numero = numero;
                foreach (var linha in grupo.Linhas)
                    linha.Chave = numero ?? linha.Chave;
                contratosCadastrados++;
            }
            catch (Exception ex) when (ErroDeGravacao(ex))
            {
                _db.ChangeTracker.Clear();
                MarcarErroGrupo(grupo, MensagemDe(ex));
                erros++;
            }
        }

        _auditLogWriter.Registrar("ImportacaoPlanilha", "Empresa", ctx.EmpresaId, new
        {
            Arquivo = arquivo,
            ctx.Opcoes.AtualizarExistentes,
            ClientesCadastrados = cadastrados,
            ClientesAtualizados = atualizados,
            ContratosCadastrados = contratosCadastrados,
            LinhasComErroNaGravacao = erros
        });
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Importação por planilha na empresa {EmpresaId}: {Clientes} clientes novos, {Atualizados} atualizados, {Contratos} contratos, {Erros} erros na gravação.",
            ctx.EmpresaId, cadastrados, atualizados, contratosCadastrados, erros);
    }

    private static void MarcarErroGrupo(GrupoContrato grupo, string mensagem)
    {
        grupo.Situacao_ = SituacaoLinhaImportacao.Erro;
        foreach (var linha in grupo.Linhas)
        {
            linha.Situacao = SituacaoLinhaImportacao.Erro;
            linha.Erro(linha == grupo.Linhas[0] ? mensagem : $"Contrato não importado: veja a linha {grupo.Linhas[0].Linha}.");
        }
    }

    private static bool ErroDeGravacao(Exception ex) =>
        ex is RegraNegocioException or RecursoNaoEncontradoException or ValidacaoException or ValidationException or DbUpdateException;

    private static string MensagemDe(Exception ex) => ex switch
    {
        DbUpdateException => "Não foi possível gravar esta linha (conflito com outro cadastro). Confira e tente de novo.",
        ValidationException v => string.Join(" ", v.Errors.Select(e => e.ErrorMessage)),
        ValidacaoException v => string.Join(" ", v.Erros.SelectMany(e => e.Value)),
        _ => ex.Message
    };

    // =====================================================================
    // Consultas externas (BrasilAPI e ViaCEP), com cache
    // =====================================================================

    private sealed record ResultadoConsulta<T>(T? Valor, string? Erro) where T : class;

    private async Task<Dictionary<string, ResultadoConsulta<ConsultaCnpjResponse>>> ConsultarCnpjsAsync(IReadOnlyList<string> cnpjs, CancellationToken ct)
    {
        var lista = cnpjs.Distinct().Take(RegrasImportacao.MaximoConsultasCnpj).ToList();
        return await ConsultarVariosAsync(lista, "cnpj", c => _consultarCnpj.ExecutarAsync(c, ct), ct);
    }

    private async Task<Dictionary<string, ResultadoConsulta<ConsultaCepResponse>>> ConsultarCepsAsync(IReadOnlyList<string> ceps, CancellationToken ct) =>
        await ConsultarVariosAsync(ceps, "cep", c => _consultarCep.ExecutarAsync(c, ct), ct);

    private async Task<Dictionary<string, ResultadoConsulta<T>>> ConsultarVariosAsync<T>(
        IReadOnlyList<string> chaves, string tipo, Func<string, Task<T>> consultar, CancellationToken ct) where T : class
    {
        var resultado = new System.Collections.Concurrent.ConcurrentDictionary<string, ResultadoConsulta<T>>(StringComparer.Ordinal);
        using var semaforo = new SemaphoreSlim(ConsultasEmParalelo);

        await Task.WhenAll(chaves.Select(async chave =>
        {
            var chaveCache = $"importacao:{tipo}:{chave}";
            if (_cache.TryGetValue(chaveCache, out ResultadoConsulta<T>? emCache) && emCache is not null)
            {
                resultado[chave] = emCache;
                return;
            }

            await semaforo.WaitAsync(ct);
            try
            {
                ResultadoConsulta<T> obtido;
                try
                {
                    obtido = new ResultadoConsulta<T>(await consultar(chave), null);
                }
                catch (Exception ex) when (ex is RegraNegocioException or RecursoNaoEncontradoException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    obtido = new ResultadoConsulta<T>(null, ex.Message);
                }
                _cache.Set(chaveCache, obtido, TempoCacheConsulta);
                resultado[chave] = obtido;
            }
            finally
            {
                semaforo.Release();
            }
        }));

        return new Dictionary<string, ResultadoConsulta<T>>(resultado, StringComparer.Ordinal);
    }

    // =====================================================================
    // Apoio
    // =====================================================================

    private sealed record MunicipioLido(string Codigo, string Nome, string Uf);

    private sealed record Municipios(IReadOnlyDictionary<string, MunicipioLido> PorCodigo, IReadOnlyDictionary<string, List<MunicipioLido>> PorNome);

    private async Task<Municipios> CarregarMunicipiosAsync(CancellationToken ct)
    {
        var todos = await _db.Municipios.AsNoTracking()
            .Select(m => new MunicipioLido(m.Codigo, m.Nome, m.Uf))
            .ToListAsync(ct);
        return new Municipios(
            todos.ToDictionary(m => m.Codigo, StringComparer.Ordinal),
            todos.GroupBy(m => RegrasImportacao.ChaveMunicipio(m.Nome)).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal));
    }

    private static async Task<IReadOnlyList<string>> ValidarAsync<T>(IValidator<T> validador, T pedido, CancellationToken ct)
    {
        // Mensagens padrão do FluentValidation em português, como nas telas.
        var culturaAnterior = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = PtBr;
        try
        {
            var resultado = await validador.ValidateAsync(pedido, ct);
            return resultado.Errors.Select(e => e.ErrorMessage).Distinct().ToList();
        }
        finally
        {
            CultureInfo.CurrentUICulture = culturaAnterior;
        }
    }

    private static void AdicionarErros(LinhaTrabalho linha, IReadOnlyList<string> erros)
    {
        foreach (var erro in erros)
            linha.Erro(erro);
    }

    private static IReadOnlyList<string> Valores(LinhaLida lida, int colunas) =>
        Enumerable.Range(0, colunas).Select(i => lida.Celula(i).Texto).ToList();

    private static TotaisImportacao Totais(IReadOnlyList<LinhaTrabalho> linhas) => new(
        linhas.Count(l => l.Situacao == SituacaoLinhaImportacao.Nova),
        linhas.Count(l => l.Situacao == SituacaoLinhaImportacao.Atualiza),
        linhas.Count(l => l.Situacao == SituacaoLinhaImportacao.JaExiste),
        linhas.Count(l => l.Situacao == SituacaoLinhaImportacao.SemMudanca),
        linhas.Count(l => l.Situacao == SituacaoLinhaImportacao.Erro));

    private static AbaImportacaoResponse MontarAba<TCampo>(
        string tipo, AbaLida aba, MapaColunas<TCampo> mapa, IReadOnlyList<ColunaImportacao<TCampo>> colunas, IReadOnlyList<LinhaTrabalho> linhas, bool gravado)
        where TCampo : struct, Enum
    {
        var reconhecidas = mapa.Indices
            .OrderBy(kv => kv.Value)
            .Select(kv => (Cabecalho: aba.Cabecalhos[kv.Value], Titulo: colunas.First(c => c.Campo.Equals(kv.Key)).Titulo))
            .Where(x => ColunasImportacao.ChaveCabecalho(x.Cabecalho) != ColunasImportacao.ChaveCabecalho(x.Titulo))
            .Select(x => $"{x.Cabecalho} → {x.Titulo}")
            .ToList();

        // Nome do cliente pode vir da Receita (CNPJ): não é "faltando" da planilha.
        var faltando = mapa.Faltando.Where(f => tipo != AbaClientes || f != ColunasImportacao.Clientes.First(c => c.Campo == CampoCliente.Nome).Titulo).ToList();

        return new AbaImportacaoResponse(
            tipo,
            aba.Nome,
            aba.Cabecalhos,
            mapa.Ignoradas,
            faltando,
            reconhecidas,
            linhas.Select(l => new LinhaImportacaoResponse(
                l.Aba, l.Linha, l.Situacao, TextoSituacao(l.Situacao, gravado), l.Chave, l.Descricao,
                l.Erros.Distinct().ToList(), l.Avisos.Distinct().ToList(), l.Mudancas, l.Valores)).ToList());
    }

    private static string TextoSituacao(SituacaoLinhaImportacao situacao, bool gravado) => (situacao, gravado) switch
    {
        (SituacaoLinhaImportacao.Nova, false) => "Novo",
        (SituacaoLinhaImportacao.Nova, true) => "Cadastrado",
        (SituacaoLinhaImportacao.Atualiza, false) => "Atualiza",
        (SituacaoLinhaImportacao.Atualiza, true) => "Atualizado",
        (SituacaoLinhaImportacao.JaExiste, _) => "Já existe",
        (SituacaoLinhaImportacao.SemMudanca, _) => "Sem mudança",
        _ => "Erro"
    };

    private sealed class Contexto(Guid empresaId, Guid tenantId, OpcoesImportacao opcoes, PermissoesImportacao permissoes)
    {
        public Guid EmpresaId { get; } = empresaId;
        public Guid TenantId { get; } = tenantId;
        public OpcoesImportacao Opcoes { get; } = opcoes;
        public PermissoesImportacao Permissoes { get; } = permissoes;
        public MapaColunas<CampoCliente>? MapaClientes { get; set; }
        public MapaColunas<CampoContrato>? MapaContratos { get; set; }
        public Dictionary<string, Cliente>? ClientesExistentes { get; set; }
    }

    private sealed class LinhaTrabalho(string aba, int linha, IReadOnlyList<string> valores)
    {
        public string Aba { get; } = aba;
        public int Linha { get; } = linha;
        public IReadOnlyList<string> Valores { get; } = valores;
        public SituacaoLinhaImportacao Situacao { get; set; } = SituacaoLinhaImportacao.Erro;
        public string Chave { get; set; } = string.Empty;
        public string Descricao { get; set; } = string.Empty;
        public List<string> Erros { get; } = [];
        public List<string> Avisos { get; } = [];
        public List<string> Mudancas { get; } = [];
        public string? Documento { get; set; }
        public Guid? ClienteExistenteId { get; set; }
        public CadastrarClienteRequest? NovoCliente { get; set; }
        public AtualizarClienteRequest? Atualizacao { get; set; }

        public bool TemErro => Erros.Count > 0;

        public void Erro(string mensagem)
        {
            Erros.Add(mensagem);
            Situacao = SituacaoLinhaImportacao.Erro;
        }
    }
}
