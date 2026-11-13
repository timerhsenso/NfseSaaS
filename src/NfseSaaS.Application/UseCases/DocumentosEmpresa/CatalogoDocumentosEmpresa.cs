using System.Text.RegularExpressions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.DocumentosEmpresa;

/// <summary>Como o documento está hoje.</summary>
public enum SituacaoDocumentoEmpresa
{
    /// <summary>Não tem data de validade (contrato social, cartão CNPJ...).</summary>
    SemValidade = 1,

    Valido = 2,

    /// <summary>Vence em até <see cref="RegrasDocumentoEmpresa.DiasVenceEmBreve"/> dias (inclusive hoje).</summary>
    VenceEmBreve = 3,

    Vencido = 4
}

/// <summary>
/// Um tipo de documento da Empresa.
/// </summary>
/// <param name="ExigeValidade">Certidões: a validade vem escrita no documento e é obrigatória.</param>
/// <param name="ValidadeSugeridaDias">Dias contados da emissão quando o órgão usa prazo fixo (nulo = varia; o usuário digita).</param>
/// <param name="LinkEmissao">Página oficial onde se tira o documento (nulo = depende do estado/município).</param>
public sealed record TipoDocumentoInfo(
    TipoDocumentoEmpresa Tipo,
    string Nome,
    string Orgao,
    bool ExigeValidade,
    int? ValidadeSugeridaDias,
    string? LinkEmissao);

/// <summary>
/// DOC1: catálogo dos tipos de documento (fica no código: nome, órgão,
/// prazo de validade usual e link oficial). Prazos usuais: certidão
/// federal 180 dias, CRF do FGTS 30 dias, CNDT 180 dias; estadual e
/// municipal variam (a data vem escrita na certidão).
/// </summary>
public static class CatalogoDocumentosEmpresa
{
    public static readonly IReadOnlyList<TipoDocumentoInfo> Todos = new[]
    {
        new TipoDocumentoInfo(TipoDocumentoEmpresa.CndFederal, "Certidão federal (Receita/PGFN)", "Receita Federal",
            true, 180, "https://servicos.receitafederal.gov.br/servico/certidoes/#/home"),
        new TipoDocumentoInfo(TipoDocumentoEmpresa.CndEstadual, "Certidão estadual", "SEFAZ do estado",
            true, null, null),
        new TipoDocumentoInfo(TipoDocumentoEmpresa.CndMunicipal, "Certidão municipal", "Prefeitura",
            true, null, null),
        new TipoDocumentoInfo(TipoDocumentoEmpresa.CrfFgts, "Regularidade do FGTS (CRF)", "Caixa",
            true, 30, "https://consulta-crf.caixa.gov.br/consultacrf/pages/consultaEmpregador.jsf"),
        new TipoDocumentoInfo(TipoDocumentoEmpresa.Cndt, "Certidão trabalhista (CNDT)", "TST",
            true, 180, "https://cndt-certidao.tst.jus.br/"),
        new TipoDocumentoInfo(TipoDocumentoEmpresa.SimplesNacional, "Opção pelo Simples Nacional", "Receita Federal",
            false, null, "https://www8.receita.fazenda.gov.br/SimplesNacional/aplicacoes.aspx?id=21"),
        new TipoDocumentoInfo(TipoDocumentoEmpresa.ContratoSocial, "Contrato social", "Junta Comercial",
            false, null, null),
        new TipoDocumentoInfo(TipoDocumentoEmpresa.CartaoCnpj, "Cartão CNPJ", "Receita Federal",
            false, null, "https://solucoes.receita.fazenda.gov.br/Servicos/cnpjreva/Cnpjreva_Solicitacao.asp"),
        new TipoDocumentoInfo(TipoDocumentoEmpresa.Outro, "Outro documento", "—",
            false, null, null)
    };

    public static TipoDocumentoInfo Obter(TipoDocumentoEmpresa tipo) =>
        Todos.FirstOrDefault(t => t.Tipo == tipo)
        ?? throw new RegraNegocioException("Tipo de documento desconhecido.");

    /// <summary>Tipos fixos (todos menos "Outro") — aparecem na tela mesmo sem arquivo, como lista do que guardar.</summary>
    public static IEnumerable<TipoDocumentoInfo> Fixos => Todos.Where(t => t.Tipo != TipoDocumentoEmpresa.Outro);
}

