using System.Globalization;
using System.Text.Json;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.DocumentosEmpresa;

/// <summary>Um "lugar" de documento (tipo + nome, no caso do Outro) — o mesmo de DocumentoEmpresa.NomeChave.</summary>
public sealed record ChaveDocumento(TipoDocumentoEmpresa Tipo, string NomeChave);

/// <summary>A versão que vale de um documento da empresa (o que o envio precisa saber).</summary>
public sealed record DocumentoAtual(
    Guid Id,
    TipoDocumentoEmpresa Tipo,
    string NomeChave,
    string Titulo,
    string NomeArquivo,
    long TamanhoBytes,
    DateOnly? ValidaAte);

public enum SituacaoDocumentoExigido
{
    EmDia = 1,
    Vencido = 2,

    /// <summary>O cliente exige, mas a empresa ainda não enviou o documento.</summary>
    Faltando = 3
}

/// <summary>Um documento exigido pelo cliente e como ele está na empresa da nota.</summary>
public sealed record DocumentoExigidoAvaliado(ChaveDocumento Chave, string Titulo, SituacaoDocumentoExigido Situacao, DocumentoAtual? Documento);

/// <summary>Documento anexado ao e-mail (gravado em EmailLog.DocumentosAnexos, em JSON).</summary>
public sealed record AnexoDocumentoEmail(Guid Id, string Titulo, string NomeArquivo, long TamanhoBytes, DateOnly? ValidaAte, bool Vencido);

/// <summary>O que vai no e-mail: os anexos e o alerta a registrar (nulo = nada a avisar).</summary>
public sealed record DecisaoDocumentosEmail(IReadOnlyList<AnexoDocumentoEmail> Anexos, string? Alerta, bool SegurarEmail = false);

/// <summary>
/// DOC2: documentos que o cliente exige junto com a nota. Regras puras:
/// formato da coluna Cliente.DocumentosExigidos, avaliação contra a versão
/// que vale na empresa, e a decisão do que anexar no envio automático
/// (escolha do cliente) e no envio pela tela (escolha do usuário). A nota
/// nunca é bloqueada; documento vencido ou faltando sempre gera alerta.
/// </summary>
public static class RegrasDocumentosExigidos
{
    public const int MaximoDocumentos = 20;
    public const int TamanhoMaximoColuna = 2000;

    /// <summary>Acima disso o e-mail pode ser recusado por alguns provedores: a tela avisa (não barra).</summary>
    public const long LimiteAvisoBytes = 10L * 1024 * 1024;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------- Formato da coluna: "1;4;99:alvará de funcionamento" ----------

    public static IReadOnlyList<ChaveDocumento> Ler(string? valor)
    {
        var lista = new List<ChaveDocumento>();
        if (string.IsNullOrWhiteSpace(valor))
            return lista;

        foreach (var parte in valor.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var doisPontos = parte.IndexOf(':');
            var codigo = doisPontos < 0 ? parte : parte[..doisPontos];
            if (!int.TryParse(codigo, NumberStyles.None, CultureInfo.InvariantCulture, out var numero)
                || !Enum.IsDefined(typeof(TipoDocumentoEmpresa), numero))
                continue; // valor estranho no banco: ignora, não derruba o envio

            var tipo = (TipoDocumentoEmpresa)numero;
            var chave = tipo == TipoDocumentoEmpresa.Outro
                ? RegrasDocumentoEmpresa.NomeChave(tipo, doisPontos < 0 ? null : parte[(doisPontos + 1)..])
                : string.Empty;
            if (tipo == TipoDocumentoEmpresa.Outro && chave.Length == 0)
                continue;

            var item = new ChaveDocumento(tipo, chave);
            if (!lista.Contains(item))
                lista.Add(item);
        }
        return lista;
    }

