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
        // Idempotência: se o chamador informou uma chave e já existe uma
        // Nfse com ela para esta Empresa, é um retry da MESMA tentativa —
        // devolve o resultado já existente em vez de emitir de novo (e,
        // consequentemente, sem gastar um NumeroDps novo à toa). Não gera
        // novo evento de auditoria: nada de fato aconteceu nesta chamada.
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existente = await _db.NotasFiscais.FirstOrDefaultAsync(
                n => n.EmpresaId == request.EmpresaId && n.IdempotencyKey == request.IdempotencyKey,
                cancellationToken);

            if (existente is not null)
            {
                return new EmitirNfseResult(
                    existente.Id,
                    existente.Status == NfseStatus.Autorizada,
                    existente.NumeroNfse,
                    existente.ChaveAcesso,
                    existente.CodigoErro,
                    existente.MensagemErro);
            }
        }

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
        //
        // Os campos fiscais abaixo (CodigoTributacaoNacional em diante)
        // são um SNAPSHOT do que será de fato enviado na DPS — copiados
        // agora e nunca mais lidos de Servico/Empresa depois, para que uma
        // edição futura no cadastro não altere retroativamente o que já
        // foi declarado nesta nota.
        var nfse = new Domain.Entities.Nfse
        {
            EmpresaId = empresa.Id,
            ClienteId = cliente.Id,
            NumeroDps = numeroDps,
            SerieDps = SerieDps,
            DataCompetencia = request.DataCompetencia,
            ValorServico = request.ValorServico,
            DescricaoServico = request.DescricaoServico,
            CodigoTributacaoNacional = servico.CodigoTributacaoNacional,
            CodigoNbs = servico.CodigoNbs,
            TribIssqn = empresa.TribIssqn,
            TpRetIssqn = empresa.TpRetIssqn,
            CstPisCofins = empresa.CstPisCofins,
            TpRetPisCofins = empresa.TpRetPisCofins,
            PercentualTotalTributosSimplesNacional = empresa.PercentualTotalTributosSimplesNacional,
            IdempotencyKey = request.IdempotencyKey,
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
            // Os valores da DPS de fato enviada vêm do snapshot recém-gravado
            // na Nfse (nfse.CodigoTributacaoNacional etc.), não mais lidos
            // de novo de servico/empresa — mesma fonte que fica no banco.
            Tributacao: new TributacaoDps(
                TribIssqn: nfse.TribIssqn,
                TpRetIssqn: nfse.TpRetIssqn,
                CstPisCofins: nfse.CstPisCofins,
                TpRetPisCofins: nfse.TpRetPisCofins,
                PercentualTotalTributosSimplesNacional: nfse.PercentualTotalTributosSimplesNacional),
            NumeroDps: numeroDps,
            SerieDps: SerieDps,
            DataCompetencia: request.DataCompetencia,
            Valor: request.ValorServico,
            CodigoTributacaoNacional: nfse.CodigoTributacaoNacional,
            CodigoNbs: nfse.CodigoNbs,
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
    /// Próximo número de DPS para a Empresa/Série informada, obtido de
    /// forma atômica no Postgres: UPSERT com ON CONFLICT ... DO UPDATE ...
    /// RETURNING. Substitui o antigo cálculo em C# (MAX(NumeroDps) + 1),
    /// que tinha uma corrida real — duas emissões simultâneas para a
    /// mesma Empresa podiam ler o mesmo "último número" antes de qualquer
    /// uma delas gravar, gerando DPS com NumeroDps duplicado.
    ///
    /// Esta operação é uma instrução SQL isolada, fora da transação do
    /// SaveChangesAsync que grava a Nfse logo em seguida — de propósito:
    /// se o restante da emissão falhar depois, o número já incrementado
    /// NÃO é revertido. Isso é o comportamento correto para numeração
    /// fiscal sequencial: pode haver buracos na sequência, mas NUNCA pode
    /// haver dois NumeroDps iguais para a mesma Empresa/Série.
    /// </summary>
    private async Task<int> ProximoNumeroDpsAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        return await _db.Database.SqlQuery<int>(
            $"""
            INSERT INTO contadores_dps ("EmpresaId", "SerieDps", "UltimoNumero")
            VALUES ({empresaId}, {SerieDps}, 1)
            ON CONFLICT ("EmpresaId", "SerieDps")
            DO UPDATE SET "UltimoNumero" = contadores_dps."UltimoNumero" + 1
            RETURNING "UltimoNumero"
            """).SingleAsync(cancellationToken);
    }
}