/// <summary>Regras puras do DOC1 (sem banco nem disco).</summary>
public static partial class RegrasDocumentoEmpresa
{
    public const int DiasVenceEmBreve = 15;
    public const long TamanhoMaximoBytes = 5 * 1024 * 1024;
    public const int NomeMinimo = 3;
    public const int NomeMaximo = 80;
    public const int ObservacaoMaxima = 300;
    public const int NomeArquivoMaximo = 200;

    /// <summary>Faixa do aviso por e-mail: vence em até 15 dias, e vencido (cada uma sai uma vez por versão).</summary>
    public const int FaixaVencido = 0;
    public const int FaixaVenceEmBreve = 15;

    public static int? DiasParaVencer(DateOnly? validaAte, DateOnly hoje) =>
        validaAte is { } v ? v.DayNumber - hoje.DayNumber : null;

    public static SituacaoDocumentoEmpresa Situacao(DateOnly? validaAte, DateOnly hoje) =>
        DiasParaVencer(validaAte, hoje) switch
        {
            null => SituacaoDocumentoEmpresa.SemValidade,
            < 0 => SituacaoDocumentoEmpresa.Vencido,
            <= DiasVenceEmBreve => SituacaoDocumentoEmpresa.VenceEmBreve,
            _ => SituacaoDocumentoEmpresa.Valido
        };

    public static string TextoSituacao(SituacaoDocumentoEmpresa situacao, int? dias) => situacao switch
    {
        SituacaoDocumentoEmpresa.SemValidade => "Sem validade",
        SituacaoDocumentoEmpresa.Vencido => dias == -1 ? "Venceu ontem" : $"Vencido há {-dias} dias",
        SituacaoDocumentoEmpresa.VenceEmBreve => dias switch { 0 => "Vence hoje", 1 => "Vence amanhã", _ => $"Vence em {dias} dias" },
        _ => "Válido"
    };

    /// <summary>Faixa de aviso em que o documento está, ou null (sem validade ou ainda longe).</summary>
    public static int? FaixaAviso(DateOnly? validaAte, DateOnly hoje) =>
        Situacao(validaAte, hoje) switch
        {
            SituacaoDocumentoEmpresa.Vencido => FaixaVencido,
            SituacaoDocumentoEmpresa.VenceEmBreve => FaixaVenceEmBreve,
            _ => null
        };

    /// <summary>Validade usual a partir da emissão (ex.: FGTS = emissão + 30 dias). Nulo quando o tipo não tem prazo fixo.</summary>
    public static DateOnly? ValidadeSugerida(TipoDocumentoEmpresa tipo, DateOnly? dataEmissao) =>
        dataEmissao is { } d && CatalogoDocumentosEmpresa.Obter(tipo).ValidadeSugeridaDias is { } dias
            ? ValidadePorDias(d, dias)
            : null;

    /// <summary>Maior prazo aceito em "vale por N dias" (10 anos).</summary>
    public const int ValidadeMaximaDias = 3650;

    /// <summary>
    /// "Vale por N dias" a partir da emissão → data final (mesma conta da tela:
    /// emissão + N dias). Fora de 1 a <see cref="ValidadeMaximaDias"/> = nulo.
    /// </summary>
    public static DateOnly? ValidadePorDias(DateOnly emissao, int? dias) =>
        dias is >= 1 and <= ValidadeMaximaDias ? emissao.AddDays(dias.Value) : null;

    /// <summary>Data final → quantos dias de validade (o inverso de <see cref="ValidadePorDias"/>).</summary>
    public static int? DiasDeValidade(DateOnly emissao, DateOnly? validaAte) =>
        validaAte is { } v && v >= emissao ? v.DayNumber - emissao.DayNumber : null;

    /// <summary>Chave do "lugar" do documento (ver DocumentoEmpresa.NomeChave).</summary>
    public static string NomeChave(TipoDocumentoEmpresa tipo, string? nomePersonalizado) =>
        tipo == TipoDocumentoEmpresa.Outro
            ? EspacosRepetidos().Replace((nomePersonalizado ?? string.Empty).Trim(), " ").ToLowerInvariant()
            : string.Empty;

