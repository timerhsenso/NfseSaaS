using System.Text;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Entrega o XML da NFS-e guardado na autorização (o mesmo que vai anexado no
/// e-mail ao cliente), sem consultar a SEFIN. Nome do arquivo igual ao do
/// PDF (ObterDanfsePdfUseCase.MontarNomeArquivo), com extensão .xml. O
/// filtro de conta do DbContext garante que só a própria conta baixa.
/// </summary>
public sealed class ObterXmlNfseUseCase : IObterXmlNfseUseCase
{
    private static readonly TimeSpan OffsetBrasilia = TimeSpan.FromHours(-3);

    private readonly AppDbContext _db;

    public ObterXmlNfseUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<XmlNfseResponse> ExecutarAsync(Guid nfseId, CancellationToken cancellationToken)
    {
        var nfse = await _db.NotasFiscais.AsNoTracking()
            .Where(n => n.Id == nfseId)
            .Select(n => new { n.Status, n.XmlNfse, n.NumeroDps, n.DataEmissao, n.DataCompetencia, n.ClienteId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Nfse {nfseId} não encontrada.");

        if (nfse.Status is not (NfseStatus.Autorizada or NfseStatus.Cancelada))
            throw new RegraNegocioException($"Só existe XML de nota autorizada (ou cancelada depois de autorizada). Situação atual: {nfse.Status}.");

        if (string.IsNullOrWhiteSpace(nfse.XmlNfse))
            throw new RegraNegocioException("O XML desta nota não foi guardado na autorização. Baixe-o pelo portal da NFS-e (gov.br/nfse) com a chave de acesso.");

        var clienteApelido = await _db.Clientes.AsNoTracking()
            .Where(c => c.Id == nfse.ClienteId)
            .Select(c => c.Apelido)
            .FirstOrDefaultAsync(cancellationToken) ?? "Cliente";

        var data = nfse.DataEmissao is { } emissao
            ? DateOnly.FromDateTime(emissao.ToOffset(OffsetBrasilia).DateTime)
            : nfse.DataCompetencia;

        var nome = Path.ChangeExtension(ObterDanfsePdfUseCase.MontarNomeArquivo(clienteApelido, nfse.NumeroDps, data), ".xml");
        return new XmlNfseResponse(Encoding.UTF8.GetBytes(nfse.XmlNfse), nome);
    }
}
