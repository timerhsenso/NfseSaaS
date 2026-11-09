namespace NfseSaaS.Domain.Enums;

/// <summary>Situação de uma exportação dos dados da conta (D1).</summary>
public enum StatusExportacaoDados
{
    /// <summary>Pedida, esperando o job começar.</summary>
    Pendente = 1,

    /// <summary>O job está montando o arquivo.</summary>
    Gerando = 2,

    /// <summary>Arquivo pronto para baixar até ExpiraEm.</summary>
    Pronta = 3,

    /// <summary>Falhou; a conta pode pedir de novo.</summary>
    Erro = 4,

    /// <summary>Passou do prazo (ou o arquivo sumiu): o arquivo foi apagado.</summary>
    Expirada = 5
}
