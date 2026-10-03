using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Domain.Snapshots;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Nacional.Exceptions;
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
///
/// Reenvio de nota rejeitada (request.ReenvioDeNfseId, ou automático
/// quando a IdempotencyKey aponta pra uma tentativa rejeitada — caso da
/// Nota Mensal em lote): é uma emissão NOVA, pelo mesmo caminho, com novo
/// NumeroDps e snapshot relido do cadastro atual. A rejeitada fica
/// intacta, ligada à nova por Nfse.ReenvioDeNfseId, e a IdempotencyKey
/// passa pra nova tentativa. Ver Nfse.RejeicaoPermiteReenvio pra regra de
/// quando reenviar é seguro.
/// </summary>
public sealed class EmitirNfseUseCase : IEmitirNfseUseCase
{
    // Faixa 00001-49999 = emissão com aplicativo próprio (tpEmit=1) — ver
    // comentário completo em NfseSaaS.Nacional.Builders.DpsBuilder.
    private const string SerieDps = "00001";

    // Mesmo limite de NfseConfiguration.MensagemErro / NfseEvento.Mensagem.
    private const int TamanhoMaximoMensagem = 1000;

    private readonly AppDbContext _db;
    private readonly INfseNacionalService _nfseNacionalService;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INfseEventoWriter _nfseEventoWriter;

    public EmitirNfseUseCase(
        AppDbContext db,
        INfseNacionalService nfseNacionalService,
        IAuditLogWriter auditLogWriter,
        INfseEventoWriter nfseEventoWriter)
    {
        _db = db;
        _nfseNacionalService = nfseNacionalService;
        _auditLogWriter = auditLogWriter;
        _nfseEventoWriter = nfseEventoWriter;
    }

    public async Task<EmitirNfseResult> ExecutarAsync(EmitirNfseRequest request, CancellationToken cancellationToken)
    {
        // Idempotência: se o chamador informou uma chave e já existe uma
        // Nfse com ela para esta Empresa, é um retry da MESMA tentativa —
        // devolve o resultado já existente em vez de emitir de novo (e,
        // consequentemente, sem gastar um NumeroDps novo à toa). Não gera
        // novo evento de auditoria: nada de fato aconteceu nesta chamada.
        //
        // EXCEÇÃO: a tentativa existente foi rejeitada de um jeito que
        // permite reenvio (ver Nfse.RejeicaoPermiteReenvio). Aí a mesma
        // chave vira um reenvio — sem isso, uma Nota Mensal rejeitada (ex.:
        // CEP do Cliente errado) devolvia a MESMA rejeição antiga pra
        // sempre, mesmo depois do cadastro corrigido, sem nem chamar a
        // SEFIN de novo (lote manual e automação).
        Domain.Entities.Nfse? original = null;

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existente = await _db.NotasFiscais.FirstOrDefaultAsync(
                n => n.EmpresaId == request.EmpresaId && n.IdempotencyKey == request.IdempotencyKey,
                cancellationToken);

            if (existente is not null)
            {
                if (!existente.RejeicaoPermiteReenvio())
                    return ResultadoDaTentativaExistente(existente);

                original = existente;
            }
        }

        if (request.ReenvioDeNfseId is { } reenvioDeNfseId)
        {
            if (original is not null && original.Id != reenvioDeNfseId)
                throw new RegraNegocioException("A chave de idempotência informada pertence a outra nota.");

            original ??= await _db.NotasFiscais.FirstOrDefaultAsync(
                    n => n.Id == reenvioDeNfseId && n.EmpresaId == request.EmpresaId, cancellationToken)
                ?? throw new RecursoNaoEncontradoException($"Nfse {reenvioDeNfseId} não encontrada.");

            if (!original.RejeicaoPermiteReenvio())
            {
                throw new RegraNegocioException(original.Status == NfseStatus.Rejeitada
                    ? "Esta nota falhou sem resposta da SEFIN — ela pode ter sido autorizada mesmo assim. " +
                      "Confira no portal nacional ou use \"Buscar notas da SEFIN\" antes de emitir de novo."
                    : "Só uma nota rejeitada pode ser reenviada.");
            }
        }

        if (original is not null)
            await ValidarReenvioAsync(original, request, cancellationToken);

