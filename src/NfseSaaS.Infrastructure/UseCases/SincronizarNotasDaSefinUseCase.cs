using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
/// </summary>
public sealed class SincronizarNotasDaSefinUseCase : ISincronizarNotasDaSefinUseCase
{
    private readonly AppDbContext _db;
    private readonly IAdnDistribuicaoClient _adnClient;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INfseEventoWriter _nfseEventoWriter;

    public SincronizarNotasDaSefinUseCase(
        AppDbContext db,
        IAdnDistribuicaoClient adnClient,
        IAuditLogWriter auditLogWriter,
        INfseEventoWriter nfseEventoWriter)
    {
        _db = db;
        _adnClient = adnClient;
        _auditLogWriter = auditLogWriter;
        _nfseEventoWriter = nfseEventoWriter;
    }

    public async Task<SincronizacaoSefinResponse> ExecutarAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas.FirstOrDefaultAsync(e => e.Id == empresaId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        var ultimoNsu = empresa.UltimoNsuDistribuicao ?? 0;
        var lote = await _adnClient.ConsultarPorNsuAsync(empresaId, ultimoNsu, cancellationToken);

        int importadas = 0, jaExistentes = 0, ignoradasPorConflito = 0, clientesCriados = 0;
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

        foreach (var item in lote.LoteDFe.Where(d => d.TipoDocumento == "NFSE").OrderBy(d => d.NSU))
        {
            if (item.NSU > maiorNsu)
                maiorNsu = item.NSU;

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

            var status = dados.CStat() switch
            {
                "101" => NfseStatus.Cancelada,
                "102" => NfseStatus.Substituida,
                _ => NfseStatus.Autorizada
            };

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
                Status = status
            };

            _db.NotasFiscais.Add(nfse);
            _nfseEventoWriter.Registrar(nfse.Id, NfseEventoTipo.ImportadaDaSefin, mensagem: $"NSU {item.NSU}, via Distribuição de DF-e (ADN).");
            importadas++;
        }

        empresa.UltimoNsuDistribuicao = maiorNsu;

        _auditLogWriter.Registrar("SincronizarNotasDaSefin", "Empresa", empresaId,
            new { importadas, jaExistentes, ignoradasPorConflito, clientesCriados, ultimoNsu = maiorNsu });

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

        return new SincronizacaoSefinResponse(importadas, jaExistentes, ignoradasPorConflito, clientesCriados, maiorNsu);
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

        // 3) realmente novo.
        var novoCliente = new Cliente
        {
            EmpresaId = empresaId,
            CpfCnpj = cpfCnpj,
            Nome = dados.TomadorNome(),
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
