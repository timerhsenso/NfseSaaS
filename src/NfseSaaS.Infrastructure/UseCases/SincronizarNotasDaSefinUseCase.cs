using System.Text.Json;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.SincronizacaoSefin;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Domain.Snapshots;
using NfseSaaS.Infrastructure.Danfse;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.SincronizacaoSefin;
using NfseSaaS.Nacional.Clients;
using NfseSaaS.Nacional.Helpers;
using NfseSaaS.Nacional.Models;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Importa, via ADN (Distribuição de DF-e), notas emitidas por outro
/// canal — ver ISincronizarNotasDaSefinUseCase. Decisão de arquitetura:
/// Nfse.ClienteId é uma FK obrigatória (não-nula) neste SaaS — uma nota
/// importada cujo tomador não existe ainda como Cliente precisa de um
/// Cliente novo, criado automaticamente a partir dos dados do próprio
/// XML (find-or-create por CpfCnpj, escopado à Empresa). A alternativa
/// (tornar ClienteId anulável pra "notas externas") espalharia essa
/// exceção por toda a UI/relatórios que hoje assumem Cliente sempre
/// presente — o find-or-create é a opção de menor ondulação.
///
/// Situação da nota: o XML da NFS-e é imutável (assinado na geração), então
/// toda nota importada nasce Autorizada. Cancelamento/substituição chegam
/// pelo ADN como documentos de EVENTO separados, com NSU próprio, e são
/// aplicados por AplicarEventoAsync — inclusive sobre notas importadas em
/// sincronizações anteriores ou emitidas por este próprio sistema e
/// canceladas depois no portal.
/// </summary>
public sealed class SincronizarNotasDaSefinUseCase : ISincronizarNotasDaSefinUseCase
{
    private readonly AppDbContext _db;
    private readonly IAdnDistribuicaoClient _adnClient;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INfseEventoWriter _nfseEventoWriter;
    private readonly ILogger<SincronizarNotasDaSefinUseCase> _logger;

    public SincronizarNotasDaSefinUseCase(
        AppDbContext db,
        IAdnDistribuicaoClient adnClient,
        IAuditLogWriter auditLogWriter,
        INfseEventoWriter nfseEventoWriter,
        ILogger<SincronizarNotasDaSefinUseCase> logger)
    {
        _db = db;
        _adnClient = adnClient;
        _auditLogWriter = auditLogWriter;
        _nfseEventoWriter = nfseEventoWriter;
        _logger = logger;
    }

