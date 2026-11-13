using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.Fiscal.Retencoes;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// C6: o cálculo que a emissão vai fazer, para a tela mostrar o líquido e
/// os avisos antes de mandar. Mesmas regras (RegrasRetencoesFederais) e a
/// mesma origem dos padrões (cliente, alíquota de ISS da empresa).
/// </summary>
public sealed class SimularRetencoesUseCase : ISimularRetencoesUseCase
{
    private readonly AppDbContext _db;

    public SimularRetencoesUseCase(AppDbContext db) => _db = db;

    public async Task<SimulacaoRetencoesResponse> ExecutarAsync(SimularRetencoesRequest request, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas.AsNoTracking()
            .Where(e => e.Id == request.EmpresaId)
            .Select(e => new { e.OpSimpNac, e.AliquotaIss, e.AvisoDispensaRetencaoNaDescricao })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Empresa {request.EmpresaId} não encontrada.");

        var percentuais = request.RetencoesFederais;
        if (percentuais is null)
        {
            var doCliente = request.ClienteId is { } clienteId
                ? await ClientesDaEmpresa.Utilizaveis(_db, request.EmpresaId).AsNoTracking()
                    .Where(c => c.Id == clienteId)
                    .Select(c => new PercentuaisRetencao(c.RetencaoIrrfPercentual, c.RetencaoPisPercentual,
                        c.RetencaoCofinsPercentual, c.RetencaoCsllPercentual, c.RetencaoInssPercentual))
                    .FirstOrDefaultAsync(cancellationToken)
                : null;
            percentuais = RegrasRetencoesFederais.PadraoParaRegime(doCliente, empresa.OpSimpNac);
        }

        var disponivel = empresa.OpSimpNac != RegrasRetencoesFederais.OpSimpNacMei;
        if (request.ValorServico <= 0)
            return new SimulacaoRetencoesResponse(null, percentuais, empresa.OpSimpNac, disponivel, null, null);

        try
        {
            var resultado = RegrasRetencoesFederais.CalcularValidado(new EntradaRetencoes(
                request.ValorServico,
                request.DescontoIncondicionado ?? 0m,
                request.DescontoCondicionado ?? 0m,
                percentuais,
                empresa.OpSimpNac,
                request.IssRetido,
                request.AliquotaIss ?? empresa.AliquotaIss,
                request.ReterAbaixoDoMinimo));

            var frase = empresa.AvisoDispensaRetencaoNaDescricao
                && RegrasRetencoesFederais.OptanteSimples(empresa.OpSimpNac)
                && !resultado.TemRetencaoFederal
                    ? RegrasRetencoesFederais.FraseDispensaSimples
                    : null;

            return new SimulacaoRetencoesResponse(resultado, percentuais, empresa.OpSimpNac, disponivel, frase, null);
        }
        catch (RegraNegocioException ex)
        {
            return new SimulacaoRetencoesResponse(null, percentuais, empresa.OpSimpNac, disponivel, null, ex.Message);
        }
    }
}
