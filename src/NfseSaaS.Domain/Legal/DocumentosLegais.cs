namespace NfseSaaS.Domain.Legal;

/// <summary>
/// Versão atual dos Termos de uso e da Política de privacidade, e os dados
/// do responsável que aparecem nos dois textos (Views/Legal). É o ÚNICO lugar
/// a mudar quando os textos forem revisados.
///
/// Regra de versão: mudou alguma coisa que altera direito ou obrigação de
/// quem usa (preço, responsabilidade, dado tratado, compartilhamento,
/// retenção), suba a versão. Cada aceite fica registrado em AceiteTermos
/// (versão, data, IP, navegador). Ajuste de texto que não muda nada (erro de
/// digitação, formatação) não precisa de versão nova.
///
/// Aviso prévio (Termos, item 14): a versão nova é publicada com
/// <see cref="VigentesDesde"/> no futuro (pelo menos 15 dias). Até lá, quem
/// aceitou a versão anterior continua usando normalmente (vê um aviso para
/// ler e aceitar antes); a partir da data, a tela de aceite passa a ser
/// obrigatória para quem ainda não aceitou (ExigeAceiteTermosFilter). Quem
/// se cadastra no meio do caminho já aceita a versão nova.
/// </summary>
public static class DocumentosLegais
{
    public const string VersaoTermosDeUso = "1.1";

    public const string VersaoPoliticaPrivacidade = "1.1";

    /// <summary>Dia em que a versão atual passa (ou passou) a valer. Atualize junto com a versão.</summary>
    public static readonly DateOnly VigentesDesde = new(2026, 11, 24);

    /// <summary>Versão que vale até a véspera de <see cref="VigentesDesde"/> (texto guardado em /termos/1.0).</summary>
    public const string VersaoAnteriorTermosDeUso = "1.0";

    /// <inheritdoc cref="VersaoAnteriorTermosDeUso"/>
    public const string VersaoAnteriorPoliticaPrivacidade = "1.0";

    /// <summary>Data em que a versão anterior passou a valer.</summary>
    public static readonly DateOnly AnterioresVigentesDesde = new(2026, 11, 4);

    /// <summary>Uma frase com o que mudou na versão atual (aviso no topo das telas e e-mail de aviso prévio). Atualize junto com a versão.</summary>
    public const string ResumoMudancas =
        "Mudam as regras do teste grátis, do preço de fundador, do atraso e do cancelamento; entram o \"Indique e ganhe\" e os convites por e-mail; e o responsável pela plataforma passa a ser a LFMS - Consultoria e Desenvolvimento de Sistemas Ltda.";

    /// <summary>A versão atual já vale hoje? (Antes disso, o aceite da anterior ainda libera o uso.)</summary>
    public static bool VersaoAtualVigente(DateOnly hoje) => hoje >= VigentesDesde;

    /// <summary>O aceite da versão anterior ainda libera o uso hoje?</summary>
    public static bool AceiteAnteriorAindaVale(DateOnly hoje) => !VersaoAtualVigente(hoje);

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

    public const string NomeServico = "micoempresa";

    public const string Site = "micoempresa.com.br";

    public const string Foro = "Salvador/BA";

    public static bool PossuiMarcadorPendente => ResponsavelNome.Contains('[');
}
