namespace NfseSaaS.Domain.Enums;

/// <summary>Situação da NFS-e de uma mensalidade paga (N1).</summary>
public enum StatusNotaMensalidade
{
    /// <summary>Pagamento registrado, nota ainda não emitida.</summary>
    Pendente = 1,

    /// <summary>Nota autorizada.</summary>
    Emitida = 2,

    /// <summary>A última tentativa falhou (ver UltimoErro); o job tenta de novo.</summary>
    Erro = 3,

    /// <summary>Não será emitida (pagamento desfeito antes da emissão, ou tentativas esgotadas).</summary>
    Ignorada = 4
}