    public async Task<SincronizacaoSefinResponse> ExecutarAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas.FirstOrDefaultAsync(e => e.Id == empresaId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        var ultimoNsu = empresa.UltimoNsuDistribuicao ?? 0;
        var lote = await _adnClient.ConsultarPorNsuAsync(empresaId, empresa.TipoAmbiente.ParaTpAmb(), ultimoNsu, cancellationToken);

        int importadas = 0, jaExistentes = 0, ignoradasPorConflito = 0, clientesCriados = 0, atualizadasPorEvento = 0;
        var maiorNsu = ultimoNsu;

        // Cache em memória dos Clientes criados NESTA execução — sem
        // isso, duas notas do mesmo lote com o mesmo tomador (ainda não
        // existente no banco) cairiam na mesma consulta "não existe",
        // cada uma criaria seu próprio Cliente, e o SaveChanges no final
        // do laço bateria na constraint única (TenantId, EmpresaId,
        // CpfCnpj) — confirmado no log real de erro.
        var clientesNestaExecucao = new Dictionary<string, Guid>();

        // Mesmo raciocínio pra NumeroDps+SerieDps: a constraint única
        // (TenantId, EmpresaId, NumeroDps, SerieDps) foi desenhada pra
        // garantir que NOSSA própria numeração sequencial (ContadorDps)
        // nunca duplique — mas notas importadas de OUTRO emissor (ex.:
        // Emissor Web oficial, com sua própria numeração) podem colidir
        // com essa constraint sem serem, de fato, a mesma nota (a
        // identidade real e única de uma nota é a ChaveAcesso, já
        // checada acima). Isso é confirmado: aconteceu de verdade num
        // lote real de homologação. Em vez de deixar o SaveChanges
        // final estourar e perder o lote inteiro, cada conflito é
        // detectado e a nota correspondente é pulada (contada
        // separadamente), preservando as demais.
        var numeracaoJaVista = new HashSet<(int NumeroDps, string SerieDps)>();

        // Notas adicionadas NESTA execução (ainda não salvas), por
        // ChaveAcesso — um evento de cancelamento pode vir no mesmo lote
        // da própria nota (NSU maior), antes do SaveChanges final.
        var notasNestaExecucao = new Dictionary<string, Nfse>();

        // TODOS os documentos do lote, não só NFSE: o cursor de NSU
        // precisa avançar sobre eventos também, e os eventos precisam ser
        // processados. Antes o filtro "NFSE" descartava os eventos e o
        // cursor passava por cima deles — o cancelamento nunca era visto.
        // Ordem por NSU garante que a nota é processada antes dos seus
        // eventos quando os dois vêm no mesmo lote.
        foreach (var item in lote.LoteDFe.OrderBy(d => d.NSU))
        {
            if (item.NSU > maiorNsu)
                maiorNsu = item.NSU;

            if (item.TipoDocumento != "NFSE")
            {
                if (await AplicarEventoAsync(empresaId, item, notasNestaExecucao, cancellationToken))
                    atualizadasPorEvento++;
                continue;
            }

            var jaExiste = await _db.NotasFiscais.AsNoTracking()
                .AnyAsync(n => n.EmpresaId == empresaId && n.ChaveAcesso == item.ChaveAcesso, cancellationToken);

            if (jaExiste)
            {
                jaExistentes++;
                continue;
            }

            var xml = GZipHelper.DescomprimirDeBase64(item.ArquivoXml);
            var dados = new NfseXmlImportDados(xml);

            var numeroDps = dados.NumeroDps();
            var serieDps = dados.SerieDps();

            var conflitaNoLote = !numeracaoJaVista.Add((numeroDps, serieDps));
            var conflitaNoBanco = !conflitaNoLote && await _db.NotasFiscais.AsNoTracking()
                .AnyAsync(n => n.EmpresaId == empresaId && n.NumeroDps == numeroDps && n.SerieDps == serieDps, cancellationToken);

            if (conflitaNoLote || conflitaNoBanco)
            {
                ignoradasPorConflito++;
                continue;
            }

            var (clienteId, clienteFoiCriado) = await ObterOuCriarClienteAsync(empresaId, dados, clientesNestaExecucao, cancellationToken);
            if (clienteFoiCriado)
                clientesCriados++;

            var snapshot = new NfseSnapshotFiscal(
                Versao: 1,
                Empresa: new NfseSnapshotEmpresa(
                    RazaoSocial: empresa.RazaoSocial,
                    NomeFantasia: empresa.NomeFantasia,
                    Cnpj: empresa.Cnpj,
                    InscricaoMunicipal: empresa.InscricaoMunicipal,
                    OpSimpNac: empresa.OpSimpNac,
                    RegApTribSN: empresa.RegApTribSN,
                    RegEspTrib: empresa.RegEspTrib,
                    Endereco: new NfseSnapshotEndereco(empresa.Cep, empresa.Logradouro, empresa.Numero, empresa.Complemento, empresa.Bairro, empresa.Uf)),
                Cliente: new NfseSnapshotCliente(
                    Nome: dados.TomadorNome(),
                    CpfCnpj: dados.TomadorCpfCnpj(),
                    Endereco: new NfseSnapshotEndereco(dados.TomadorCep(), dados.TomadorLogradouro(), dados.TomadorNumero(), dados.TomadorComplemento(), dados.TomadorBairro(), CodigosNfse.UfPorCodigoIbge(dados.TomadorCodigoMunicipio()))),
                Servico: new NfseSnapshotServico(
                    DescricaoCadastro: dados.DescricaoServico(),
                    ValorPadraoCadastro: dados.ValorServico()));

            var nfse = new Nfse
            {
                EmpresaId = empresaId,
                ClienteId = clienteId,
                NumeroDps = dados.NumeroDps(),
                SerieDps = dados.SerieDps(),
                NumeroNfse = dados.NumeroNfse(),
                ChaveAcesso = item.ChaveAcesso,
                DataCompetencia = dados.DataCompetencia(),
                DataEmissao = dados.DataEmissao(),
                ValorServico = dados.ValorServico(),
                DescricaoServico = dados.DescricaoServico(),
                CodigoTributacaoNacional = dados.CodigoTributacaoNacional(),
                CodigoNbs = dados.CodigoNbs(),
                TribIssqn = dados.TribIssqn(),
                TpRetIssqn = dados.TpRetIssqn(),
                CstPisCofins = dados.CstPisCofins(),
                TpRetPisCofins = dados.TpRetPisCofins(),
                PercentualTotalTributosSimplesNacional = dados.PercentualTotalTributosSimplesNacional(),
                ValorLiquido = dados.ValorLiquido(),
                XmlNfse = xml,
                SnapshotFiscalJson = JsonSerializer.Serialize(snapshot),
                // Sempre Autorizada aqui: o XML da NFS-e não muda depois de
                // gerado (cStat dele não reflete cancelamento posterior).
                // Cancelamento/substituição vêm pelos eventos do ADN — ver
                // AplicarEventoAsync.
                Status = NfseStatus.Autorizada,
                // Simplificação conhecida: usa o ambiente ATUAL da
                // Empresa, não o tpAmb original gravado no XML importado
                // (NfseXmlImportDados ainda não expõe esse campo). Numa
                // sincronização normal (mesmo dia/mesmo ambiente) dá no
                // mesmo; o caso raro que fica errado é importar, via ADN,
                // uma nota antiga de um ambiente que a Empresa já não
                // está mais — aceitável por ora, mas documentado aqui pra
                // não virar surpresa depois.
                TipoAmbiente = empresa.TipoAmbiente
            };

            _db.NotasFiscais.Add(nfse);
            notasNestaExecucao[item.ChaveAcesso] = nfse;
            _nfseEventoWriter.Registrar(nfse.Id, NfseEventoTipo.ImportadaDaSefin, mensagem: $"NSU {item.NSU}, via Distribuição de DF-e (ADN).");
            importadas++;
        }

        empresa.UltimoNsuDistribuicao = maiorNsu;

        _auditLogWriter.Registrar("SincronizarNotasDaSefin", "Empresa", empresaId,
            new { importadas, jaExistentes, ignoradasPorConflito, clientesCriados, atualizadasPorEvento, ultimoNsu = maiorNsu });

        await _db.SaveChangesAsync(cancellationToken);

        // Notas importadas (de outro emissor, tipicamente o Emissor Web
        // oficial do gov.br) podem usar a mesma SerieDps que a nossa
        // própria emissão usa ("00001" — faixa reservada a tpEmit=1,
        // qualquer aplicativo próprio, não só o nosso). O contador
        // interno (contadores_dps) nunca fica sabendo desses números,
        // porque eles chegam por aqui, não por EmitirNfseUseCase — sem
        // isto, uma emissão nossa post-sincronização podia gerar um
        // NumeroDps que colide com um já importado (confirmado: erro real
        // de constraint única em produção restrita). Alinha o contador
        // pra NUNCA ficar atrás do maior NumeroDps já visto pra cada
        // SerieDps desta Empresa — inclusive cobrindo sincronizações
        // anteriores a esta correção, já que reflete o estado atual da
        // tabela, não só o lote de agora.
        await AlinharContadorDpsAsync(empresaId, cancellationToken);

        return new SincronizacaoSefinResponse(importadas, jaExistentes, ignoradasPorConflito, clientesCriados, atualizadasPorEvento, maiorNsu);
    }

