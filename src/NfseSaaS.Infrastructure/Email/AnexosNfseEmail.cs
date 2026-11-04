using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Email;

/// <summary>
/// Monta os anexos do e-mail da nota ao tomador — PDF (DANFSe) e XML — a
/// partir da própria Nfse, na hora do envio (ver EmailLog.NfseId). Precisa
/// do Tenant da nota já definido no escopo (o EmailDispatchHostedService
/// faz isso), porque a leitura passa pelo isolamento normal.
///
/// O XML é obrigatório (é o documento fiscal); sem ele o envio falha e fica
/// registrado. O PDF é "melhor esforço": se a geração falhar, o e-mail sai
/// só com o XML e o motivo vai pro log — o tomador ainda recebe a nota.
/// </summary>
public sealed class AnexosNfseEmail
{
    private readonly AppDbContext _db;
    private readonly IObterDanfsePdfUseCase _obterDanfsePdf;
    private readonly ILogger<AnexosNfseEmail> _logger;

    public AnexosNfseEmail(AppDbContext db, IObterDanfsePdfUseCase obterDanfsePdf, ILogger<AnexosNfseEmail> logger)
    {
        _db = db;
        _obterDanfsePdf = obterDanfsePdf;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<AnexoEmail>> MontarAsync(Guid nfseId, CancellationToken cancellationToken)
    {
        var nfse = await _db.NotasFiscais.AsNoTracking()
            .Where(n => n.Id == nfseId)
            .Select(n => new { n.XmlNfse, n.NumeroDps })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Nota {nfseId} não encontrada para anexar ao e-mail.");

        if (string.IsNullOrWhiteSpace(nfse.XmlNfse))
            throw new InvalidOperationException("A nota não tem o XML autorizado guardado — não há o que anexar.");

        var anexos = new List<AnexoEmail>();
        var nomeBase = $"NFSe_{nfse.NumeroDps:00000}";

        try
        {
            var pdf = await _obterDanfsePdf.ExecutarAsync(nfseId, cancellationToken);
            anexos.Add(new AnexoEmail(pdf.NomeArquivo, pdf.ContentType, pdf.Bytes));
            nomeBase = Path.GetFileNameWithoutExtension(pdf.NomeArquivo);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "PDF da nota {NfseId} não pôde ser gerado; o e-mail vai só com o XML.", nfseId);
        }

        anexos.Add(new AnexoEmail($"{nomeBase}.xml", "application/xml", Encoding.UTF8.GetBytes(nfse.XmlNfse)));

        return anexos;
    }
}
