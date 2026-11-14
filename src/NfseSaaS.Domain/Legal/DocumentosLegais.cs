namespace NfseSaaS.Domain.Legal;

/// <summary>Documentos que o usuário aceita (JUR1). Termos e Política andam juntos (mesma versão); os outros têm versão própria.</summary>
public enum TipoDocumentoLegal
{
    TermosDeUso = 1,
    PoliticaPrivacidade = 2,

    /// <summary>Acordo de Tratamento de Dados Pessoais: um por conta, aceito pelo administrador.</summary>
    Dpa = 3,

    /// <summary>Autorização de custódia e uso do certificado A1: uma por empresa (e certificado).</summary>
    AutorizacaoCertificado = 4,

    /// <summary>Autorização de emissão automática (Nota Mensal sem aprovação nota a nota): uma por empresa.</summary>
    AutorizacaoEmissaoAutomatica = 5
}

/// <summary>
/// Uma versão dos documentos legais. <paramref name="PublicadaEm"/> é o dia
/// em que o texto aparece no site e o aviso prévio começa;
/// <paramref name="VigenteDesde"/> é o dia em que passa a valer (pelo menos 15
/// dias depois, Termos item 17). Os instrumentos (DPA e autorizações) nulos
/// não existiam naquela versão.
/// </summary>
public sealed record VersaoDocumentosLegais(
    string Versao,
    DateOnly PublicadaEm,
    DateOnly VigenteDesde,
    string ResumoMudancas,
    string? VersaoDpa = null,
    string? VersaoAutorizacaoCertificado = null,
    string? VersaoAutorizacaoEmissaoAutomatica = null)
{
    public bool Vigente(DateOnly hoje) => hoje >= VigenteDesde;

    public bool Publicada(DateOnly hoje) => hoje >= PublicadaEm;

    /// <summary>Nome da view do texto desta versão (Views/Legal/Termos_1_2.cshtml).</summary>
    public string SufixoView => Versao.Replace('.', '_');
}

