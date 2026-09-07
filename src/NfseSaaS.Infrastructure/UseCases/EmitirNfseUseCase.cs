using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Nacional.Helpers;
using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Services;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Implementação real do caso de uso de emissão de NFS-e: busca
/// Empresa/Cliente/Servico (já isolados por tenant via AppDbContext),
/// monta o DpsRequest a partir de dados reais persistidos (nada mais
/// hardcoded, ao contrário da ferramenta NfseSaaS.EmitirTeste), chama
/// INfseNacionalService.EmitirAsync e persiste o resultado.
/// </summary>
public sealed class EmitirNfseUseCase : IEmitirNfseUseCase
{
    // Faixa 00001-49999 = emissão com aplicativo próprio (tpEmit=1) — ver
    // comentário completo em NfseSaaS.Nacional.Builders.DpsBuilder.
    private const string SerieDps = "00001";

    private readonly AppDbContext _db;
    private readonly INfseNacionalService _nfseNacionalService;
    private readonly IAuditLogWriter _auditLogWriter;

    public EmitirNfseUseCase(AppDbContext db, INfseNacionalService nfseNacionalService, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _nfseNacionalService = nfseNacionalService;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<EmitirNfseResult> ExecutarAsync(EmitirNfseRequest request, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas.FirstOrDefaultAsync(e => e.Id == request.EmpresaId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Empresa {request.EmpresaId} não encontrada.");

        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Id == request.ClienteId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Cliente {request.ClienteId} não encontrado.");

        var servico = await _db.Servicos.FirstOrDefaultAsync(s => s.Id == request.ServicoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Serviço {request.ServicoId} não encontrado.");

        var numeroDps = await ProximoNumeroDpsAsync(request.EmpresaId, cancellationToken);

        // Grava como "Processando" ANTES de chamar a SEFIN — se a chamada
        // falhar (rede, timeout), já existe um registro rastreável em vez
        // de perder silenciosamente a tentativa. (Auditoria fica só no
        // desfecho final, não neste insert intermediário — ver abaixo.)
        var nfse = new Domain.Entities.Nfse
        {
            EmpresaId = empresa.Id,
            ClienteId = cliente.Id,
            NumeroDps = numeroDps,
            SerieDps = SerieDps,
            DataCompetencia = request.DataCompetencia,
            ValorServico = request.ValorServico,
            DescricaoServico = request.DescricaoServico,
            Status = NfseStatus.Processando
        };

        _db.NotasFiscais.Add(nfse);
        await _db.SaveChangesAsync(cancellationToken);

        var dpsRequest = new DpsRequest(
            Prestador: new PrestadorDps(
                Cnpj: empresa.Cnpj,
                InscricaoMunicipal: empresa.InscricaoMunicipal,
                Telefone: empresa.Telefone,
                Email: empresa.Email,
                CodigoMunicipio: empresa.CodigoMunicipio,
                OpSimpNac: empresa.OpSimpNac,
                RegApTribSN: empresa.RegApTribSN,
                RegEspTrib: empresa.RegEspTrib),
            Tomador: new TomadorDps(
                CnpjOuCpf: cliente.CpfCnpj,
                Nome: cliente.Nome,
                CodigoMunicipio: cliente.CodigoMunicipio,
                Cep: cliente.Cep,
                Logradouro: cliente.Logradouro,
                Numero: cliente.Numero,
                Bairro: cliente.Bairro),
            Tributacao: new TributacaoDps(
                TribIssqn: empresa.TribIssqn,
                TpRetIssqn: empresa.TpRetIssqn,
                CstPisCofins: empresa.CstPisCofins,
                TpRetPisCofins: empresa.TpRetPisCofins,
                PercentualTotalTributosSimplesNacional: empresa.PercentualTotalTributosSimplesNacional),
            NumeroDps: numeroDps,
            SerieDps: SerieDps,
            DataCompetencia: request.DataCompetencia,
            Valor: request.ValorServico,
            CodigoTributacaoNacional: servico.CodigoTributacaoNacional,
            CodigoNbs: servico.CodigoNbs,
            DescricaoServico: request.DescricaoServico);

        try
        {
            var resposta = await _nfseNacionalService.EmitirAsync(dpsRequest, empresa.Id, cancellationToken);

            // Npgsql só aceita DateTimeOffset com Offset=0 (UTC) em colunas
            // "timestamp with time zone" — a SEFIN retorna a data com o
            // offset de Brasília (-03:00), então convertemos antes de salvar.
            nfse.DataEmissao = (resposta.DataHoraProcessamento ?? DateTimeOffset.UtcNow).ToUniversalTime();
            nfse.ChaveAcesso = resposta.ChaveAcesso;
            nfse.NumeroNfse = resposta.IdDps;

            if (resposta.Sucesso)
            {
                nfse.Status = NfseStatus.Autorizada;

                if (!string.IsNullOrWhiteSpace(resposta.NfseXmlGZipB64))
                    nfse.XmlNfse = GZipHelper.DescomprimirDeBase64(resposta.NfseXmlGZipB64);
            }
            else
            {
                var primeiroErro = resposta.Erros.FirstOrDefault();
                nfse.Status = NfseStatus.Rejeitada;
                nfse.CodigoErro = primeiroErro?.Codigo;
                nfse.MensagemErro = primeiroErro?.Descricao;
            }

            _auditLogWriter.Registrar("EmitirNfse", "Nfse", nfse.Id, new { nfse.Status, nfse.ChaveAcesso, nfse.ValorServico });

            await _db.SaveChangesAsync(cancellationToken);

            return new EmitirNfseResult(nfse.Id, resposta.Sucesso, nfse.NumeroNfse, nfse.ChaveAcesso, nfse.CodigoErro, nfse.MensagemErro);
        }
        catch (Exception ex)
        {
            // Falha de comunicação/certificado etc. — registra o erro na
            // própria Nfse em vez de deixá-la "Processando" para sempre.
            nfse.Status = NfseStatus.Rejeitada;
            nfse.MensagemErro = ex.Message;

            _auditLogWriter.Registrar("EmitirNfse", "Nfse", nfse.Id, new { nfse.Status, Erro = ex.Message });

            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Próximo número de DPS para a Empresa/Série informada. Simples e
    /// suficiente para este estágio — não há ainda concorrência real de
    /// múltiplas emissões simultâneas para a mesma Empresa; se isso vier a
    /// acontecer, precisará de um lock/sequência transacional.
    /// </summary>
    private async Task<int> ProximoNumeroDpsAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var ultimo = await _db.NotasFiscais
            .Where(n => n.EmpresaId == empresaId && n.SerieDps == SerieDps)
            .OrderByDescending(n => n.NumeroDps)
            .Select(n => (int?)n.NumeroDps)
            .FirstOrDefaultAsync(cancellationToken);

        return (ultimo ?? 0) + 1;
    }
}
