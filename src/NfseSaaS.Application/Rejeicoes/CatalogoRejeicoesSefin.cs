using System.Collections.Frozen;
using System.Text.Json;

namespace NfseSaaS.Application.Rejeicoes;

/// <summary>Onde o usuário corrige o que causou a rejeição.</summary>
public enum AreaCorrecaoRejeicao
{
    Empresa = 1,
    Tributacao = 2,
    Certificado = 3,
    Cliente = 4,
    Servico = 5,
    Nota = 6,
    Cancelamento = 7,
    ReformaTributaria = 8,
    Suporte = 9
}

/// <summary>
/// Rejeição da SEFIN explicada para quem emite: a mensagem oficial e, em
/// linguagem simples, onde corrigir e o que fazer.
/// </summary>
/// <param name="MensagemOficial">Texto da SEFIN (o recebido na resposta; sem ele, o do catálogo oficial).</param>
/// <param name="OndeCorrigir">Rótulo curto da área (ex.: "Cadastro do cliente").</param>
/// <param name="LinkTela">Tela do sistema onde se corrige, quando houver.</param>
/// <param name="NoCatalogo">false = código fora dos anexos conhecidos (regra nova da SEFIN): só a mensagem recebida é confiável.</param>
public sealed record RejeicaoSefinTraduzida(
    string Codigo,
    string? MensagemOficial,
    AreaCorrecaoRejeicao Area,
    string OndeCorrigir,
    string OQueFazer,
    string? LinkTela,
    bool NoCatalogo);

/// <summary>
/// Catálogo dos códigos de rejeição da NFS-e Nacional, lido das planilhas
/// oficiais (Anexo I – DPS/NFS-e, Anexo II – Eventos, Anexo VI – IBS/CBS) e
/// embutido no assembly (Rejeicoes/rejeicoes-sefin.json, gerado por
/// tools/rejeicoes-sefin/gerar_catalogo.py — nunca editar o JSON à mão).
///
/// A "tradução" não reescreve a regra da SEFIN (a mensagem oficial continua
/// sendo mostrada): diz ONDE corrigir no sistema, pelo caminho XML do campo
/// rejeitado, e o QUE fazer — com texto específico para as rejeições mais
/// comuns (<see cref="Especificas"/>).
/// </summary>
public static class CatalogoRejeicoesSefin
{
    private const string NomeRecurso = "NfseSaaS.Application.Rejeicoes.rejeicoes-sefin.json";

    private sealed record ItemCatalogo(string Codigo, string? Mensagem, string? Campo, string? Caminho, string? Fonte);

    private static readonly Lazy<FrozenDictionary<string, ItemCatalogo>> Itens = new(Carregar);

    /// <summary>Quantidade de códigos no catálogo (para teste e diagnóstico).</summary>
    public static int Quantidade => Itens.Value.Count;

    /// <summary>Todos os códigos do catálogo oficial.</summary>
    public static IEnumerable<string> Codigos => Itens.Value.Keys;

    /// <summary>
    /// Explica um código de erro da SEFIN (ex.: "E0312"). Null quando não é
    /// um código da SEFIN (vazio, ou erro local como "VALIDACAO-LOCAL" e
    /// "CERTIFICADO", que já vêm com mensagem própria do sistema).
    /// </summary>
    public static RejeicaoSefinTraduzida? Traduzir(string? codigo, string? mensagemRecebida)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return null;

        codigo = codigo.Trim().ToUpperInvariant();
        if (codigo.Length < 4 || codigo[0] != 'E' || !codigo.Skip(1).All(char.IsAsciiDigit))
            return null;

        Itens.Value.TryGetValue(codigo, out var item);
        var mensagem = string.IsNullOrWhiteSpace(mensagemRecebida) ? item?.Mensagem : mensagemRecebida;

        if (Especificas.TryGetValue(codigo, out var especifica))
            return Montar(codigo, mensagem, especifica.Area, especifica.OQueFazer, item is not null);

