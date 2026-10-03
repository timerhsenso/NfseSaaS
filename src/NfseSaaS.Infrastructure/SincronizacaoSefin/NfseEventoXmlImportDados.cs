using System.Xml;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Infrastructure.SincronizacaoSefin;

/// <summary>
/// Extrai, do XML de um EVENTO distribuído pelo ADN, o que interessa pra
/// sincronização: de qual NFS-e ele é (chNFSe), qual o tipo (código do
/// grupo e{tipo}, ex.: e101101) e o motivo informado.
///
/// Por que isto existe: o XML da NFS-e é imutável (assinado na geração)
/// — uma nota cancelada depois continua com o mesmo XML de "gerada". O
/// cancelamento chega pelo ADN como um documento SEPARADO, de tipo
/// evento, com NSU próprio. Sem processar esses documentos, toda nota
/// cancelada no portal é importada como Autorizada.
///
/// Leitura por local-name() de propósito: localiza o grupo do evento
/// pelo nome (e + 6 dígitos) em qualquer ponto do documento, sem
/// depender do aninhamento exato (evento/infEvento/pedRegEvento/
/// infPedReg) nem do prefixo de namespace.
/// </summary>
internal sealed class NfseEventoXmlImportDados
{
    private readonly XmlDocument _doc;
    private readonly XmlElement? _grupoEvento;

    public NfseEventoXmlImportDados(string xmlEvento)
    {
        _doc = new XmlDocument();
        _doc.LoadXml(xmlEvento);
        _grupoEvento = LocalizarGrupoEvento(_doc);
    }

    /// <summary>Código do tipo de evento sem o "e" (ex.: "101101"), ou null se o XML não tiver grupo de evento.</summary>
    public string? CodigoTipoEvento() => _grupoEvento?.LocalName[1..];

    public string? ChaveAcesso() =>
        _doc.SelectSingleNode("//*[local-name()='chNFSe']")?.InnerText.Trim() is { Length: > 0 } chave ? chave : null;

    /// <summary>Motivo informado no evento (xMotivo), caindo pra descrição do tipo (xDesc) quando não houver.</summary>
    public string? Motivo()
    {
        if (_grupoEvento is null)
            return null;

        var motivo = _grupoEvento.SelectSingleNode("*[local-name()='xMotivo']")?.InnerText.Trim();
        if (!string.IsNullOrEmpty(motivo))
            return motivo;

        var descricao = _grupoEvento.SelectSingleNode("*[local-name()='xDesc']")?.InnerText.Trim();
        return string.IsNullOrEmpty(descricao) ? null : descricao;
    }

    /// <summary>
    /// Situação que a NFS-e passa a ter depois do evento, ou null quando o
    /// evento não muda a situação da nota (manifestação, análise fiscal
    /// etc. — só registrados no log, não aplicados).
    /// </summary>
    public static NfseStatus? StatusResultante(string? codigoTipoEvento) => codigoTipoEvento switch
    {
        "101101" => NfseStatus.Cancelada,    // Cancelamento de NFS-e (pelo emitente)
        "305101" => NfseStatus.Cancelada,    // Cancelamento de NFS-e por ofício
        "105102" => NfseStatus.Substituida,  // Cancelamento de NFS-e por substituição
        _ => null
    };

    private static XmlElement? LocalizarGrupoEvento(XmlDocument doc)
    {
        foreach (XmlNode no in doc.GetElementsByTagName("*"))
        {
            if (no is XmlElement elemento && EhNomeDeGrupoEvento(elemento.LocalName))
                return elemento;
        }

        return null;
    }

    private static bool EhNomeDeGrupoEvento(string nome) =>
        nome.Length == 7 && nome[0] == 'e' && nome.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;
}
