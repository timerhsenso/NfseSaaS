using System.Globalization;
using System.Xml;
using Microsoft.Extensions.Options;
using NfseSaaS.Nacional.Helpers;
using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Options;

namespace NfseSaaS.Nacional.Builders;

/// <summary>
/// Monta o XML (ainda NÃO assinado) da DPS. Migrado de
/// NfsePoc/DpsBuilder.cs (método Gerar) — mesmo layout, namespace do SPED,
/// faixa de série por tpEmit e margem de segurança no dhEmi já validados
/// numa emissão real aceita pela SEFIN (HTTP 201).
///
/// Diferença central em relação à POC: TODOS os dados de prestador,
/// tomador e tributação agora vêm de <see cref="DpsRequest"/> — a POC
/// tinha um único prestador fixo no código, o que não faz sentido num SaaS
/// multiempresa.
///
/// O tpAmb (request.TpAmb) TAMBÉM vem de fora agora — este builder não
/// lê mais NfseNacionalOptions/appsettings pra decidir isso. Antes era
/// um valor global (mesmo ambiente fiscal pra toda empresa do sistema);
/// hoje é decidido por Empresa (Empresa.TipoAmbiente), resolvido em
/// EmitirNfseUseCase antes de chegar aqui.
///
/// A ÚNICA coisa que este builder ainda lê de NfseNacionalOptions é o
/// leiaute (<see cref="LeiauteDps"/>) — e mesmo assim resolvido a partir
/// do tpAmb do request, nunca um valor global. Motivo: durante a transição
/// da NT 009, Homologação e Produção exigem XMLs incompatíveis; isso é
/// propriedade do AMBIENTE da SEFIN, não da Empresa.
/// </summary>
public sealed class DpsBuilder : IDpsBuilder
{
    // Finalidade da NFS-e (NT 009, item 2.2): 0 = NFS-e regular. O XSD de
    // Produção Restrita de 20/10/2026 (TSRTCFinNFSe) só aceita "0" — notas
    // de ajuste de crédito/débito (1/2) ainda não foram liberadas.
    private const string FinNfseRegular = "0";

    // opSimpNac = 3: Optante pelo Simples Nacional ME/EPP (exceto MEI).
    private const string OpSimpNacMeEpp = "3";

    // indTotTrib = 0: "Não informar nenhum valor estimado para os Tributos"
    // (Decreto 8.264/2014) — único valor aceito pelo TSTipoIndTotTrib.
    private const string IndTotTribNaoInformar = "0";

    // indDest = 0: o destinatário do serviço é o próprio tomador (TSRTCIndDest).
    private const string IndDestTomador = "0";

    private readonly NfseNacionalOptions _options;

    public DpsBuilder(IOptions<NfseNacionalOptions> options)
    {
        _options = options.Value;
    }

    private const string Ns = "http://www.sped.fazenda.gov.br/nfse";

    // tpEmit=1 (emissão com aplicativo próprio) exige série na faixa
    // 00001-49999 — ver manual da NFS-e Nacional. A validação dessa faixa
    // é responsabilidade de IDpsValidator, não deste builder.
    private const string TpEmit = "1";

    // 2 = CNPJ. Fixo porque Empresa, no Domain deste SaaS, sempre se
    // identifica por CNPJ (não há emissão por CPF neste sistema).
    private const string TpInscPrestadorCnpj = "2";

    // Margem de segurança subtraída do dhEmi para absorver latência de rede
    // e o desvio residual de relógio entre este servidor e o da SEFIN —
    // comprovadamente necessária na POC para evitar a rejeição E0008
    // ("A data de emissão da DPS não pode ser posterior à data do seu
    // processamento").
    private static readonly TimeSpan MargemSegurancaDhEmi = TimeSpan.FromSeconds(5);