        var area = item is null ? AreaCorrecaoRejeicao.Suporte : ClassificarPorCampo(item.Caminho, item.Campo);
        return Montar(codigo, mensagem, area, OrientacaoPadrao(area, item is not null), item is not null);
    }

    /// <summary>Área a partir do caminho/campo XML da regra (ver Anexo I). Público para teste.</summary>
    public static AreaCorrecaoRejeicao ClassificarPorCampo(string? caminho, string? campo)
    {
        caminho ??= string.Empty;

        if (caminho == "RECEPCAO")
            return AreaCorrecaoRejeicao.Suporte; // certificado de transmissão é tratado em Especificas
        if (caminho.StartsWith("evento/", StringComparison.Ordinal))
            return AreaCorrecaoRejeicao.Cancelamento;
        if (caminho.Contains("/IBSCBS", StringComparison.Ordinal))
            return AreaCorrecaoRejeicao.ReformaTributaria;
        if (caminho.Contains("/infDPS/prest/regTrib", StringComparison.Ordinal))
            return AreaCorrecaoRejeicao.Tributacao;
        if (caminho.Contains("/infDPS/prest", StringComparison.Ordinal))
            return campo is "CNPJ" or "CPF" or "IM" or "email" or "fone"
                ? AreaCorrecaoRejeicao.Empresa
                : AreaCorrecaoRejeicao.Suporte; // nome, endereço e NIF do prestador não vão na DPS quando ele é o emitente
        if (caminho.Contains("/infDPS/toma", StringComparison.Ordinal))
            return AreaCorrecaoRejeicao.Cliente;
        if (caminho.Contains("/infDPS/serv/cServ", StringComparison.Ordinal))
            return AreaCorrecaoRejeicao.Servico;
        if (caminho.Contains("/infDPS/serv/locPrest", StringComparison.Ordinal))
            return AreaCorrecaoRejeicao.Empresa;
        if (caminho.Contains("/valores/trib", StringComparison.Ordinal))
            return campo == "tpRetISSQN" ? AreaCorrecaoRejeicao.Cliente : AreaCorrecaoRejeicao.Tributacao;
        if (caminho.Contains("/infDPS/valores", StringComparison.Ordinal))
            return AreaCorrecaoRejeicao.Nota;
        if (caminho.EndsWith("/infDPS/", StringComparison.Ordinal))
        {
            return campo switch
            {
                "dCompet" => AreaCorrecaoRejeicao.Nota,
                "cLocEmi" => AreaCorrecaoRejeicao.Empresa,
                _ => AreaCorrecaoRejeicao.Suporte
            };
        }

        // Intermediário, obra, evento, comércio exterior, substituição e os
        // grupos que a própria SEFIN gera na NFS-e: não vêm de cadastro.
        return AreaCorrecaoRejeicao.Suporte;
    }

    private static RejeicaoSefinTraduzida Montar(string codigo, string? mensagem, AreaCorrecaoRejeicao area, string oQueFazer, bool noCatalogo)
    {
        var (onde, link) = area switch
        {
            AreaCorrecaoRejeicao.Empresa => ("Cadastro da empresa", "/Empresas"),
            AreaCorrecaoRejeicao.Tributacao => ("Configuração fiscal da empresa", "/Empresas"),
            AreaCorrecaoRejeicao.Certificado => ("Certificado digital", "/Empresas"),
            AreaCorrecaoRejeicao.Cliente => ("Cadastro do cliente", "/Clientes"),
            AreaCorrecaoRejeicao.Servico => ("Cadastro do serviço", "/Servicos"),
            AreaCorrecaoRejeicao.Nota => ("Dados da nota", (string?)null),
            AreaCorrecaoRejeicao.Cancelamento => ("Cancelamento", null),
            AreaCorrecaoRejeicao.ReformaTributaria => ("IBS/CBS (reforma tributária)", null),
            _ => ("Suporte micoempresa", null)
        };

        return new RejeicaoSefinTraduzida(codigo, mensagem, area, onde, oQueFazer, link, noCatalogo);
    }

    private static string OrientacaoPadrao(AreaCorrecaoRejeicao area, bool noCatalogo) => area switch
    {
        AreaCorrecaoRejeicao.Empresa =>
            "Confira os dados da empresa emissora (CNPJ, inscrição municipal, município, e-mail) em Empresas. Se estiverem certos, o cadastro na prefeitura pode estar diferente: confirme com ela ou com o contador.",
        AreaCorrecaoRejeicao.Tributacao =>
            "Confira a configuração fiscal da empresa em Empresas (situação no Simples Nacional, regime de apuração, regime especial, tributação do ISS, alíquota, PIS/COFINS). Na dúvida, confirme com o contador.",
        AreaCorrecaoRejeicao.Certificado =>
            "Há um problema com o certificado digital usado para assinar a nota. Envie um certificado A1 válido (dentro da validade, ICP-Brasil, com o CNPJ da empresa) em Empresas › Certificado.",
        AreaCorrecaoRejeicao.Cliente =>
            "Confira o cadastro do cliente em Clientes: CPF/CNPJ, endereço, CEP, município e se ele retém o ISS.",
        AreaCorrecaoRejeicao.Servico =>
            "Confira o serviço usado na nota em Serviços: código de tributação nacional, NBS e descrição. O código precisa ser aceito pela prefeitura do local onde o ISS é devido.",
        AreaCorrecaoRejeicao.Nota =>
            "Confira o que foi informado na emissão (data de competência, valor, descontos) e emita de novo.",
        AreaCorrecaoRejeicao.Cancelamento =>
            "O pedido de cancelamento foi recusado pela SEFIN. Se o prazo de cancelamento já passou ou a nota não pode mais ser cancelada, fale com a prefeitura.",
        AreaCorrecaoRejeicao.ReformaTributaria =>
            "Regra do novo grupo de IBS/CBS da reforma tributária. Fale com o suporte da micoempresa informando o código.",
        _ when !noCatalogo =>
            "Código ainda não catalogado (regra nova da SEFIN). Leia a mensagem acima; se não ficar claro o que corrigir, fale com o suporte da micoempresa informando o código.",
        _ =>
            "Esse dado é preenchido pelo próprio sistema, não pelo cadastro. Fale com o suporte da micoempresa informando o código."
    };

    /// <summary>
    /// Texto específico para as rejeições mais comuns no dia a dia — o
    /// restante usa a orientação padrão da área (OrientacaoPadrao).
    /// </summary>
    private static readonly FrozenDictionary<string, (AreaCorrecaoRejeicao Area, string OQueFazer)> Especificas =
        new Dictionary<string, (AreaCorrecaoRejeicao, string)>
        {
            // Certificado de transmissão (recepção)
            ["E1200"] = (AreaCorrecaoRejeicao.Certificado, "O certificado digital da empresa não foi aceito. Envie um certificado A1 válido em Empresas › Certificado."),
            ["E1203"] = (AreaCorrecaoRejeicao.Certificado, "O certificado digital da empresa venceu. Renove com a certificadora e envie o novo em Empresas › Certificado."),
            ["E1205"] = (AreaCorrecaoRejeicao.Certificado, "O certificado digital não é de uma certificadora ICP-Brasil reconhecida. Use um certificado A1 ICP-Brasil e envie em Empresas › Certificado."),
            ["E1206"] = (AreaCorrecaoRejeicao.Certificado, "A SEFIN não conseguiu verificar se o certificado foi revogado (falha temporária na lista da certificadora). Tente de novo mais tarde; se persistir, fale com a certificadora."),
            ["E1207"] = (AreaCorrecaoRejeicao.Certificado, "O certificado digital foi revogado pela certificadora. Emita um novo e envie em Empresas › Certificado."),
            ["E1208"] = (AreaCorrecaoRejeicao.Certificado, "O certificado digital não é ICP-Brasil. Use um certificado A1 ICP-Brasil e envie em Empresas › Certificado."),
            ["E1209"] = (AreaCorrecaoRejeicao.Certificado, "O certificado digital não traz CNPJ/CPF. Use um certificado e-CNPJ (A1) da empresa e envie em Empresas › Certificado."),

            // Numeração / duplicidade
            ["E0014"] = (AreaCorrecaoRejeicao.Suporte, "Já existe nota autorizada com este número de DPS. Não reenvie igual: use \"Sincronizar com a SEFIN\" na lista de notas para trazer a nota que já existe. Se continuar, fale com o suporte."),

            // Competência
            ["E0015"] = (AreaCorrecaoRejeicao.Nota, "A data de competência não pode ser depois da data de emissão. Use a competência do mês atual ou de um mês anterior."),
            ["E0016"] = (AreaCorrecaoRejeicao.Nota, "O município da empresa só aceita notas com competência a partir da data em que aderiu à NFS-e Nacional. Use uma competência a partir dessa data."),
            ["E1270"] = (AreaCorrecaoRejeicao.Nota, "O município da empresa só aceita notas com competência a partir da data em que aderiu à NFS-e Nacional. Use uma competência a partir dessa data."),
            ["E0018"] = (AreaCorrecaoRejeicao.Nota, "A competência não pode ser anterior à abertura do CNPJ da empresa. Confira a data de competência."),

            // Município emissor / convênio
            ["E0037"] = (AreaCorrecaoRejeicao.Empresa, "O município da empresa não tem convênio com a NFS-e Nacional. Confira o município em Empresas; se estiver certo, a prefeitura ainda não aderiu."),
            ["E0038"] = (AreaCorrecaoRejeicao.Empresa, "O convênio do município da empresa com a NFS-e Nacional não está ativo. Confirme com a prefeitura."),
            ["E0039"] = (AreaCorrecaoRejeicao.Empresa, "O município da empresa não permite emitir pelos emissores nacionais. Confirme com a prefeitura como emitir."),
            ["E1272"] = (AreaCorrecaoRejeicao.Empresa, "O município da empresa não está ativo na NFS-e Nacional. Confira o município em Empresas e confirme com a prefeitura."),

            // Prestador
            ["E0080"] = (AreaCorrecaoRejeicao.Empresa, "O CNPJ da empresa é inválido. Corrija em Empresas."),
            ["E0082"] = (AreaCorrecaoRejeicao.Empresa, "O CNPJ da empresa não consta na Receita na data de competência. Confira o CNPJ em Empresas e a data de competência."),
            ["E0084"] = (AreaCorrecaoRejeicao.Empresa, "Na Receita, o CNPJ da empresa não tem estabelecimento no município informado. Confira o município em Empresas (deve ser o do CNPJ)."),
            ["E0116"] = (AreaCorrecaoRejeicao.Empresa, "A prefeitura exige a inscrição municipal da empresa. Preencha a inscrição municipal em Empresas."),
            ["E0119"] = (AreaCorrecaoRejeicao.Empresa, "A inscrição municipal da empresa não está autorizada a emitir NFS-e. Confira a inscrição em Empresas e a situação dela na prefeitura."),
            ["E0124"] = (AreaCorrecaoRejeicao.Empresa, "A inscrição municipal da empresa está inativa na prefeitura nessa competência. Regularize na prefeitura ou confira a inscrição em Empresas."),
            ["E0160"] = (AreaCorrecaoRejeicao.Tributacao, "A situação no Simples Nacional informada não bate com o cadastro do Simples nessa competência. Corrija a situação no Simples Nacional em Empresas."),
            ["E0162"] = (AreaCorrecaoRejeicao.Tributacao, "Empresa não optante do Simples (ou MEI) não informa regime de apuração do Simples. Ajuste em Empresas."),
            ["E0166"] = (AreaCorrecaoRejeicao.Tributacao, "Para empresa do Simples Nacional (ME/EPP), o regime de apuração dos tributos é obrigatório. Preencha em Empresas."),

            // Tomador (cliente)
            ["E0188"] = (AreaCorrecaoRejeicao.Cliente, "O CNPJ do cliente é inválido. Corrija em Clientes."),
            ["E0190"] = (AreaCorrecaoRejeicao.Cliente, "O CNPJ do cliente não consta na Receita. Confira o CNPJ em Clientes."),
            ["E0202"] = (AreaCorrecaoRejeicao.Cliente, "O cliente da nota é a própria empresa emissora. Escolha outro cliente."),
            ["E0206"] = (AreaCorrecaoRejeicao.Cliente, "O CPF do cliente é inválido. Corrija em Clientes."),
            ["E0207"] = (AreaCorrecaoRejeicao.Cliente, "O CPF do cliente não consta na Receita. Confira o CPF em Clientes."),
            ["E0238"] = (AreaCorrecaoRejeicao.Cliente, "O município do endereço do cliente não existe na tabela do IBGE. Corrija o município (código IBGE) em Clientes."),
            ["E0240"] = (AreaCorrecaoRejeicao.Cliente, "O CEP do cliente não existe ou não é do município informado. Corrija o CEP (ou o município) em Clientes."),
            ["E0247"] = (AreaCorrecaoRejeicao.Cliente, "O e-mail do cliente é inválido. Corrija ou apague o e-mail em Clientes."),

            // Serviço
            ["E0310"] = (AreaCorrecaoRejeicao.Servico, "O código de tributação nacional do serviço não existe na lista nacional. Corrija o código em Serviços."),
            ["E0312"] = (AreaCorrecaoRejeicao.Servico, "A prefeitura onde o ISS é devido não aceita este código de tributação nacional. Confira o código do serviço em Serviços, de preferência com o contador."),
            ["E0316"] = (AreaCorrecaoRejeicao.Servico, "O código NBS do serviço não existe. Corrija o NBS em Serviços."),

            // ISS / retenção / alíquota
            ["E0595"] = (AreaCorrecaoRejeicao.Tributacao, "A alíquota de ISS não pode passar de 5%. Corrija a alíquota em Empresas."),
            ["E0600"] = (AreaCorrecaoRejeicao.Tributacao, "MEI não informa alíquota de ISS. Deixe a alíquota em branco em Empresas."),
            ["E0602"] = (AreaCorrecaoRejeicao.Tributacao, "Sem ISS a pagar (imunidade, exportação ou não incidência) não se informa alíquota. Ajuste a tributação do ISS ou a alíquota em Empresas."),
            ["E0604"] = (AreaCorrecaoRejeicao.Tributacao, "Empresa com regime especial de tributação não informa alíquota de ISS. Ajuste o regime especial ou a alíquota em Empresas."),
            ["E0580"] = (AreaCorrecaoRejeicao.Cliente, "Não há retenção de ISS quando a nota é de imunidade, exportação ou não incidência. Desmarque \"Retém ISS\" no cliente (ou na emissão)."),
            ["E0583"] = (AreaCorrecaoRejeicao.Cliente, "Empresa MEI não pode ter ISS retido. Desmarque \"Retém ISS\" no cliente (ou na emissão)."),
            ["E0588"] = (AreaCorrecaoRejeicao.Cliente, "Empresa com regime especial de tributação não pode ter ISS retido. Desmarque \"Retém ISS\" no cliente (ou na emissão)."),

            // Prazo
            ["E1294"] = (AreaCorrecaoRejeicao.Nota, "Passou o prazo para enviar esta nota à SEFIN (data de emissão antiga). Emita uma nota nova com a data de hoje.")
        }.ToFrozenDictionary();

    private static FrozenDictionary<string, ItemCatalogo> Carregar()
    {
        using var recurso = typeof(CatalogoRejeicoesSefin).Assembly.GetManifestResourceStream(NomeRecurso)
            ?? throw new InvalidOperationException($"Recurso {NomeRecurso} não encontrado no assembly.");

        var itens = JsonSerializer.Deserialize<List<ItemCatalogo>>(recurso, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new List<ItemCatalogo>();

        return itens.ToFrozenDictionary(i => i.Codigo, StringComparer.OrdinalIgnoreCase);
    }
}
