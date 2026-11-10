using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Clientes;

/// <summary>
/// Regras puras do cliente compartilhado entre as empresas da conta (C10).
///
/// "Disponível para": só esta empresa (Cliente.EmpresaId = a empresa) ou
/// todas as empresas da conta (Cliente.EmpresaId = nulo). Na lista de uma
/// empresa aparecem os exclusivos dela e os compartilhados; se a empresa
/// tiver um exclusivo com o mesmo CPF/CNPJ de um compartilhado, só o
/// exclusivo aparece ali (o exclusivo vence).
/// </summary>
public static class RegrasClienteCompartilhado
{
    public const string TextoTodas = "Todas as empresas";
    public const string TextoSoEsta = "Só esta empresa";

    /// <summary>Com uma empresa só, a pergunta não faz sentido: o campo fica oculto.</summary>
    public static bool MostrarCampo(int totalEmpresas) => totalEmpresas > 1;

    /// <summary>
    /// Padrão do campo para um cliente novo. Perfil Empresa (filiais da
    /// mesma dona) → todas; Contador (cada empresa é um cliente diferente
    /// do escritório) → só esta; conta com uma empresa → só esta.
    /// </summary>
    public static bool PadraoCompartilhado(PerfilConta perfil, int totalEmpresas) =>
        MostrarCampo(totalEmpresas) && perfil == PerfilConta.Empresa;

    /// <summary>O que gravar no cadastro: o que veio da tela ou, sem escolha, o padrão.</summary>
    public static bool Resolver(bool? pedido, PerfilConta perfil, int totalEmpresas) =>
        pedido ?? PadraoCompartilhado(perfil, totalEmpresas);

    public static string Texto(bool compartilhado) => compartilhado ? TextoTodas : TextoSoEsta;

    public static string MensagemDocumentoCompartilhadoRepetido(string cpfCnpj) =>
        $"Já existe um cliente com o CPF/CNPJ '{cpfCnpj}' disponível para todas as empresas da conta.";

    public static string MensagemExclusivoNestaEmpresa(string cpfCnpj) =>
        $"Esta empresa já tem o cliente '{cpfCnpj}' só para ela. Edite esse cadastro e mude \"Disponível para\" para \"{TextoTodas}\".";

    public const string MensagemEmpresaParaExclusivo =
        "Informe a empresa que vai ficar com o cliente (\"Só esta empresa\").";

    public static string MensagemUsadoEmOutrasEmpresas(int notas, int contratos) =>
        $"Este cliente já tem {notas} nota(s) e {contratos} contrato(s) em outras empresas da conta: " +
        $"ele precisa continuar disponível para todas.";
}