/// <summary>
/// Versões dos Termos de uso, da Política de privacidade e dos instrumentos
/// (DPA, Autorização do certificado A1, Autorização de emissão automática),
/// e os dados do responsável que aparecem nos textos (Views/Legal). É o ÚNICO
/// lugar a mudar quando os textos forem revisados.
///
/// Regra de versão: mudou alguma coisa que altera direito ou obrigação de
/// quem usa (preço, responsabilidade, dado tratado, compartilhamento,
/// retenção), acrescente uma versão em <see cref="Versoes"/> com o texto novo
/// em Views/Legal (Termos_X_Y, Privacidade_X_Y) e os instrumentos novos em
/// Application/Legal/Textos. Ajuste que não muda nada (erro de digitação) não
/// precisa de versão.
///
/// Publicação e vigência por data (JUR1): a versão nova pode subir no deploy
/// antes da hora. Até <see cref="VersaoDocumentosLegais.PublicadaEm"/> ninguém
/// a vê; de lá até <see cref="VersaoDocumentosLegais.VigenteDesde"/> é o aviso
/// prévio (texto no site, aviso no topo, e-mail, aceite antecipado); a partir
/// da vigência, quem não aceitou ela (ou uma mais nova) vai para a tela de
/// aceite (ExigeAceiteTermosFilter). Aceitar uma versão mais nova vale para
/// as anteriores.
/// </summary>
public static class DocumentosLegais
{
    /// <summary>Todas as versões, da mais antiga para a mais nova.</summary>
    public static readonly IReadOnlyList<VersaoDocumentosLegais> Versoes =
    [
        new("1.0", new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4),
            "Primeira versão."),
        new("1.1", new DateOnly(2026, 11, 8), new DateOnly(2026, 11, 24),
            "Mudam as regras do teste grátis, do preço de fundador, do atraso e do cancelamento; entram o \"Indique e ganhe\" e os convites por e-mail; e o responsável pela plataforma passa a ser a LFMS - Consultoria e Desenvolvimento de Sistemas Ltda."),
        new("1.2", new DateOnly(2026, 11, 24), new DateOnly(2026, 12, 9),
            "A emissão passa a ser assistida (cada nota confirmada por um usuário), e a automática fica opcional, com autorização própria; o certificado digital e o tratamento dos dados dos seus clientes ganham documentos próprios (Autorização do certificado A1 e Acordo de Tratamento de Dados); e ficam definidos o suporte, a guarda dos documentos e o registro dos aceites.",
            VersaoDpa: "1.0", VersaoAutorizacaoCertificado: "1.0", VersaoAutorizacaoEmissaoAutomatica: "1.0")
    ];

    /// <summary>A versão mais nova já publicada (o texto que /termos mostra hoje).</summary>
    public static VersaoDocumentosLegais Atual(DateOnly hoje) => Versoes.Last(v => v.Publicada(hoje));

    /// <summary>A versão que vale hoje (a que precisa estar aceita para usar o sistema).</summary>
    public static VersaoDocumentosLegais Vigente(DateOnly hoje) => Versoes.Last(v => v.Vigente(hoje));

    /// <summary>Uma versão pelo número (erro se não existir: uso nas views de cada versão).</summary>
    public static VersaoDocumentosLegais Versao(string versao) => Versoes.First(v => v.Versao == versao);

    /// <summary>Uma versão publicada até hoje, pelo número ("1.1"). Nulo = não existe ou ainda não foi publicada.</summary>
    public static VersaoDocumentosLegais? Publicada(string? versao, DateOnly hoje) =>
        Versoes.FirstOrDefault(v => v.Versao == versao && v.Publicada(hoje));

    /// <summary>A versão anterior a esta (nulo na primeira).</summary>
    public static VersaoDocumentosLegais? Anterior(VersaoDocumentosLegais versao)
    {
        var indice = Ordem(versao.Versao);
        return indice > 0 ? Versoes[indice - 1] : null;
    }

    /// <summary>Posição da versão na lista (−1 = desconhecida).</summary>
    public static int Ordem(string? versao)
    {
        for (var i = 0; i < Versoes.Count; i++)
        {
            if (Versoes[i].Versao == versao)
                return i;
        }
        return -1;
    }

    /// <summary>O aceite desta versão (dos Termos e da Política) libera o uso hoje? Vale a versão vigente ou qualquer mais nova.</summary>
    public static bool AceiteLibera(string? versaoAceita, DateOnly hoje) =>
        Ordem(versaoAceita) >= Ordem(Vigente(hoje).Versao);

    /// <summary>Há versão nova publicada que ainda não vale (aviso prévio)?</summary>
    public static bool EmAvisoPrevio(DateOnly hoje) => !Atual(hoje).Vigente(hoje);

    /// <summary>
    /// Versão de um instrumento (DPA ou autorizações) no texto atual. Nula =
    /// o instrumento ainda não existe na versão publicada. Termos e Política
    /// usam a própria versão.
    /// </summary>
    public static string? VersaoInstrumento(TipoDocumentoLegal tipo, VersaoDocumentosLegais versao) => tipo switch
    {
        TipoDocumentoLegal.TermosDeUso or TipoDocumentoLegal.PoliticaPrivacidade => versao.Versao,
        TipoDocumentoLegal.Dpa => versao.VersaoDpa,
        TipoDocumentoLegal.AutorizacaoCertificado => versao.VersaoAutorizacaoCertificado,
        TipoDocumentoLegal.AutorizacaoEmissaoAutomatica => versao.VersaoAutorizacaoEmissaoAutomatica,
        _ => null
    };

    /// <summary>Nome do documento como aparece nas telas e no comprovante.</summary>
    public static string Nome(TipoDocumentoLegal tipo) => tipo switch
    {
        TipoDocumentoLegal.TermosDeUso => "Termos de uso",
        TipoDocumentoLegal.PoliticaPrivacidade => "Política de privacidade",
        TipoDocumentoLegal.Dpa => "Acordo de Tratamento de Dados Pessoais (DPA)",
        TipoDocumentoLegal.AutorizacaoCertificado => "Autorização de custódia e uso do certificado digital A1",
        TipoDocumentoLegal.AutorizacaoEmissaoAutomatica => "Autorização de emissão automática de NFS-e",
        _ => tipo.ToString()
    };

    /// <summary>Endereço público do texto de uma versão (/termos/1.2, /dpa/1.0).</summary>
    public static string Url(TipoDocumentoLegal tipo, string versao) => tipo switch
    {
        TipoDocumentoLegal.TermosDeUso => $"/termos/{versao}",
        TipoDocumentoLegal.PoliticaPrivacidade => $"/privacidade/{versao}",
        TipoDocumentoLegal.Dpa => $"/dpa/{versao}",
        TipoDocumentoLegal.AutorizacaoCertificado => $"/autorizacao-certificado/{versao}",
        TipoDocumentoLegal.AutorizacaoEmissaoAutomatica => $"/autorizacao-emissao-automatica/{versao}",
        _ => "/"
    };

    /// <summary>
    /// Razão social do responsável pela plataforma (controlador dos dados de
    /// conta, na LGPD). Enquanto estiver com colchetes, a subida da aplicação
    /// registra um aviso no log (ver Program.cs).
    /// </summary>
    public const string ResponsavelNome = "LFMS - Consultoria e Desenvolvimento de Sistemas Ltda";

    /// <summary>CNPJ do responsável (a empresa que vende os planos e emite as notas das mensalidades).</summary>
    public const string ResponsavelCnpj = "04.747.304/0001-78";

    /// <summary>Cidade da sede do responsável.</summary>
    public const string Sede = "Lauro de Freitas/BA";

    public const string EmailPrivacidade = "lgpd@micoempresa.com.br";

    /// <summary>Suporte e avisos contratuais (Termos 1.2, itens 8 e 20).</summary>
    public const string EmailSuporte = "suporte@micoempresa.com.br";

    public const string NomeServico = "micoempresa";

    public const string Site = "micoempresa.com.br";

    public const string Foro = "Salvador/BA";

    public static bool PossuiMarcadorPendente => ResponsavelNome.Contains('[');
}
