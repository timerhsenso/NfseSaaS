namespace NfseSaaS.Application.Authorization;

/// <summary>
/// Códigos estáveis das telas do sistema — a "coluna" da matriz de
/// permissão IAEC (ver GrupoTela). Centralizado aqui pelo mesmo motivo
/// de Papeis: nenhum lugar do código decide uma string de tela por conta
/// própria. TelaSeeder usa isto pra popular a tabela Tela no startup;
/// RequerPermissaoAttribute usa isto como o Codigo esperado.
/// </summary>
public static class TelaCatalogo
{
    public const string Empresas = "Empresas";
    public const string Clientes = "Clientes";
    public const string Servicos = "Servicos";
    public const string Contratos = "Contratos";
    public const string Nfse = "Nfse";
    public const string Usuarios = "Usuarios";
    public const string Auditoria = "Auditoria";
    public const string Grupos = "Grupos";

    public static readonly (string Codigo, string Nome, int Ordem)[] Todas =
    {
        (Empresas, "Empresas", 1),
        (Clientes, "Clientes", 2),
        (Servicos, "Serviços", 3),
        (Contratos, "Contratos", 4),
        (Nfse, "Notas Fiscais", 5),
        (Usuarios, "Usuários", 6),
        (Grupos, "Grupos de permissão", 7),
        (Auditoria, "Auditoria", 8)
    };
}