    /// <summary>Confere e grava (nulo = nenhum documento exigido).</summary>
    public static string? Gravar(IEnumerable<ChaveDocumento>? chaves)
    {
        var lista = new List<ChaveDocumento>();
        foreach (var c in chaves ?? Enumerable.Empty<ChaveDocumento>())
        {
            if (!Enum.IsDefined(c.Tipo))
                throw new RegraNegocioException("Tipo de documento desconhecido.");
            var chave = c.Tipo == TipoDocumentoEmpresa.Outro
                ? RegrasDocumentoEmpresa.NomeChave(c.Tipo, c.NomeChave).Replace(";", ",")
                : string.Empty;
            if (c.Tipo == TipoDocumentoEmpresa.Outro && (chave.Length < RegrasDocumentoEmpresa.NomeMinimo || chave.Length > RegrasDocumentoEmpresa.NomeMaximo))
                throw new RegraNegocioException("Documento \"Outro\" sem nome válido.");
            var item = new ChaveDocumento(c.Tipo, chave);
            if (!lista.Contains(item))
                lista.Add(item);
        }

        if (lista.Count > MaximoDocumentos)
            throw new RegraNegocioException($"Escolha no máximo {MaximoDocumentos} documentos.");
        if (lista.Count == 0)
            return null;

        var texto = string.Join(';', lista.Select(c => c.Tipo == TipoDocumentoEmpresa.Outro
            ? $"{(int)c.Tipo}:{c.NomeChave}"
            : ((int)c.Tipo).ToString(CultureInfo.InvariantCulture)));
        if (texto.Length > TamanhoMaximoColuna)
            throw new RegraNegocioException("Lista de documentos grande demais.");
        return texto;
    }

    // ---------- Avaliação ----------

    public static IReadOnlyList<DocumentoExigidoAvaliado> Avaliar(
        IEnumerable<ChaveDocumento> exigidos, IEnumerable<DocumentoAtual> atuais, DateOnly hoje)
    {
        var porChave = atuais.GroupBy(a => new ChaveDocumento(a.Tipo, a.NomeChave)).ToDictionary(g => g.Key, g => g.First());
        return exigidos.Select(c =>
        {
            if (!porChave.TryGetValue(c, out var doc))
                return new DocumentoExigidoAvaliado(c, TituloDaChave(c), SituacaoDocumentoExigido.Faltando, null);
            var vencido = RegrasDocumentoEmpresa.Situacao(doc.ValidaAte, hoje) == SituacaoDocumentoEmpresa.Vencido;
            return new DocumentoExigidoAvaliado(c, doc.Titulo, vencido ? SituacaoDocumentoExigido.Vencido : SituacaoDocumentoExigido.EmDia, doc);
        }).ToList();
    }

    /// <summary>Título sem documento enviado: o do tipo; no Outro, o nome guardado (minúsculo) com a primeira letra maiúscula.</summary>
    public static string TituloDaChave(ChaveDocumento c) =>
        c.Tipo == TipoDocumentoEmpresa.Outro && c.NomeChave.Length > 0
            ? char.ToUpper(c.NomeChave[0], PtBr) + c.NomeChave[1..]
            : CatalogoDocumentosEmpresa.Obter(c.Tipo).Nome;

    public static string TextoSituacao(DocumentoExigidoAvaliado item) => item.Situacao switch
    {
        SituacaoDocumentoExigido.Faltando => "Não enviado em Documentos da empresa",
        SituacaoDocumentoExigido.Vencido => $"Vencido em {item.Documento!.ValidaAte!.Value.ToString("dd/MM/yyyy", PtBr)}",
        _ => item.Documento!.ValidaAte is { } v ? $"Válido até {v.ToString("dd/MM/yyyy", PtBr)}" : "Sem validade"
    };

    // ---------- Decisão ----------

    /// <summary>Envio automático: vale a escolha do cadastro do cliente.</summary>
    public static DecisaoDocumentosEmail DecidirAutomatico(IReadOnlyList<DocumentoExigidoAvaliado> itens, AcaoDocumentoPendente acao)
    {
        var pendentes = itens.Where(i => i.Situacao != SituacaoDocumentoExigido.EmDia).ToList();
        if (acao == AcaoDocumentoPendente.NaoMandarEmail && pendentes.Count > 0)
            return new DecisaoDocumentosEmail(Array.Empty<AnexoDocumentoEmail>(),
                "E-mail não enviado automaticamente (o cliente está configurado para segurar): " + Pendencias(pendentes) + ".", SegurarEmail: true);

        var mandarVencido = acao == AcaoDocumentoPendente.MandarMesmoVencido;
        var anexos = itens
            .Where(i => i.Situacao == SituacaoDocumentoExigido.EmDia || (mandarVencido && i.Situacao == SituacaoDocumentoExigido.Vencido))
            .Select(Anexo)
            .ToList();
        return new DecisaoDocumentosEmail(anexos, Alerta(itens, anexos, Array.Empty<DocumentoExigidoAvaliado>()));
    }

