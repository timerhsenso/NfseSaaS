namespace NfseSaaS.Domain.Legal;

/// <summary>
/// Versão vigente dos Termos de uso e da Política de privacidade, e os dados
/// do responsável que aparecem nos dois textos (Views/Legal). É o ÚNICO lugar
/// a mudar quando os textos forem revisados.
///
/// Regra de versão: mudou alguma coisa que altera direito ou obrigação de
/// quem usa (preço, responsabilidade, dado tratado, compartilhamento,
/// retenção), suba a versão. Todo usuário logado passa a ver a tela de
/// aceite (ExigeAceiteTermosFilter) até aceitar a nova versão, e cada aceite
/// fica registrado em AceiteTermos (versão, data, IP, navegador). Ajuste de
/// texto que não muda nada (erro de digitação, formatação) não precisa de
/// versão nova.
/// </summary>
public static class DocumentosLegais
{
    public const string VersaoTermosDeUso = "1.0";

    public const string VersaoPoliticaPrivacidade = "1.0";

    /// <summary>Data em que a versão atual passou a valer. Atualize junto com a versão.</summary>
    public static readonly DateOnly VigentesDesde = new(2026, 11, 4);

    /// <summary>
    /// Nome completo do responsável pela plataforma (controlador dos dados de
    /// conta, na LGPD). Enquanto estiver com colchetes, a subida da aplicação
    /// registra um aviso no log (ver Program.cs).
    /// </summary>
    public const string ResponsavelNome = "Carlos Eduardo Caetano Santos";

    /// <summary>Canal de privacidade/LGPD (pedidos de titulares, incidentes).</summary>
    public const string EmailPrivacidade = "lgpd@micoempresa.com.br";

    public const string NomeServico = "micoempresa";

    public const string Site = "micoempresa.com.br";

    public const string Foro = "Salvador/BA";

    /// <summary>True enquanto algum dado do responsável ainda for um marcador a preencher.</summary>
    public static bool PossuiMarcadorPendente => ResponsavelNome.Contains('[');
}
