using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.UseCases.DadosConta;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Domain.Legal;
using NfseSaaS.Infrastructure.Danfse;
using NfseSaaS.Infrastructure.Documents;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;

namespace NfseSaaS.Infrastructure.DadosConta;

/// <summary>
/// Monta o ZIP da exportação dos dados da conta (D1). Roda com a conta
/// definida no escopo: todas as consultas passam pelo filtro de conta
/// (usuários, que não têm filtro, são filtrados à mão).
///
/// Conteúdo:
/// - LEIAME.txt: o que tem, formatos e o que ficou de fora;
/// - dados/*.csv e dados/*.json: conta, usuários, empresas, clientes,
///   serviços, contratos (com serviços, histórico de valores e documentos),
///   notas e eventos das notas;
/// - notas/{CNPJ}/{ambiente}/{AAAA-MM}/: XML oficial e PDF (DANFSe) das notas
///   autorizadas e canceladas;
/// - contratos/{CNPJ}/: os documentos anexados aos contratos.
/// Ficam de fora, de propósito: certificados digitais e senhas (segurança),
/// Histórico/auditoria e e-mails enviados (registros técnicos da plataforma).
///
/// As notas são lidas em lotes (o XML de cada uma pode ser grande) e uma
/// falha pontual (PDF que não gera, documento sumido do disco) não derruba
/// a exportação: vai para a lista de pendências do LEIAME.
/// </summary>
public sealed class MontadorExportacaoDados
{
    private const int LoteNotas = 100;
    private static readonly TimeSpan OffsetBrasilia = TimeSpan.FromHours(-3);

    private readonly AppDbContext _db;
    private readonly ContratoDocumentoFileStore _documentos;
    private readonly IWebHostEnvironment _ambiente;
    private readonly ILogger<MontadorExportacaoDados> _logger;

    public MontadorExportacaoDados(AppDbContext db, ContratoDocumentoFileStore documentos, IWebHostEnvironment ambiente, ILogger<MontadorExportacaoDados> logger)
    {
        _db = db;
        _documentos = documentos;
        _ambiente = ambiente;
        _logger = logger;
    }

    /// <returns>Quantidade de notas exportadas.</returns>
    public async Task<int> MontarAsync(Guid tenantId, Stream destino, CancellationToken cancellationToken)
    {
        var pendencias = new List<string>();
        var nomesUsados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var agora = DateTimeOffset.UtcNow;

        using var zip = new ZipArchive(destino, ZipArchiveMode.Create, leaveOpen: true);

        var conta = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, cancellationToken);
        var assinatura = await _db.Assinaturas.AsNoTracking().FirstOrDefaultAsync(a => a.TenantId == tenantId, cancellationToken);
        var nomePlano = assinatura?.Plano is { } codigoPlano
            ? await _db.Planos.AsNoTracking().Where(p => p.Codigo == codigoPlano).Select(p => p.Nome).FirstOrDefaultAsync(cancellationToken)
            : null;

        var tContas = new TabelaExportacao("conta",
            "Documento", "Perfil", "RazaoSocial", "NomeFantasia", "Ativa", "CriadaEm", "CodigoIndicacao",
            "Plano", "SituacaoAssinatura", "PrecoMensal", "Fundador", "AssinadaEm", "CanceladaEm");
        tContas.Adicionar(conta.Documento, conta.Perfil, conta.RazaoSocial, conta.NomeFantasia, conta.Ativo, conta.CreatedAt,
            conta.CodigoIndicacao, nomePlano, assinatura?.Status, assinatura?.PrecoMensal, assinatura?.Fundador,
            assinatura?.AssinadaEm, assinatura?.CanceladaEm);
        Tabela(zip, tContas);