    /// <summary>
    /// Aplica um documento de evento do ADN sobre a NFS-e correspondente.
    /// Só cancelamento (101101), cancelamento por ofício (305101) e
    /// cancelamento por substituição (105102) mudam a situação da nota;
    /// os demais tipos são só registrados no log.
    ///
    /// Idempotente: nota já Cancelada/Substituida não é tocada de novo —
    /// cobre tanto reprocessar o mesmo NSU quanto o cancelamento feito
    /// por este próprio sistema (CancelarNfseUseCase), que também volta
    /// pelo ADN como evento.
    /// </summary>
    /// <returns>true se a situação da nota foi alterada.</returns>
    private async Task<bool> AplicarEventoAsync(
        Guid empresaId, DfeItem item, Dictionary<string, Nfse> notasNestaExecucao, CancellationToken cancellationToken)
    {
        NfseEventoXmlImportDados evento;
        try
        {
            evento = new NfseEventoXmlImportDados(GZipHelper.DescomprimirDeBase64(item.ArquivoXml));
        }
        catch (XmlException ex)
        {
            _logger.LogWarning(ex,
                "Sincronização SEFIN: Empresa {EmpresaId}, NSU {Nsu} (TipoDocumento {TipoDocumento}) com XML ilegível — ignorado.",
                empresaId, item.NSU, item.TipoDocumento);
            return false;
        }

        var codigoEvento = evento.CodigoTipoEvento();
        var novoStatus = NfseEventoXmlImportDados.StatusResultante(codigoEvento);

        if (novoStatus is null)
        {
            _logger.LogInformation(
                "Sincronização SEFIN: Empresa {EmpresaId}, NSU {Nsu} (TipoDocumento {TipoDocumento}, evento {CodigoEvento}) não altera a situação da nota — ignorado.",
                empresaId, item.NSU, item.TipoDocumento, codigoEvento ?? "(sem grupo de evento)");
            return false;
        }

        var chaveAcesso = evento.ChaveAcesso() ?? item.ChaveAcesso;

        if (!notasNestaExecucao.TryGetValue(chaveAcesso, out var nfse))
        {
            nfse = await _db.NotasFiscais
                .FirstOrDefaultAsync(n => n.EmpresaId == empresaId && n.ChaveAcesso == chaveAcesso, cancellationToken);
        }

        if (nfse is null)
        {
            // Nota não existe aqui (ex.: foi pulada por conflito de
            // numeração) — não há o que atualizar.
            _logger.LogWarning(
                "Sincronização SEFIN: Empresa {EmpresaId}, NSU {Nsu} — evento {CodigoEvento} para a chave {ChaveAcesso}, mas a nota não existe neste sistema — ignorado.",
                empresaId, item.NSU, codigoEvento, chaveAcesso);
            return false;
        }

        if (nfse.Status is NfseStatus.Cancelada or NfseStatus.Substituida)
            return false;

        nfse.Status = novoStatus.Value;

        var tipoEvento = novoStatus == NfseStatus.Cancelada ? NfseEventoTipo.Cancelada : NfseEventoTipo.Substituida;
        var mensagem = $"Registrado na SEFIN (NSU {item.NSU}, via Distribuição de DF-e/ADN). {evento.Motivo()}".Trim();
        if (mensagem.Length > 1000)
            mensagem = mensagem[..1000];

        _nfseEventoWriter.Registrar(nfse.Id, tipoEvento, codigoEvento, mensagem);

        _logger.LogInformation(
            "Sincronização SEFIN: Empresa {EmpresaId}, NSU {Nsu} — Nfse {NfseId} ({ChaveAcesso}) passou para {Status} pelo evento {CodigoEvento}.",
            empresaId, item.NSU, nfse.Id, chaveAcesso, nfse.Status, codigoEvento);

        return true;
    }