        var empresa = await _db.Empresas.FirstOrDefaultAsync(e => e.Id == request.EmpresaId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Empresa {request.EmpresaId} não encontrada.");

        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Id == request.ClienteId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Cliente {request.ClienteId} não encontrado.");

        var servico = await _db.Servicos.FirstOrDefaultAsync(s => s.Id == request.ServicoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Serviço {request.ServicoId} não encontrado.");

        // Contrato é opcional (nota pode ser avulsa, fora de qualquer
        // Contrato) — mas se informado, precisa realmente pertencer a
        // este Cliente. Não confiamos cegamente no ContratoId que veio da
        // tela: um Contrato de outro Cliente aqui seria um vínculo de
        // rastreabilidade errado (mesmo a Nfse em si continuando correta,
        // já que Servico/Valor/Descrição vêm de request, não do Contrato).
        if (request.ContratoId.HasValue)
        {
            var contratoValido = await _db.Contratos.AnyAsync(
                c => c.Id == request.ContratoId.Value && c.ClienteId == request.ClienteId && c.EmpresaId == request.EmpresaId,
                cancellationToken);

            if (!contratoValido)
                throw new RegraNegocioException($"Contrato {request.ContratoId} não encontrado para este Cliente/Empresa.");
        }

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
        var snapshotFiscal = new NfseSnapshotFiscal(
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
                Nome: cliente.Nome,
                CpfCnpj: cliente.CpfCnpj,
                Endereco: new NfseSnapshotEndereco(cliente.Cep, cliente.Logradouro, cliente.Numero, cliente.Complemento, cliente.Bairro, cliente.Uf)),
            Servico: new NfseSnapshotServico(
                DescricaoCadastro: servico.Descricao,
                ValorPadraoCadastro: servico.ValorPadrao));

        // Reenvio: a chave de idempotência da tentativa rejeitada passa
        // pra esta (chamadas futuras com a mesma chave — ex.: lote de Nota
        // Mensal — passam a enxergar a tentativa mais recente). Salvo
        // ANTES do insert, em separado, por causa do índice único em
        // (TenantId, EmpresaId, IdempotencyKey). Se o resto falhar depois
        // disso, a rejeitada só fica sem chave — inofensivo.
        var idempotencyKey = request.IdempotencyKey;
        if (original is not null && original.IdempotencyKey is not null)
        {
            idempotencyKey = original.IdempotencyKey;
            original.IdempotencyKey = null;
            await _db.SaveChangesAsync(cancellationToken);
        }

        var nfse = new Domain.Entities.Nfse
        {
            EmpresaId = empresa.Id,
            ClienteId = cliente.Id,
            ContratoId = request.ContratoId,
            ServicoId = servico.Id,
            ReenvioDeNfseId = original?.Id,
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
            IdempotencyKey = idempotencyKey,
            SnapshotFiscalJson = JsonSerializer.Serialize(snapshotFiscal),
            Status = NfseStatus.Processando,
            TipoAmbiente = empresa.TipoAmbiente
        };

        _db.NotasFiscais.Add(nfse);
        _nfseEventoWriter.Registrar(nfse.Id, NfseEventoTipo.DpsEnviada,
            mensagem: original is null ? null : $"Reenvio da DPS nº {original.NumeroDps}/{original.SerieDps}, rejeitada ({original.CodigoErro}).");

        if (original is not null)
            _auditLogWriter.Registrar("ReenviarNfse", "Nfse", original.Id, new { NovaNfseId = nfse.Id, NovoNumeroDps = numeroDps });

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
            DescricaoServico: request.DescricaoServico,
            // Tradução Domain → Nacional acontece só aqui, no único lugar
            // que conhece os dois lados (ver comentário em DpsRequest.TpAmb
            // sobre por que o módulo Nacional não pode fazer essa tradução
            // sozinho). Lê de nfse.TipoAmbiente (já congelado acima), não
            // de empresa.TipoAmbiente de novo — mesma fonte que vai pro
            // banco, sem chance de os dois divergirem.
            TpAmb: nfse.TipoAmbiente.ParaTpAmb());

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
                {
                    nfse.XmlNfse = GZipHelper.DescomprimirDeBase64(resposta.NfseXmlGZipB64);
                    nfse.ValorLiquido = NfseXmlValoresParser.ExtrairValorLiquido(nfse.XmlNfse);
                }