        // ----- Usuários (AspNetUsers não tem filtro de conta: filtro manual) -----
        var grupos = await _db.Grupos.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Nome, cancellationToken);
        var usuarios = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.Email)
            .Select(u => new { u.Id, u.Email, u.GrupoId, u.EmailConfirmed, u.ConvidadoEm, u.ConviteAceitoEm, u.LockoutEnd })
            .ToListAsync(cancellationToken);
        var tUsuarios = new TabelaExportacao("usuarios",
            "Id", "Email", "Grupo", "EmailConfirmado", "ConvidadoEm", "ConviteAceitoEm", "Bloqueado");
        foreach (var u in usuarios)
            tUsuarios.Adicionar(u.Id, u.Email, u.GrupoId is { } g ? grupos.GetValueOrDefault(g) : null, u.EmailConfirmed,
                u.ConvidadoEm, u.ConviteAceitoEm, u.LockoutEnd is { } fim && fim > agora);
        Tabela(zip, tUsuarios);

        // ----- Empresas (sem certificado nem senha) -----
        var empresas = await _db.Empresas.AsNoTracking().OrderBy(e => e.RazaoSocial).ToListAsync(cancellationToken);
        var cnpjPorEmpresa = empresas.ToDictionary(e => e.Id, e => e.Cnpj);
        var tEmpresas = new TabelaExportacao("empresas",
            "Id", "Cnpj", "RazaoSocial", "NomeFantasia", "InscricaoMunicipal", "CodigoMunicipio", "Telefone", "Email",
            "Cep", "Logradouro", "Numero", "Complemento", "Bairro", "Uf",
            "OpcaoSimplesNacional", "RegimeApuracaoSimples", "RegimeEspecialTributacao", "TributacaoIssqn", "AliquotaIss",
            "CstPisCofins", "TipoRetencaoPisCofins", "PercentualTributosSimplesNacional",
            "Ativa", "Ambiente", "CriadaEm");
        foreach (var e in empresas)
            tEmpresas.Adicionar(e.Id, e.Cnpj, e.RazaoSocial, e.NomeFantasia, e.InscricaoMunicipal, e.CodigoMunicipio, e.Telefone, e.Email,
                e.Cep, e.Logradouro, e.Numero, e.Complemento, e.Bairro, e.Uf,
                e.OpSimpNac, e.RegApTribSN, e.RegEspTrib, e.TribIssqn, e.AliquotaIss,
                e.CstPisCofins, e.TpRetPisCofins, e.PercentualTotalTributosSimplesNacional,
                e.Ativo, e.TipoAmbiente, e.CreatedAt);
        Tabela(zip, tEmpresas);

        string Cnpj(Guid empresaId) => cnpjPorEmpresa.GetValueOrDefault(empresaId, "sem-empresa");

        // ----- Clientes -----
        var clientes = await _db.Clientes.AsNoTracking().OrderBy(c => c.Nome).ToListAsync(cancellationToken);
        var clientePorId = clientes.ToDictionary(c => c.Id);
        var tClientes = new TabelaExportacao("clientes",
            "Id", "EmpresaCnpj", "CpfCnpj", "Nome", "Apelido", "Email", "EmailsCopia", "Telefone",
            "CodigoMunicipio", "Cep", "Logradouro", "Numero", "Complemento", "Bairro", "Uf",
            "RetemIss", "EnviarNotaPorEmail", "Ativo", "CriadoEm");
        foreach (var c in clientes)
            // C10: cliente compartilhado entre as empresas da conta = "Todas".
            tClientes.Adicionar(c.Id, c.EmpresaId is Guid empresaCliente ? Cnpj(empresaCliente) : "Todas", c.CpfCnpj, c.Nome, c.Apelido, c.Email, c.EmailsCopia, c.Telefone,
                c.CodigoMunicipio, c.Cep, c.Logradouro, c.Numero, c.Complemento, c.Bairro, c.Uf,
                c.RetemIss, c.EnviarNfsePorEmail, c.Ativo, c.CreatedAt);
        Tabela(zip, tClientes);

        // ----- Serviços -----
        var servicos = await _db.Servicos.AsNoTracking().OrderBy(s => s.Descricao).ToListAsync(cancellationToken);
        var descricaoServico = servicos.ToDictionary(s => s.Id, s => s.Descricao);
        var tServicos = new TabelaExportacao("servicos",
            "Id", "EmpresaCnpj", "Descricao", "CodigoTributacaoNacional", "CodigoNbs", "IndicadorOperacaoIbsCbs",
            "ClassificacaoTributariaIbsCbs", "ValorPadrao", "Ativo", "CriadoEm");
        foreach (var s in servicos)
            tServicos.Adicionar(s.Id, Cnpj(s.EmpresaId), s.Descricao, s.CodigoTributacaoNacional, s.CodigoNbs, s.CIndOp,
                s.CClassTrib, s.ValorPadrao, s.Ativo, s.CreatedAt);
        Tabela(zip, tServicos);

        // ----- Contratos -----
        var contratos = await _db.Contratos.AsNoTracking().OrderBy(c => c.Descricao).ToListAsync(cancellationToken);
        var tContratos = new TabelaExportacao("contratos",
            "Id", "EmpresaCnpj", "ClienteId", "Cliente", "Descricao", "ValorAtual", "DataInicio", "DataFim", "Status",
            "TipoCobranca", "PermitirAlterarValorNaEmissao", "PeriodicidadeReajusteMeses", "IndiceReajuste",
            "DataUltimoReajuste", "ReajusteAutomatico", "AplicarIndiceNegativo", "Observacao", "CriadoEm");
        foreach (var c in contratos)
            tContratos.Adicionar(c.Id, Cnpj(c.EmpresaId), c.ClienteId, clientePorId.GetValueOrDefault(c.ClienteId)?.Apelido, c.Descricao,
                c.ValorAtual, c.DataInicioContrato, c.DataFim, c.Status, c.TipoCobranca, c.PermitirAlterarValorNaEmissao,
                c.PeriodicidadeReajusteMeses, c.IndiceReajuste, c.DataUltimoReajuste, c.ReajusteAutomatico,
                c.AplicarIndiceNegativo, c.Observacao, c.CreatedAt);
        Tabela(zip, tContratos);

        var itens = await _db.ContratoServicos.AsNoTracking().OrderBy(i => i.ContratoId).ToListAsync(cancellationToken);
        var tItens = new TabelaExportacao("contratos_servicos", "ContratoId", "ServicoId", "Servico", "Quantidade", "ValorUnitario");
        foreach (var i in itens)
            tItens.Adicionar(i.ContratoId, i.ServicoId, descricaoServico.GetValueOrDefault(i.ServicoId), i.Quantidade, i.ValorUnitario);
        Tabela(zip, tItens);

        var movimentacoes = await _db.MovimentacoesValorContrato.AsNoTracking()
            .OrderBy(m => m.ContratoId).ThenBy(m => m.DataVigencia).ToListAsync(cancellationToken);
        var tMovimentacoes = new TabelaExportacao("contratos_historico_valores",
            "ContratoId", "Tipo", "Status", "DataVigencia", "ValorAnterior", "ValorNovo", "PercentualCalculado",
            "PercentualAplicado", "Indice", "CompetenciaIndiceInicio", "CompetenciaIndiceFim", "Motivo", "Automatico",
            "DecididoEm", "CriadoEm");
        foreach (var m in movimentacoes)
            tMovimentacoes.Adicionar(m.ContratoId, m.Tipo, m.Status, m.DataVigencia, m.ValorAnterior, m.ValorNovo,
                m.PercentualCalculado, m.PercentualAplicado, m.Indice, m.CompetenciaIndiceInicio, m.CompetenciaIndiceFim,
                m.Motivo, m.Automatico, m.DecididoEm, m.CreatedAt);
        Tabela(zip, tMovimentacoes);

        // ----- Documentos dos contratos (arquivo + lista) -----
        var contratoPorId = contratos.ToDictionary(c => c.Id);
        var documentos = await _db.ContratoDocumentos.AsNoTracking().OrderBy(d => d.ContratoId).ThenBy(d => d.CreatedAt).ToListAsync(cancellationToken);
        var tDocumentos = new TabelaExportacao("contratos_documentos", "ContratoId", "Contrato", "NomeOriginal", "TamanhoBytes", "EnviadoEm", "ArquivoNoZip");
        foreach (var d in documentos)
        {
            var contrato = contratoPorId.GetValueOrDefault(d.ContratoId);
            var pasta = $"contratos/{Cnpj(d.EmpresaId)}/{NomeSeguro(contrato?.Descricao ?? "contrato", 60)}-{d.ContratoId.ToString("N")[..8]}";
            var caminho = Unico(nomesUsados, pasta, NomeSeguro(Path.GetFileNameWithoutExtension(d.NomeOriginal), 80), d.Extensao);
            string? noZip = null;
            try
            {
                var bytes = await _documentos.LerAsync(tenantId, d.EmpresaId, d.ContratoId, d.Id, d.Extensao, cancellationToken);
                await GravarAsync(zip, caminho, bytes, CompressionLevel.Fastest, cancellationToken);
                noZip = caminho;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException)
            {
                pendencias.Add($"Documento \"{d.NomeOriginal}\" do contrato \"{contrato?.Descricao}\": arquivo não encontrado no servidor.");
                _logger.LogWarning("Exportação: documento {DocumentoId} da conta {TenantId} não encontrado no disco.", d.Id, tenantId);
            }
            tDocumentos.Adicionar(d.ContratoId, contrato?.Descricao, d.NomeOriginal, d.TamanhoBytes, d.CreatedAt, noZip);
        }
        Tabela(zip, tDocumentos);

        // ----- Notas (em lotes) -----
        var tNotas = new TabelaExportacao("notas",
            "Id", "EmpresaCnpj", "Ambiente", "Status", "NumeroNfse", "SerieDps", "NumeroDps", "ChaveAcesso",
            "DataCompetencia", "DataEmissao", "ClienteId", "Cliente", "ClienteCpfCnpj", "ContratoId", "ServicoId",
            "ValorServico", "ValorLiquido", "DescricaoServico", "CodigoTributacaoNacional", "CodigoNbs",
            "IndicadorOperacaoIbsCbs", "ClassificacaoTributariaIbsCbs", "TributacaoIssqn", "TipoRetencaoIssqn", "AliquotaIss",
            "CodigoErro", "MensagemErro", "ArquivoXml", "ArquivoPdf", "CriadaEm");
        var caminhoLogo = Path.Combine(_ambiente.WebRootPath ?? string.Empty, "img", "logo-nfse-horizontal.png");
        var totalNotas = 0;
        for (var pagina = 0; ; pagina++)
        {
            var lote = await _db.NotasFiscais.AsNoTracking()
                .OrderBy(n => n.DataCompetencia).ThenBy(n => n.Id)
                .Skip(pagina * LoteNotas).Take(LoteNotas)
                .ToListAsync(cancellationToken);
            if (lote.Count == 0)
                break;

            foreach (var n in lote)
            {
                totalNotas++;
                var cliente = clientePorId.GetValueOrDefault(n.ClienteId);
                string? arquivoXml = null, arquivoPdf = null;

                if (n.Status is (NfseStatus.Autorizada or NfseStatus.Cancelada) && !string.IsNullOrWhiteSpace(n.XmlNfse))
                {
                    var dataArquivo = n.DataEmissao is { } emissao ? DateOnly.FromDateTime(emissao.ToOffset(OffsetBrasilia).DateTime) : n.DataCompetencia;
                    var pasta = $"notas/{Cnpj(n.EmpresaId)}/{(n.TipoAmbiente == TipoAmbiente.Producao ? "producao" : "homologacao")}/{n.DataCompetencia:yyyy-MM}";
                    var baseNome = Path.GetFileNameWithoutExtension(ObterDanfsePdfUseCase.MontarNomeArquivo(cliente?.Apelido ?? "Cliente", n.NumeroDps, dataArquivo));
                    var caminhoXml = Unico(nomesUsados, pasta, baseNome, ".xml");
                    await GravarAsync(zip, caminhoXml, Encoding.UTF8.GetBytes(n.XmlNfse), CompressionLevel.Optimal, cancellationToken);
                    arquivoXml = caminhoXml;

                    try
                    {
                        var pdf = DanfsePdfGenerator.Gerar(n.XmlNfse, cancelada: n.Status == NfseStatus.Cancelada, caminhoLogo: caminhoLogo);
                        var caminhoPdf = Path.ChangeExtension(caminhoXml, ".pdf");
                        nomesUsados.Add(caminhoPdf);
                        await GravarAsync(zip, caminhoPdf, pdf, CompressionLevel.Fastest, cancellationToken);
                        arquivoPdf = caminhoPdf;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        pendencias.Add($"Nota {n.NumeroNfse ?? n.NumeroDps.ToString()} ({cliente?.Apelido}): o PDF não pôde ser gerado; o XML oficial está no arquivo.");
                        _logger.LogWarning(ex, "Exportação: PDF da nota {NfseId} da conta {TenantId} não gerado.", n.Id, tenantId);
                    }
                }

                tNotas.Adicionar(n.Id, Cnpj(n.EmpresaId), n.TipoAmbiente, n.Status, n.NumeroNfse, n.SerieDps, n.NumeroDps, n.ChaveAcesso,
                    n.DataCompetencia, n.DataEmissao, n.ClienteId, cliente?.Nome, cliente?.CpfCnpj, n.ContratoId, n.ServicoId,
                    n.ValorServico, n.ValorLiquido, n.DescricaoServico, n.CodigoTributacaoNacional, n.CodigoNbs,
                    n.CIndOp, n.CClassTrib, n.TribIssqn, n.TpRetIssqn, n.AliquotaIss,
                    n.CodigoErro, n.MensagemErro, arquivoXml, arquivoPdf, n.CreatedAt);
            }
        }
        Tabela(zip, tNotas);

        var eventos = await _db.NfseEventos.AsNoTracking().OrderBy(e => e.NfseId).ThenBy(e => e.CreatedAt).ToListAsync(cancellationToken);
        var tEventos = new TabelaExportacao("notas_eventos", "NotaId", "Tipo", "Codigo", "Mensagem", "Data");
        foreach (var e in eventos)
            tEventos.Adicionar(e.NfseId, e.Tipo, e.Codigo, e.Mensagem, e.CreatedAt);
        Tabela(zip, tEventos);

        await GravarAsync(zip, "LEIAME.txt", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Leiame(conta.RazaoSocial, conta.Documento, agora, totalNotas, pendencias))).ToArray(),
            CompressionLevel.Optimal, cancellationToken);

        return totalNotas;
    }

    internal static string Leiame(string razaoSocial, string documento, DateTimeOffset geradoEm, int notas, IReadOnlyList<string> pendencias)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Exportação dos dados — {DocumentosLegais.NomeServico}");
        sb.AppendLine($"Conta: {razaoSocial} ({documento})");
        sb.AppendLine($"Gerada em: {geradoEm.ToOffset(OffsetBrasilia):dd/MM/yyyy HH:mm} (horário de Brasília)");
        sb.AppendLine($"Notas: {notas}");
        sb.AppendLine();
        sb.AppendLine("O QUE TEM AQUI");
        sb.AppendLine("- dados/: os cadastros e o histórico da conta, cada tabela em dois formatos:");
        sb.AppendLine("  .csv (abre no Excel; separador ponto e vírgula, datas no horário de Brasília) e");
        sb.AppendLine("  .json (para levar a outro sistema; datas no padrão ISO 8601).");
        sb.AppendLine("  conta, usuarios, empresas, clientes, servicos, contratos, contratos_servicos,");
        sb.AppendLine("  contratos_historico_valores, contratos_documentos, notas, notas_eventos.");
        sb.AppendLine("- notas/{CNPJ}/{producao|homologacao}/{AAAA-MM}/: o XML oficial e o PDF (DANFSe) de cada nota");
        sb.AppendLine("  autorizada ou cancelada. O XML é o documento fiscal válido; guarde pelo prazo legal.");
        sb.AppendLine("  Notas de homologação são de teste e não têm valor fiscal.");
        sb.AppendLine("- contratos/{CNPJ}/: os documentos anexados aos contratos.");
        sb.AppendLine("As tabelas se ligam pelos campos Id (ex.: notas.ClienteId = clientes.Id).");
        sb.AppendLine();
        sb.AppendLine("O QUE FICOU DE FORA, DE PROPÓSITO");
        sb.AppendLine("- Certificados digitais e senhas: por segurança, nunca saem da plataforma.");
        sb.AppendLine("  Use o arquivo .pfx original que você enviou.");
        sb.AppendLine("- Histórico (auditoria) e e-mails enviados: são registros técnicos da plataforma.");
        sb.AppendLine("  Se precisar deles, peça pelo e-mail abaixo.");
        sb.AppendLine("- Notas rejeitadas ou não enviadas não têm XML oficial: aparecem só em dados/notas.");
        if (pendencias.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("PENDÊNCIAS NESTA EXPORTAÇÃO");
            foreach (var p in pendencias)
                sb.AppendLine("- " + p);
        }
        sb.AppendLine();
        sb.AppendLine($"Dúvidas sobre os seus dados: {DocumentosLegais.EmailPrivacidade}");
        return sb.ToString();
    }

    private static void Tabela(ZipArchive zip, TabelaExportacao tabela)
    {
        Gravar(zip, $"dados/{tabela.Nome}.csv", tabela.ParaCsv());
        Gravar(zip, $"dados/{tabela.Nome}.json", tabela.ParaJson());
    }

    private static void Gravar(ZipArchive zip, string caminho, byte[] bytes)
    {
        var entrada = zip.CreateEntry(caminho, CompressionLevel.Optimal);
        using var s = entrada.Open();
        s.Write(bytes);
    }

    private static async Task GravarAsync(ZipArchive zip, string caminho, byte[] bytes, CompressionLevel nivel, CancellationToken cancellationToken)
    {
        var entrada = zip.CreateEntry(caminho, nivel);
        await using var s = entrada.Open();
        await s.WriteAsync(bytes, cancellationToken);
    }

    /// <summary>Caminho único dentro do ZIP (desempata com " (2)", " (3)"...).</summary>
    internal static string Unico(HashSet<string> usados, string pasta, string nome, string extensao)
    {
        var caminho = $"{pasta}/{nome}{extensao}";
        for (var i = 2; !usados.Add(caminho); i++)
            caminho = $"{pasta}/{nome} ({i}){extensao}";
        return caminho;
    }

    /// <summary>Nome de pasta/arquivo sem caracteres inválidos nem barras (nada de "../" dentro do ZIP).</summary>
    internal static string NomeSeguro(string texto, int maximo)
    {
        var invalidos = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var limpo = new string(texto.Trim().Select(c => invalidos.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim('.', ' ');
        if (limpo.Length == 0)
            limpo = "arquivo";
        return limpo.Length <= maximo ? limpo : limpo[..maximo].TrimEnd('.', ' ');
    }
}