    public (string XmlDps, string InfDpsId) Construir(DpsRequest request)
    {
        if (request.TpAmb is not ("1" or "2"))
            throw new ArgumentException($"TpAmb inválido: '{request.TpAmb}'. Esperado \"1\" (Produção) ou \"2\" (Homologação).", nameof(request));

        // DateTimeOffset.Now usa o fuso do SERVIDOR, não o de Brasília — em
        // produção (container Linux/Docker) o fuso do SO é UTC, então
        // .Now vem com offset "+00:00". O dhEmi acaba correto em termos de
        // INSTANTE (mesmo ponto no tempo), mas a SEFIN rejeitou com E0008
        // ("dhEmi posterior ao processamento") mesmo assim — sinal de que
        // ela não normaliza o offset recebido antes de comparar, e lê os
        // dígitos do relógio como se já fossem hora de Brasília. Resultado:
        // um dhEmi em UTC aparenta estar ~3h no "futuro" da perspectiva
        // dela. Construir explicitamente em -03:00 (fixo, sem horário de
        // verão desde 2019 — mesma premissa já usada em
        // ListarNfseUseCase.offsetBrasilia) elimina essa ambiguidade
        // independente do fuso do servidor onde isto roda.
        var agora = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)) - MargemSegurancaDhEmi;

        var leiaute = _options.ObterLeiauteDps(request.TpAmb);

        var infDpsId =
            "DPS" +
            request.Prestador.CodigoMunicipio +
            TpInscPrestadorCnpj +
            request.Prestador.Cnpj +
            request.SerieDps +
            request.NumeroDps.ToString("D15");

        var doc = new XmlDocument { PreserveWhitespace = true };

        var dps = doc.CreateElement("DPS", Ns);
        dps.SetAttribute("versao", "1.01");
        doc.AppendChild(dps);

        var infDps = doc.CreateElement("infDPS", Ns);
        infDps.SetAttribute("Id", infDpsId);
        dps.AppendChild(infDps);

        Add(doc, infDps, "tpAmb", request.TpAmb);
        Add(doc, infDps, "dhEmi", agora.ToString("yyyy-MM-ddTHH:mm:sszzz"));
        Add(doc, infDps, "verAplic", "NfseSaaS_1.0");
        Add(doc, infDps, "serie", request.SerieDps);
        Add(doc, infDps, "nDPS", request.NumeroDps.ToString(CultureInfo.InvariantCulture));
        Add(doc, infDps, "dCompet", request.DataCompetencia.ToString("yyyy-MM-dd"));
        Add(doc, infDps, "tpEmit", TpEmit);

        // NT 009: finNFSe é obrigatório e fica entre tpEmit (/cMotivoEmisTI,
        // que não usamos) e cLocEmi. No leiaute NT 004 o elemento não existe
        // e quebraria o schema de Produção.
        if (leiaute == LeiauteDps.Nt009)
            Add(doc, infDps, "finNFSe", FinNfseRegular);

        Add(doc, infDps, "cLocEmi", request.Prestador.CodigoMunicipio);

        // Substituição (C7): entre cLocEmi e prest, como manda o XSD.
        if (request.Substituicao is { } subst)
        {
            var grupo = AddNode(doc, infDps, "subst");
            Add(doc, grupo, "chSubstda", subst.ChaveSubstituida);
            Add(doc, grupo, "cMotivo", subst.CodigoMotivo);
            if (!string.IsNullOrWhiteSpace(subst.DescricaoMotivo))
                Add(doc, grupo, "xMotivo", subst.DescricaoMotivo.Trim());
        }

        var prest = AddNode(doc, infDps, "prest");
        Add(doc, prest, "CNPJ", request.Prestador.Cnpj);

        // IM, fone e email são opcionais no XSD (minOccurs=0). Vazios, NÃO
        // podem virar elemento vazio — <IM></IM> quebra o Pattern do tipo
        // (E1235). Mesmo motivo do pTotTribSN/regApTribSN abaixo.
        AddOpcional(doc, prest, "IM", request.Prestador.InscricaoMunicipal);
        // TSTelefone é [0-9]{6,20}: "(71) 3333-4444" digitado com máscara
        // no cadastro quebrava TODA nota da Empresa (E1235). Vai só dígito.
        AddOpcional(doc, prest, "fone", new string((request.Prestador.Telefone ?? string.Empty).Where(char.IsDigit).ToArray()));
        AddOpcional(doc, prest, "email", request.Prestador.Email);

        var regTrib = AddNode(doc, prest, "regTrib");
        Add(doc, regTrib, "opSimpNac", request.Prestador.OpSimpNac);

        // regApTribSN só existe pra optante ME/EPP (opSimpNac = 3) — texto
        // do próprio XSD (TSRegimeApuracaoSimpNac). Pra Não optante e MEI
        // o elemento não vai, mesmo que o cadastro da Empresa tenha valor.
        if (request.Prestador.OpSimpNac == OpSimpNacMeEpp)
            AddOpcional(doc, regTrib, "regApTribSN", request.Prestador.RegApTribSN);

        Add(doc, regTrib, "regEspTrib", request.Prestador.RegEspTrib);

        // Tomador: CPF (11) ou CNPJ (14, numérico ou alfanumérico) — o
        // elemento muda conforme o documento. Antes era sempre <CNPJ>, e
        // tomador pessoa física quebrava o Pattern [0-9A-Z]{14} (E1235).
        var toma = AddNode(doc, infDps, "toma");
        var documentoTomador = NormalizarDocumento(request.Tomador.CnpjOuCpf);
        Add(doc, toma, documentoTomador.Length == 11 ? "CPF" : "CNPJ", documentoTomador);
        Add(doc, toma, "xNome", request.Tomador.Nome);

        // Endereço opcional (C3): sem endereço, o grupo end não vai (o
        // validador já barrou CNPJ sem endereço). Ordem do TCInfoPessoa:
        // xNome, end?, fone?, email?; do TCEndereco: endNac, xLgr, nro, xCpl?, xBairro.
        if (!request.Tomador.SemEndereco)
        {
            var end = AddNode(doc, toma, "end");
            var endNac = AddNode(doc, end, "endNac");
            Add(doc, endNac, "cMun", request.Tomador.CodigoMunicipio);
            Add(doc, endNac, "CEP", request.Tomador.Cep);
            Add(doc, end, "xLgr", request.Tomador.Logradouro);
            Add(doc, end, "nro", request.Tomador.Numero);
            AddOpcional(doc, end, "xCpl", Limitar(request.Tomador.Complemento, 156));
            Add(doc, end, "xBairro", request.Tomador.Bairro);
        }

        // fone: TSTelefone [0-9]{6,20} — só dígitos; fora disso não vai
        // (um telefone mal digitado no cadastro não pode rejeitar a nota).
        var foneTomador = new string((request.Tomador.Telefone ?? string.Empty).Where(char.IsDigit).ToArray());
        if (foneTomador.Length is >= 6 and <= 20)
            Add(doc, toma, "fone", foneTomador);
        // email: TSEmail até 80 caracteres; maior não vai.
        var emailTomador = request.Tomador.Email?.Trim();
        if (!string.IsNullOrEmpty(emailTomador) && emailTomador.Length <= 80)
            Add(doc, toma, "email", emailTomador);

        var serv = AddNode(doc, infDps, "serv");
        var locPrest = AddNode(doc, serv, "locPrest");
        // C2: local da prestação escolhido na nota/contrato; sem escolha, o do prestador.
        Add(doc, locPrest, "cLocPrestacao", string.IsNullOrWhiteSpace(request.CodigoMunicipioPrestacao)
            ? request.Prestador.CodigoMunicipio
            : request.CodigoMunicipioPrestacao);

        var cServ = AddNode(doc, serv, "cServ");
        Add(doc, cServ, "cTribNac", request.CodigoTributacaoNacional);

        // Ordem do cServ mudou na NT 009: xDescServ passou a ser o ÚLTIMO
        // elemento (cTribNac, cTribMun?, cNBS?, cIntContrib?, xDescServ).
        // Na NT 004 continua cTribNac → xDescServ → cNBS.
        if (leiaute == LeiauteDps.Nt009)
        {
            Add(doc, cServ, "cNBS", request.CodigoNbs);
            Add(doc, cServ, "xDescServ", request.DescricaoServico);
        }
        else
        {
            Add(doc, cServ, "xDescServ", request.DescricaoServico);
            Add(doc, cServ, "cNBS", request.CodigoNbs);
        }

        var valores = AddNode(doc, infDps, "valores");
        var vServPrest = AddNode(doc, valores, "vServPrest");
        Add(doc, vServPrest, "vServ", request.Valor.ToString("0.00", CultureInfo.InvariantCulture));

        var trib = AddNode(doc, valores, "trib");
        var tribMun = AddNode(doc, trib, "tribMun");
        Add(doc, tribMun, "tribISSQN", request.Tributacao.TribIssqn);
        Add(doc, tribMun, "tpRetISSQN", request.Tributacao.TpRetIssqn);

        // pAliq vem DEPOIS de tpRetISSQN no TCTribMunicipal. Só vai quando
        // informado — obrigatório com ISS retido (o tomador precisa saber
        // quanto reter); vazio, o elemento fica de fora.
        AddOpcional(doc, tribMun, "pAliq", request.Tributacao.AliquotaIss);

        var tribFed = AddNode(doc, trib, "tribFed");
        var piscofins = AddNode(doc, tribFed, "piscofins");
        Add(doc, piscofins, "CST", request.Tributacao.CstPisCofins);
        Add(doc, piscofins, "tpRetPisCofins", request.Tributacao.TpRetPisCofins);

        // <totTrib> é OBRIGATÓRIO no XSD (TCInfoTributacao), com um choice
        // de exatamente 1 filho: vTotTrib | pTotTrib | indTotTrib | pTotTribSN.
        // Antes o grupo era omitido pra Não optante — DPS inválida (E1235).
        //   - Optante (MEI "2" ou ME/EPP "3"): pTotTribSN, já exigido pelo
        //     DpsValidator pra esses regimes.
        //   - Não optante ("1"): indTotTrib=0 ("não informar valor estimado
        //     dos tributos", Decreto 8.264/2014) — único valor do tipo
        //     TSTipoIndTotTrib. O SaaS não calcula tributo aproximado (Lei
        //     12.741) pra Empresa fora do Simples; se isso virar demanda,
        //     o caminho é pTotTrib/vTotTrib, não este.
        var totTrib = AddNode(doc, trib, "totTrib");
        if (request.Prestador.OpSimpNac is "2" or OpSimpNacMeEpp)
            Add(doc, totTrib, "pTotTribSN", request.Tributacao.PercentualTotalTributosSimplesNacional);
        else
            Add(doc, totTrib, "indTotTrib", IndTotTribNaoInformar);

        // IBSCBS: último filho do infDPS (depois de valores). O grupo só
        // existe no leiaute NT 009; no NT 004 quebraria o schema de
        // Produção. Ordem do TCRTCInfoIBSCBS: indFinal?, cIndOp?, ...,
        // indDest, ..., valores. indFinal (uso ou consumo pessoal, art. 57)
        // não vai: é opcional e o prestador não tem como saber.
        if (leiaute == LeiauteDps.Nt009 && request.IbsCbs is { } ibsCbs)
        {
            var grupo = AddNode(doc, infDps, "IBSCBS");
            // Com tributação a declarar, cIndOp é obrigatório (E0860); sem, proibido (E0859).
            if (ibsCbs.ComTributacao)
                Add(doc, grupo, "cIndOp", ibsCbs.CIndOp ?? throw new InvalidOperationException("cIndOp é obrigatório quando há tributação de IBS/CBS (E0860)."));
            // 0: o destinatário é o próprio tomador. O grupo dest não é enviado (E0908).
            Add(doc, grupo, "indDest", IndDestTomador);
            var valoresIbsCbs = AddNode(doc, grupo, "valores");
            var tribIbsCbs = AddNode(doc, valoresIbsCbs, "trib");
            Add(doc, tribIbsCbs, "CST", ibsCbs.Cst);
            Add(doc, tribIbsCbs, "cClassTrib", ibsCbs.CClassTrib);
            // Com tributação, o subgrupo é obrigatório (E0960) mesmo vazio:
            // crédito presumido, tributação regular e diferimento (filhos
            // opcionais) ainda não são usados.
            if (ibsCbs.ComTributacao)
                AddNode(doc, tribIbsCbs, "gIBSCBS");
        }

        return (XmlSerializationHelper.ToXmlString(doc), infDpsId);
    }

    /// <summary>
    /// Remove só a máscara (ponto, barra, hífen, espaço) e põe em maiúsculo —
    /// NÃO usa "somente dígitos", porque o CNPJ alfanumérico (NT 009, item
    /// 2.1) tem letras que precisam chegar intactas na SEFIN.
    /// </summary>
    private static string NormalizarDocumento(string documento) =>
        new(documento.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>Elemento opcional (minOccurs=0): valor vazio = elemento ausente, nunca elemento vazio.</summary>
    private static string? Limitar(string? valor, int maximo)
    {
        var texto = valor?.Trim();
        return string.IsNullOrEmpty(texto) || texto.Length <= maximo ? texto : texto[..maximo].TrimEnd();
    }

    private static void AddOpcional(XmlDocument doc, XmlElement parent, string nome, string? valor)
    {
        if (!string.IsNullOrWhiteSpace(valor))
            Add(doc, parent, nome, valor);
    }

    private static XmlElement AddNode(XmlDocument doc, XmlElement parent, string nome)
    {
        var element = doc.CreateElement(nome, Ns);
        parent.AppendChild(element);
        return element;
    }

    private static void Add(XmlDocument doc, XmlElement parent, string nome, string valor)
    {
        var element = doc.CreateElement(nome, Ns);
        // Mesmo motivo do Add() em EventoCancelamentoBuilder: espaço
        // sobrando no início/fim quebra o Pattern de vários tipos do
        // schema da SEFIN — aparado aqui, uma vez, pra qualquer campo de
        // texto livre da DPS (descrição do serviço, nomes, endereço).
        element.InnerText = valor.Trim();
        parent.AppendChild(element);
    }
}