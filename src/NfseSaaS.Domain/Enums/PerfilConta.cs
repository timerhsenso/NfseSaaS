namespace NfseSaaS.Domain.Enums;

/// <summary>Quem abriu a conta (escolhido na primeira etapa do cadastro).</summary>
public enum PerfilConta
{
    /// <summary>A própria empresa emite as notas dela. A conta nasce com a Empresa cadastrada.</summary>
    Empresa = 1,

    /// <summary>Contador ou escritório que emite pelos clientes (CPF ou CNPJ). As Empresas são as dos clientes.</summary>
    Contador = 2
}
