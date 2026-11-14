using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Contratos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class CadastrarContratoUseCase : ICadastrarContratoUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public CadastrarContratoUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<Guid> ExecutarAsync(CadastrarContratoRequest request, CancellationToken cancellationToken)
    {
        var municipioEmpresa = await _db.Empresas.Where(e => e.Id == request.EmpresaId).Select(e => e.CodigoMunicipio).FirstOrDefaultAsync(cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Empresa {request.EmpresaId} não encontrada.");

        // C2: local da prestação padrão das notas (igual ao da Empresa = não guarda).
        var localPrestacao = RegrasLocalPrestacao.ParaGuardar(request.CodigoMunicipioPrestacao, municipioEmpresa);
        await ConferenciaMunicipio.GarantirExisteAsync(_db, localPrestacao, cancellationToken);

        // Cliente e Serviços da MESMA Empresa do Contrato (ver EmitirNfseUseCase).
        // C10: cliente exclusivo da Empresa ou compartilhado pela conta.
        var clienteExiste = await ClientesDaEmpresa.Utilizaveis(_db, request.EmpresaId).AnyAsync(c => c.Id == request.ClienteId, cancellationToken);
        if (!clienteExiste)
            throw new RecursoNaoEncontradoException($"Cliente {request.ClienteId} não encontrado.");

        var servicoIds = request.Servicos.Select(s => s.ServicoId).Distinct().ToList();
        var totalServicosEncontrados = await _db.Servicos.CountAsync(s => servicoIds.Contains(s.Id) && s.EmpresaId == request.EmpresaId, cancellationToken);
        if (totalServicosEncontrados != servicoIds.Count)
            throw new RecursoNaoEncontradoException("Um ou mais Serviços informados não foram encontrados.");

        var valorAtual = request.Servicos.Sum(s => s.Quantidade * s.ValorUnitario);

        // Número: o digitado (texto livre) ou o próximo "ano/NNNN" da empresa.
        var numero = NumeroContratoRegras.Normalizar(request.Numero)
            ?? await NumeracaoContrato.ProximoAsync(_db, request.EmpresaId, DataBrasilia.Hoje().Year, cancellationToken);
        await NumeracaoContrato.GarantirUnicoAsync(_db, request.EmpresaId, numero, null, cancellationToken);

        var contrato = new Contrato
        {
            EmpresaId = request.EmpresaId,
            ClienteId = request.ClienteId,
            Numero = numero,
            Descricao = request.Descricao?.Trim() ?? string.Empty,
            ValorAtual = valorAtual,
            DataInicioContrato = request.DataInicioContrato,
            PeriodicidadeReajusteMeses = request.PeriodicidadeReajusteMeses,
            IndiceReajuste = request.IndiceReajuste,
            DiasAlertaOverride = request.DiasAlertaOverride,
            Observacao = request.Observacao,
            DataFim = request.DataFim,
            Status = request.Status,
            TipoCobranca = request.TipoCobranca,
            PermitirAlterarValorNaEmissao = request.PermitirAlterarValorNaEmissao,
            CodigoMunicipioPrestacao = localPrestacao,
            ReajusteAutomatico = request.ReajusteAutomatico,
            AplicarIndiceNegativo = request.AplicarIndiceNegativo,
            DataUltimoReajuste = request.DataUltimoReajuste
        };

        _db.Contratos.Add(contrato);

        foreach (var servico in request.Servicos)
        {
            _db.ContratoServicos.Add(new ContratoServico
            {
                ContratoId = contrato.Id,
                ServicoId = servico.ServicoId,
                Quantidade = servico.Quantidade,
                ValorUnitario = servico.ValorUnitario,
                ValorVariavel = servico.ValorVariavel
            });
        }

        // Primeiro evento do Histórico de valores: com que valor e composição
        // o Contrato nasceu. Vigência = início do Contrato.
        _db.MovimentacoesValorContrato.Add(new MovimentacaoValorContrato
        {
            ContratoId = contrato.Id,
            Tipo = TipoMovimentacaoValor.ValorInicial,
            Status = StatusMovimentacaoValor.Aplicado,
            DataVigencia = request.DataInicioContrato,
            ValorAnterior = null,
            ValorNovo = valorAtual,
            ComposicaoJson = await ComposicaoContrato.SerializarAsync(
                _db, request.Servicos.Select(s => (s.ServicoId, s.Quantidade, s.ValorUnitario)), cancellationToken)
        });

        _auditLogWriter.Registrar("CadastrarContrato", "Contrato", contrato.Id, new { contrato.EmpresaId, contrato.ClienteId, contrato.Numero, contrato.Descricao, contrato.ValorAtual, TotalLinhas = request.Servicos.Count });

        await _db.SaveChangesAsync(cancellationToken);

        return contrato.Id;
    }
}
