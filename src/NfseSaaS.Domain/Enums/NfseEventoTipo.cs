namespace NfseSaaS.Domain.Enums;

/// <summary>
/// Tipo de evento registrado no histórico de uma Nfse (ver
/// <see cref="Entities.NfseEvento"/>). Cada transição real de
/// <see cref="NfseStatus"/> ao longo do fluxo de emissão/cancelamento
/// gera um evento correspondente — a trilha completa fica em NfseEvento,
/// não só o estado final na própria Nfse.
/// </summary>
public enum NfseEventoTipo
{
    /// <summary>DPS montada, assinada e enviada à SEFIN Nacional (Nfse gravada como Processando).</summary>
    DpsEnviada = 0,

    /// <summary>NFS-e autorizada pela SEFIN Nacional.</summary>
    Autorizada = 1,

    /// <summary>DPS rejeitada pela SEFIN Nacional (ver Codigo/Mensagem do evento).</summary>
    Rejeitada = 2,

    /// <summary>Falha de comunicação, certificado ou infraestrutura ao tentar emitir — não é uma rejeição fiscal da SEFIN.</summary>
    FalhaComunicacao = 3,

    /// <summary>Evento de cancelamento (e101101) aceito pela SEFIN Nacional.</summary>
    Cancelada = 4,

    /// <summary>Tentativa de cancelamento rejeitada pela SEFIN Nacional — a Nfse permanece Autorizada.</summary>
    CancelamentoRejeitado = 5,

    /// <summary>
    /// Nfse importada via Distribuição de DF-e (ADN) — emitida por OUTRO
    /// canal (portal web Emissor Nacional, outro sistema) e trazida pra
    /// este SaaS depois. Distingue de DpsEnviada/Autorizada, que só se
    /// aplicam a notas emitidas através deste próprio sistema.
    /// </summary>
    ImportadaDaSefin = 6
}