    /// <summary>
    /// Envio pela tela. <paramref name="selecionados"/>: os ids marcados no
    /// modal (nulo = o padrão: todos os que existem). Documento vencido
    /// marcado só vai com <paramref name="enviarVencidos"/> (o "mandar assim
    /// mesmo?" respondido); sem ele, a pergunta volta como erro de regra. No
    /// lote (selecionados nulo), sem <paramref name="enviarVencidos"/> o
    /// vencido simplesmente não vai e o alerta fica registrado.
    /// </summary>
    public static DecisaoDocumentosEmail DecidirManual(
        IReadOnlyList<DocumentoExigidoAvaliado> itens, IReadOnlyCollection<Guid>? selecionados, bool enviarVencidos)
    {
        var existentes = itens.Where(i => i.Documento is not null).ToList();
        List<DocumentoExigidoAvaliado> marcados;
        if (selecionados is null)
        {
            marcados = existentes.Where(i => i.Situacao == SituacaoDocumentoExigido.EmDia || enviarVencidos).ToList();
        }
        else
        {
            marcados = existentes.Where(i => selecionados.Contains(i.Documento!.Id)).ToList();
            var vencidos = marcados.Where(i => i.Situacao == SituacaoDocumentoExigido.Vencido).ToList();
            if (vencidos.Count > 0 && !enviarVencidos)
                throw new RegraNegocioException(
                    $"{Lista(vencidos.Select(v => $"{v.Titulo} ({TextoSituacao(v).ToLowerInvariant()})"))}: mandar assim mesmo? Confirme ou desmarque o documento.");
        }

        var retirados = selecionados is null
            ? Array.Empty<DocumentoExigidoAvaliado>()
            : existentes.Where(i => i.Situacao == SituacaoDocumentoExigido.EmDia && !marcados.Contains(i)).ToArray();
        var anexos = marcados.Select(Anexo).ToList();
        return new DecisaoDocumentosEmail(anexos, Alerta(itens, anexos, retirados));
    }

    private static AnexoDocumentoEmail Anexo(DocumentoExigidoAvaliado i) =>
        new(i.Documento!.Id, i.Titulo, i.Documento.NomeArquivo, i.Documento.TamanhoBytes, i.Documento.ValidaAte,
            i.Situacao == SituacaoDocumentoExigido.Vencido);

    private static string? Alerta(
        IReadOnlyList<DocumentoExigidoAvaliado> itens, IReadOnlyList<AnexoDocumentoEmail> anexos, IReadOnlyCollection<DocumentoExigidoAvaliado> retirados)
    {
        var enviados = anexos.Select(a => a.Id).ToHashSet();
        var partes = new List<string>();

        var vencidosEnviados = itens.Where(i => i.Situacao == SituacaoDocumentoExigido.Vencido && enviados.Contains(i.Documento!.Id)).ToList();
        if (vencidosEnviados.Count > 0)
            partes.Add("Vencido enviado assim mesmo: " + Lista(vencidosEnviados.Select(Descricao)));

        var vencidosFora = itens.Where(i => i.Situacao == SituacaoDocumentoExigido.Vencido && !enviados.Contains(i.Documento!.Id)).ToList();
        if (vencidosFora.Count > 0)
            partes.Add("Vencido, não foi: " + Lista(vencidosFora.Select(Descricao)));

        var faltando = itens.Where(i => i.Situacao == SituacaoDocumentoExigido.Faltando).ToList();
        if (faltando.Count > 0)
            partes.Add("Faltando (não enviado em Documentos da empresa): " + Lista(faltando.Select(f => f.Titulo)));

        if (retirados.Count > 0)
            partes.Add("Retirado neste envio: " + Lista(retirados.Select(r => r.Titulo)));

        return partes.Count == 0 ? null : string.Join(". ", partes) + ".";
    }

    private static string Pendencias(IEnumerable<DocumentoExigidoAvaliado> pendentes) =>
        Lista(pendentes.Select(p => p.Situacao == SituacaoDocumentoExigido.Faltando ? $"{p.Titulo} (faltando)" : Descricao(p)));

    private static string Descricao(DocumentoExigidoAvaliado i) =>
        $"{i.Titulo} (venceu em {i.Documento!.ValidaAte!.Value.ToString("dd/MM/yyyy", PtBr)})";

    private static string Lista(IEnumerable<string> itens) => string.Join(", ", itens);

    // ---------- JSON do EmailLog ----------

    public static string? Serializar(IReadOnlyList<AnexoDocumentoEmail> anexos) =>
        anexos.Count == 0 ? null : JsonSerializer.Serialize(anexos, Json);

    public static IReadOnlyList<AnexoDocumentoEmail> Desserializar(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<AnexoDocumentoEmail>();
        try
        {
            return JsonSerializer.Deserialize<List<AnexoDocumentoEmail>>(json, Json) ?? new List<AnexoDocumentoEmail>();
        }
        catch (JsonException)
        {
            return Array.Empty<AnexoDocumentoEmail>();
        }
    }
}