    /// <summary>Título para a tela e o e-mail: nome do tipo, ou o nome dado no "Outro".</summary>
    public static string Titulo(TipoDocumentoEmpresa tipo, string? nomePersonalizado) =>
        tipo == TipoDocumentoEmpresa.Outro && !string.IsNullOrWhiteSpace(nomePersonalizado)
            ? nomePersonalizado.Trim()
            : CatalogoDocumentosEmpresa.Obter(tipo).Nome;

    /// <summary>Dados do envio conferidos e completos (validade sugerida aplicada quando falta).</summary>
    public sealed record DadosValidados(string? NomePersonalizado, string NomeChave, DateOnly? DataEmissao, DateOnly? ValidaAte, string? Observacao);

    public static DadosValidados Validar(
        TipoDocumentoEmpresa tipo, string? nomePersonalizado, DateOnly? dataEmissao, DateOnly? validaAte, string? observacao, DateOnly hoje)
    {
        var info = CatalogoDocumentosEmpresa.Obter(tipo);

        string? nome = null;
        if (tipo == TipoDocumentoEmpresa.Outro)
        {
            nome = EspacosRepetidos().Replace((nomePersonalizado ?? string.Empty).Trim(), " ");
            if (nome.Length < NomeMinimo || nome.Length > NomeMaximo)
                throw new RegraNegocioException($"Dê um nome ao documento ({NomeMinimo} a {NomeMaximo} letras), ex.: \"Alvará de funcionamento\".");
        }

        // Todo documento tem data de emissão: é dela que sai a validade em dias e o histórico de versões.
        if (dataEmissao is not { } emissao)
            throw new RegraNegocioException("Informe a data de emissão do documento (vem escrita nele).");
        if (emissao > hoje)
            throw new RegraNegocioException("A data de emissão não pode ser depois de hoje.");

        var validade = validaAte ?? ValidadeSugerida(tipo, dataEmissao);
        if (info.ExigeValidade && validade is null)
            throw new RegraNegocioException($"Informe até quando a {info.Nome.ToLowerInvariant()} vale (a data vem escrita na certidão).");

        if (validade is { } v && dataEmissao is { } e && v < e)
            throw new RegraNegocioException("A validade não pode ser antes da data de emissão.");

        var obs = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim();
        if (obs is { Length: > ObservacaoMaxima })
            throw new RegraNegocioException($"A observação pode ter até {ObservacaoMaxima} caracteres.");

        return new DadosValidados(nome, NomeChave(tipo, nome), dataEmissao, validade, obs);
    }

    /// <summary>Só PDF de verdade (começa com "%PDF-"), não vazio e até 5 MB.</summary>
    public static void ValidarArquivo(string? nomeArquivo, byte[]? conteudo)
    {
        if (conteudo is null || conteudo.Length == 0)
            throw new RegraNegocioException("Escolha o arquivo do documento (PDF).");
        if (conteudo.LongLength > TamanhoMaximoBytes)
            throw new RegraNegocioException("O arquivo pode ter até 5 MB.");
        if (conteudo.Length < 5 || conteudo[0] != '%' || conteudo[1] != 'P' || conteudo[2] != 'D' || conteudo[3] != 'F' || conteudo[4] != '-')
            throw new RegraNegocioException("Envie o documento em PDF (o arquivo escolhido não é um PDF).");
    }

    /// <summary>Nome do arquivo só para exibir/baixar: sem pasta, sem caracteres estranhos, terminando em .pdf.</summary>
    public static string NomeArquivoSeguro(string? nomeArquivo)
    {
        var nome = Path.GetFileNameWithoutExtension(Path.GetFileName((nomeArquivo ?? string.Empty).Replace('\\', '/')));
        nome = CaracteresProibidos().Replace(nome, "_").Trim(' ', '.', '_');
        if (nome.Length == 0)
            nome = "documento";
        if (nome.Length > NomeArquivoMaximo - 4)
            nome = nome[..(NomeArquivoMaximo - 4)];
        return nome + ".pdf";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex EspacosRepetidos();

    [GeneratedRegex(@"[^\p{L}\p{N} ._()\-]")]
    private static partial Regex CaracteresProibidos();
}
