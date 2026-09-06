namespace NfseSaaS.Domain.Enums;

/// <summary>Ciclo de vida de uma NFS-e dentro do SaaS.</summary>
public enum NfseStatus
{
    /// <summary>DPS ainda não enviada à SEFIN Nacional.</summary>
    Rascunho = 0,

    /// <summary>DPS enviada, aguardando/processando resposta da SEFIN.</summary>
    Processando = 1,

    /// <summary>NFS-e autorizada pela SEFIN Nacional.</summary>
    Autorizada = 2,

    /// <summary>DPS rejeitada pela SEFIN Nacional (ver CodigoErro/MensagemErro).</summary>
    Rejeitada = 3,

    /// <summary>NFS-e cancelada após autorização.</summary>
    Cancelada = 4,

    /// <summary>NFS-e substituída por uma nova emissão.</summary>
    Substituida = 5
}