    private async Task AlinharContadorDpsAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO contadores_dps ("EmpresaId", "SerieDps", "UltimoNumero")
            SELECT "EmpresaId", "SerieDps", MAX("NumeroDps")
            FROM notas_fiscais
            WHERE "EmpresaId" = {empresaId}
            GROUP BY "EmpresaId", "SerieDps"
            ON CONFLICT ("EmpresaId", "SerieDps")
            DO UPDATE SET "UltimoNumero" = GREATEST(contadores_dps."UltimoNumero", EXCLUDED."UltimoNumero")
            """, cancellationToken);
    }

    private async Task<(Guid ClienteId, bool Criado)> ObterOuCriarClienteAsync(
        Guid empresaId, NfseXmlImportDados dados, Dictionary<string, Guid> clientesNestaExecucao, CancellationToken cancellationToken)
    {
        var cpfCnpj = dados.TomadorCpfCnpj();

        // 1) já criado nesta mesma execução (ainda não salvo no banco) —
        // ver comentário completo no chamador.
        if (clientesNestaExecucao.TryGetValue(cpfCnpj, out var clienteIdEmMemoria))
            return (clienteIdEmMemoria, false);

        // 2) já existe no banco, de uma sincronização/cadastro anterior.
        var clienteExistente = await _db.Clientes
            .FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.CpfCnpj == cpfCnpj, cancellationToken);

        if (clienteExistente is not null)
        {
            clientesNestaExecucao[cpfCnpj] = clienteExistente.Id;
            return (clienteExistente.Id, false);
        }

        // 3) realmente novo. Apelido é obrigatório e o XML não tem um —
        // gera a partir da razão social (o usuário ajusta depois no
        // cadastro).
        var nome = dados.TomadorNome();
        var apelido = await ApelidoCliente.GerarAPartirDoNomeAsync(_db, empresaId, nome, cpfCnpj, cancellationToken);

        var novoCliente = new Cliente
        {
            EmpresaId = empresaId,
            CpfCnpj = cpfCnpj,
            Nome = nome,
            Apelido = apelido,
            Email = dados.TomadorEmail(),
            Telefone = dados.TomadorTelefone(),
            CodigoMunicipio = dados.TomadorCodigoMunicipio(),
            Cep = dados.TomadorCep(),
            Logradouro = dados.TomadorLogradouro(),
            Numero = dados.TomadorNumero(),
            Complemento = dados.TomadorComplemento(),
            Bairro = dados.TomadorBairro(),
            Uf = CodigosNfse.UfPorCodigoIbge(dados.TomadorCodigoMunicipio())
        };

        _db.Clientes.Add(novoCliente);
        clientesNestaExecucao[cpfCnpj] = novoCliente.Id;
        return (novoCliente.Id, true);
    }
}
