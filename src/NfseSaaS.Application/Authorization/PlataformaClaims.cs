namespace NfseSaaS.Application.Authorization;

/// <summary>
/// Claims do OPERADOR DA PLATAFORMA (o dono do SaaS), que fica acima de
/// qualquer tenant. Ferramentas que enxergam dados de todos os tenants —
/// log do Serilog, painel do Hangfire, tela Sobre — só abrem com este
/// Claim. Ele NÃO vem da matriz de Grupos/Telas do tenant: é concedido no
/// login a partir de PlataformaOptions.UsuariosOperadoresIds (appsettings
/// ou variável de ambiente), então nenhum Administrador de tenant consegue
/// se dar esse acesso pela tela de Grupos.
/// </summary>
public static class PlataformaClaims
{
    public const string Operador = "plataforma:operador";

    /// <summary>
    /// Usuário com a verificação em duas etapas ligada (SEG1). Serve para o
    /// que não passa pelo ExigeDuploFatorFilter, como o painel do Hangfire.
    /// </summary>
    public const string DuploFatorLigado = "seguranca:duplo_fator";
}
