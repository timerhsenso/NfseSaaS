namespace NfseSaaS.Domain.Enums;

/// <summary>
/// DOC2: o que o ENVIO AUTOMÁTICO do e-mail da nota faz quando um documento
/// que o cliente exige está vencido ou ainda não foi enviado (configurado
/// no cadastro do cliente). No envio pela tela quem decide é o usuário. A
/// nota em si nunca é bloqueada; o alerta fica registrado no e-mail.
/// </summary>
public enum AcaoDocumentoPendente
{
    /// <summary>Padrão: o e-mail sai com a nota e os documentos em dia, sem o vencido.</summary>
    MandarSemOPendente = 0,

    /// <summary>O documento vencido vai assim mesmo (o que falta não tem como ir).</summary>
    MandarMesmoVencido = 1,

    /// <summary>Segura o e-mail: não manda (o usuário envia pela tela depois de atualizar o documento).</summary>
    NaoMandarEmail = 2
}