                _nfseEventoWriter.Registrar(nfse.Id, NfseEventoTipo.Autorizada, mensagem: nfse.ChaveAcesso);
            }
            else
            {
                var primeiroErro = resposta.Erros.FirstOrDefault();
                nfse.Status = NfseStatus.Rejeitada;
                nfse.CodigoErro = primeiroErro?.Codigo;
                nfse.MensagemErro = primeiroErro?.Descricao;

                _nfseEventoWriter.Registrar(nfse.Id, NfseEventoTipo.Rejeitada, primeiroErro?.Codigo, primeiroErro?.Descricao);
            }

            _auditLogWriter.Registrar("EmitirNfse", "Nfse", nfse.Id, new { nfse.Status, nfse.ChaveAcesso, nfse.ValorServico, nfse.CodigoErro, nfse.MensagemErro });

            await _db.SaveChangesAsync(cancellationToken);

            return new EmitirNfseResult(nfse.Id, resposta.Sucesso, nfse.NumeroNfse, nfse.ChaveAcesso, nfse.CodigoErro, nfse.MensagemErro);
        }
        catch (Exception ex)
        {
            nfse.Status = NfseStatus.Rejeitada;

            // Sem DataEmissao a nota some da listagem (o filtro de período
            // é por DataEmissao) e não dá nem pra reenviar pela tela. Aqui
            // não houve dhProc da SEFIN — registra o momento da tentativa,
            // mesmo critério do caminho de rejeição acima (que usa
            // DataHoraProcessamento ?? UtcNow).
            nfse.DataEmissao = DateTimeOffset.UtcNow;

            // Validação local e certificado falham ANTES de qualquer envio
            // (ver NfseNacionalService.EmitirAsync) — é certo que a SEFIN
            // não gerou nota, então ganham CodigoErro e ficam reenviáveis.
            // Qualquer outra coisa (rede, timeout, 5xx) fica SEM CodigoErro:
            // a SEFIN pode ter autorizado sem a resposta chegar.
            switch (ex)
            {
                case NfseValidationException validacao:
                    nfse.CodigoErro = Domain.Entities.Nfse.CodigoErroValidacaoLocal;
                    nfse.MensagemErro = Truncar(validacao.Codigos.Count > 0 ? string.Join(" ", validacao.Codigos) : validacao.Message);
                    _nfseEventoWriter.Registrar(nfse.Id, NfseEventoTipo.Rejeitada, nfse.CodigoErro, nfse.MensagemErro);
                    break;

                case NfseCertificateException:
                    nfse.CodigoErro = Domain.Entities.Nfse.CodigoErroCertificado;
                    nfse.MensagemErro = Truncar(ex.Message);
                    _nfseEventoWriter.Registrar(nfse.Id, NfseEventoTipo.Rejeitada, nfse.CodigoErro, nfse.MensagemErro);
                    break;

                default:
                    nfse.MensagemErro = Truncar(ex.Message);
                    _nfseEventoWriter.Registrar(nfse.Id, NfseEventoTipo.FalhaComunicacao, mensagem: nfse.MensagemErro);
                    break;
            }

            _auditLogWriter.Registrar("EmitirNfse", "Nfse", nfse.Id, new { nfse.Status, nfse.CodigoErro, Erro = ex.Message });

            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Regras do reenvio além de "a rejeição permite" (já checada pelo
    /// chamador): mesma Empresa/Cliente/Contrato da original (Servico,
    /// valor, descrição e competência podem mudar — é justamente o que se
    /// corrige) e a original ainda não foi reenviada antes.
    /// </summary>
    private async Task ValidarReenvioAsync(Domain.Entities.Nfse original, EmitirNfseRequest request, CancellationToken cancellationToken)
    {
        if (original.ClienteId != request.ClienteId || original.ContratoId != request.ContratoId)
            throw new RegraNegocioException("O reenvio precisa manter o mesmo Cliente e Contrato da nota rejeitada.");

        // Checagem amigável; o índice único em ReenvioDeNfseId é o
        // backstop real contra dois reenvios simultâneos.
        var numeroDoReenvio = await _db.NotasFiscais.AsNoTracking()
            .Where(n => n.ReenvioDeNfseId == original.Id)
            .Select(n => (int?)n.NumeroDps)
            .FirstOrDefaultAsync(cancellationToken);

        if (numeroDoReenvio is not null)
            throw new RegraNegocioException($"Esta nota já foi reenviada como DPS nº {numeroDoReenvio}.");
    }

    private static EmitirNfseResult ResultadoDaTentativaExistente(Domain.Entities.Nfse existente)
    {
        // Rejeitada sem CodigoErro = falha de comunicação: não reenvia
        // sozinho (risco de duplicidade) e explica o porquê, em vez de
        // devolver a mesma mensagem técnica antiga sem contexto.
        var mensagem = existente.Status == NfseStatus.Rejeitada && !existente.RejeicaoPermiteReenvio()
            ? $"A tentativa anterior falhou sem resposta da SEFIN ({existente.MensagemErro}). Confira no portal nacional ou use \"Buscar notas da SEFIN\" antes de emitir de novo."
            : existente.MensagemErro;

        return new EmitirNfseResult(
            existente.Id,
            existente.Status == NfseStatus.Autorizada,
            existente.NumeroNfse,
            existente.ChaveAcesso,
            existente.CodigoErro,
            mensagem);
    }

    private static string Truncar(string texto) =>
        texto.Length <= TamanhoMaximoMensagem ? texto : texto[..TamanhoMaximoMensagem];

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
    ///
    /// IMPORTANTE: usa ToListAsync().Single() em vez de SingleAsync().
    /// SingleAsync() tenta COMPOR a consulta (adicionar verificação de
    /// cardinalidade por cima do SQL) para confirmar que só existe uma
    /// linha — mas um INSERT ... RETURNING não é "composable" (não pode
    /// ser embrulhado numa subquery), e o EF Core recusa com
    /// InvalidOperationException ("FromSql/SqlQuery foi chamado com SQL
    /// não-composable e com uma consulta compondo sobre ele"). ToListAsync
    /// só executa e lê as linhas retornadas, sem tentar compor nada.
    /// </summary>
    private async Task<int> ProximoNumeroDpsAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var resultados = await _db.Database.SqlQuery<int>(
            $"""
            INSERT INTO contadores_dps ("EmpresaId", "SerieDps", "UltimoNumero")
            VALUES ({empresaId}, {SerieDps}, 1)
            ON CONFLICT ("EmpresaId", "SerieDps")
            DO UPDATE SET "UltimoNumero" = contadores_dps."UltimoNumero" + 1
            RETURNING "UltimoNumero"
            """).ToListAsync(cancellationToken);

        return resultados.Single();
    }
}
