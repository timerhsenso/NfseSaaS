namespace NfseSaaS.Application.Authorization;

/// <summary>
/// Papéis (roles) de usuário dentro de um Tenant. Usa os IdentityRole
/// padrão do ASP.NET Core Identity (sem entidade própria) — só
/// centraliza os nomes aqui pra Web/Infrastructure nunca decidirem uma
/// string de papel cada um por conta própria.
///
/// Perfis conforme o levantamento original do produto:
/// - Administrador: acesso completo, inclusive convidar usuários e
///   cancelar Nfse.
/// - Emissor: pode emitir Nfse, mas não cancelar nem gerenciar cadastros.
/// - Financeiro: leitura de Nfse/AuditLog para conciliação (hoje sem
///   permissão de escrita distinta de Consulta — evolução futura).
/// - Consulta: só leitura.
/// - Contador: leitura de Nfse/AuditLog para fins fiscais/contábeis
///   (hoje sem permissão de escrita distinta de Consulta — evolução
///   futura).
/// </summary>
public static class Papeis
{
    public const string Administrador = "Administrador";
    public const string Emissor = "Emissor";
    public const string Financeiro = "Financeiro";
    public const string Consulta = "Consulta";
    public const string Contador = "Contador";

    public static readonly string[] Todos =
    {
        Administrador, Emissor, Financeiro, Consulta, Contador
    };

    /// <summary>Papéis autorizados a emitir Nfse — ver NfseController.Emitir.</summary>
    public const string PodeEmitir = $"{Administrador},{Emissor}";
}
