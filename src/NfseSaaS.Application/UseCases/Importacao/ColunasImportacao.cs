using System.Text;
using NfseSaaS.Application.UseCases.Municipios;

namespace NfseSaaS.Application.UseCases.Importacao;

/// <summary>IM1: o que cada aba da planilha traz.</summary>
public enum TipoAbaImportacao
{
    Ignorada = 0,
    Clientes = 1,
    Contratos = 2
}

public enum CampoCliente
{
    CpfCnpj,
    Nome,
    Apelido,
    Email,
    Telefone,
    Cep,
    Logradouro,
    Numero,
    Complemento,
    Bairro,
    Municipio,
    Uf,
    CodigoMunicipio,
    RetemIss,
    EnviarNotaPorEmail,
    EmailsCopia,
    DisponivelPara,
    Irrf,
    Pis,
    Cofins,
    Csll,
    Inss
}

public enum CampoContrato
{
    Numero,
    CpfCnpjCliente,
    Nome,
    Servico,
    Quantidade,
    ValorUnitario,
    ValorVariavel,
    Cobranca,
    Inicio,
    Fim,
    Indice,
    Periodicidade,
    UltimoReajuste,
    Situacao,
    Observacao
}

/// <param name="Titulo">Cabeçalho no modelo para baixar.</param>
/// <param name="Sinonimos">Outros nomes de cabeçalho reconhecidos (planilha de outro sistema). Comparados sem acento, maiúscula, espaço ou pontuação.</param>
/// <param name="Lista">Nome da lista suspensa no modelo (aba "Listas"); nulo = digitação livre.</param>
public sealed record ColunaImportacao<TCampo>(
    TCampo Campo,
    string Titulo,
    bool Obrigatoria,
    string Ajuda,
    string Exemplo,
    double Largura,
    FormatoColuna Formato = FormatoColuna.Texto,
    string? Lista = null,
    params string[] Sinonimos) where TCampo : struct, Enum;

public enum FormatoColuna
{
    Texto,
    Numero,
    Valor,
    Percentual,
    Data
}

/// <summary>
/// IM1: catálogo das colunas de cada aba. A mesma fonte monta o modelo para
/// baixar (títulos, ajuda, exemplos, listas) e reconhece o cabeçalho da
/// planilha que voltou, inclusive a exportada por outro sistema.
/// </summary>
public static class ColunasImportacao
{
    public const string ListaSimNao = "SimNao";
    public const string ListaDisponivelPara = "DisponivelPara";
    public const string ListaServicos = "Servicos";
    public const string ListaCobranca = "Cobranca";
    public const string ListaIndice = "Indice";
    public const string ListaSituacao = "Situacao";

    public const string NomeAbaClientes = "Clientes";
    public const string NomeAbaContratos = "Contratos";
    public const string NomeAbaInstrucoes = "Instruções";
    public const string NomeAbaListas = "Listas";

    public static readonly IReadOnlyList<ColunaImportacao<CampoCliente>> Clientes = new ColunaImportacao<CampoCliente>[]
    {
        new(CampoCliente.CpfCnpj, "CPF/CNPJ", true, "Só números ou com pontos e traços. É a chave do cliente: quem já está cadastrado não é duplicado.", "12.345.678/0001-95", 20,
            Sinonimos: ["cpf/cnpj", "cnpj/cpf", "cnpj", "cpf", "documento", "cpf ou cnpj", "cnpj ou cpf", "cpf cnpj", "doc", "inscricao federal", "cnpj do cliente", "cpf do cliente", "cpf/cnpj do tomador", "cnpj do tomador", "tomador cpf/cnpj"]),
        new(CampoCliente.Nome, "Nome / razão social", true, "Como sai na nota. Com CNPJ, se ficar vazio, buscamos na Receita.", "Exemplo Comércio Ltda", 34,
            Sinonimos: ["nome", "razao social", "nome/razao social", "razao social/nome", "nome completo", "cliente", "nome do cliente", "tomador", "nome do tomador", "razao social do tomador", "nome empresarial"]),
        new(CampoCliente.Apelido, "Apelido", false, "Nome curto que aparece nas telas. Vazio: usamos o nome fantasia ou o nome.", "Exemplo", 18,
            Sinonimos: ["apelido", "nome fantasia", "fantasia", "nome curto", "nome de exibicao"]),
        new(CampoCliente.Email, "E-mail", false, "E-mail que recebe a nota.", "financeiro@exemplo.com.br", 28,
            Sinonimos: ["email", "e-mail", "e mail", "email principal", "email do cliente", "email do tomador", "email para envio da nota", "email financeiro"]),
        new(CampoCliente.Telefone, "Telefone", false, "Com DDD.", "(71) 99999-0000", 16,
            Sinonimos: ["telefone", "fone", "celular", "whatsapp", "tel", "telefone/celular"]),
        new(CampoCliente.Cep, "CEP", false, "8 números. O endereço é obrigatório para CNPJ (vazio: buscamos na Receita).", "40010-000", 11,
            Sinonimos: ["cep", "codigo postal"]),
        new(CampoCliente.Logradouro, "Logradouro", false, "Rua, avenida…", "Rua Exemplo", 28,
            Sinonimos: ["logradouro", "endereco", "rua", "endereco completo", "rua/avenida"]),
        new(CampoCliente.Numero, "Número", false, "Número do endereço (S/N se não tiver).", "100", 9,
            Sinonimos: ["numero", "nro", "n", "num", "numero do endereco", "no"]),
        new(CampoCliente.Complemento, "Complemento", false, "Sala, andar, bloco…", "Sala 2", 16,
            Sinonimos: ["complemento", "compl"]),
        new(CampoCliente.Bairro, "Bairro", false, "", "Centro", 18,
            Sinonimos: ["bairro", "distrito"]),
        new(CampoCliente.Municipio, "Município", false, "Nome da cidade. Ou use a coluna do código IBGE.", "Salvador", 20,
            Sinonimos: ["municipio", "cidade", "nome do municipio", "localidade"]),
        new(CampoCliente.Uf, "UF", false, "2 letras.", "BA", 6,
            Sinonimos: ["uf", "estado", "sigla uf"]),
        new(CampoCliente.CodigoMunicipio, "Código IBGE do município", false, "7 números. Se preencher, não precisa do nome do município.", "2927408", 14,
            Sinonimos: ["codigo ibge", "ibge", "codigo do municipio", "codigo municipio", "cod municipio", "cod ibge", "municipio ibge", "codigo ibge do municipio", "cmun"]),
        new(CampoCliente.RetemIss, "Retém ISS", false, "Sim quando o cliente retém o ISS na fonte. Vazio = Não.", "Não", 10, Lista: ListaSimNao,
            Sinonimos: ["retem iss", "iss retido", "retencao iss", "reter iss", "retem o iss"]),
        new(CampoCliente.EnviarNotaPorEmail, "Enviar nota por e-mail", false, "Vazio = Sim quando tem e-mail.", "Sim", 12, Lista: ListaSimNao,
            Sinonimos: ["enviar nota por email", "enviar por email", "envia email", "enviar email", "envio automatico"]),
        new(CampoCliente.EmailsCopia, "E-mails em cópia", false, "Até 5, separados por ponto e vírgula.", "contabil@exemplo.com.br", 28,
            Sinonimos: ["emails em copia", "copia", "copias", "cc", "emails copia", "email copia", "outros emails", "emails adicionais"]),
        new(CampoCliente.DisponivelPara, "Disponível para", false, "Com mais de uma empresa: \"Esta empresa\" ou \"Todas as empresas\". Vazio = o padrão da conta.", "Esta empresa", 18, Lista: ListaDisponivelPara,
            Sinonimos: ["disponivel para", "compartilhado", "empresas"]),
        new(CampoCliente.Irrf, "IRRF retido %", false, "Só se o cliente retém. Ex.: 1,5", "", 10, FormatoColuna.Percentual,
            Sinonimos: ["irrf", "irrf %", "irrf retido", "aliquota irrf", "ir retido", "irrf retido %"]),
        new(CampoCliente.Pis, "PIS retido %", false, "Ex.: 0,65", "", 10, FormatoColuna.Percentual,
            Sinonimos: ["pis", "pis %", "pis retido", "aliquota pis", "pis retido %"]),
        new(CampoCliente.Cofins, "COFINS retido %", false, "Ex.: 3", "", 10, FormatoColuna.Percentual,
            Sinonimos: ["cofins", "cofins %", "cofins retido", "aliquota cofins", "cofins retido %"]),
        new(CampoCliente.Csll, "CSLL retido %", false, "Ex.: 1", "", 10, FormatoColuna.Percentual,
            Sinonimos: ["csll", "csll %", "csll retido", "aliquota csll", "csll retido %"]),
        new(CampoCliente.Inss, "INSS retido %", false, "Ex.: 11", "", 10, FormatoColuna.Percentual,
            Sinonimos: ["inss", "inss %", "inss retido", "aliquota inss", "inss retido %"])
    };

    public static readonly IReadOnlyList<ColunaImportacao<CampoContrato>> Contratos = new ColunaImportacao<CampoContrato>[]
    {
        new(CampoContrato.Numero, "Nº do contrato", false, "Vazio: o sistema numera (ano/0001). Para um contrato com vários serviços, repita o número em várias linhas.", "2026/0001", 14,
            Sinonimos: ["numero", "numero do contrato", "n do contrato", "no do contrato", "contrato", "n contrato", "cod contrato", "codigo do contrato", "nro contrato"]),
        new(CampoContrato.CpfCnpjCliente, "CPF/CNPJ do cliente", true, "O cliente precisa estar cadastrado ou na aba Clientes desta planilha.", "12.345.678/0001-95", 20,
            Sinonimos: ["cpf/cnpj", "cnpj/cpf", "cnpj", "cpf", "documento", "cpf/cnpj do cliente", "cnpj do cliente", "cpf do cliente", "documento do cliente", "cliente cpf/cnpj", "cnpj do tomador", "cpf/cnpj do tomador"]),
        new(CampoContrato.Nome, "Nome do contrato", false, "Opcional. Ex.: Manutenção mensal.", "Manutenção mensal", 24,
            Sinonimos: ["nome do contrato", "descricao", "descricao do contrato", "nome", "titulo", "objeto"]),
        new(CampoContrato.Servico, "Serviço", true, "Descrição de um serviço já cadastrado em Serviços (escolha na lista).", "", 30, Lista: ListaServicos,
            Sinonimos: ["servico", "servicos", "descricao do servico", "servico prestado", "item", "produto/servico"]),
        new(CampoContrato.Quantidade, "Quantidade", false, "Vazio = 1.", "1", 10, FormatoColuna.Numero,
            Sinonimos: ["quantidade", "qtd", "qtde", "quant"]),
        new(CampoContrato.ValorUnitario, "Valor unitário", true, "Valor de cada nota (mensalidade).", "1.500,00", 14, FormatoColuna.Valor,
            Sinonimos: ["valor unitario", "valor", "valor mensal", "mensalidade", "valor do contrato", "valor do servico", "preco", "valor r$", "valor (r$)"]),
        new(CampoContrato.ValorVariavel, "Valor muda todo mês", false, "Sim quando o valor muda a cada nota. Vazio = Não.", "Não", 12, Lista: ListaSimNao,
            Sinonimos: ["valor variavel", "valor muda todo mes", "variavel"]),
        new(CampoContrato.Cobranca, "Cobrança", false, "Mensal ou Avulso. Vazio = Mensal.", "Mensal", 11, Lista: ListaCobranca,
            Sinonimos: ["cobranca", "tipo de cobranca", "tipo", "periodicidade de cobranca", "recorrencia"]),
        new(CampoContrato.Inicio, "Início", true, "Data de início do contrato (dd/mm/aaaa).", "01/03/2025", 12, FormatoColuna.Data,
            Sinonimos: ["inicio", "data de inicio", "data inicio", "inicio do contrato", "data do contrato", "vigencia inicial", "inicio da vigencia", "data de assinatura"]),
        new(CampoContrato.Fim, "Fim", false, "Vazio = sem prazo.", "", 12, FormatoColuna.Data,
            Sinonimos: ["fim", "data de fim", "data fim", "termino", "data de termino", "fim do contrato", "vigencia final", "fim da vigencia", "vencimento do contrato"]),
        new(CampoContrato.Indice, "Índice de reajuste", false, "IPCA, IGP-M, INCC, INPC, Selic, CDI ou Outro. Vazio = IPCA (no Mensal).", "IPCA", 12, Lista: ListaIndice,
            Sinonimos: ["indice", "indice de reajuste", "reajuste", "indice reajuste"]),
        new(CampoContrato.Periodicidade, "Reajuste a cada (meses)", false, "Vazio = 12.", "12", 12, FormatoColuna.Numero,
            Sinonimos: ["periodicidade", "reajuste a cada", "periodicidade do reajuste", "meses para reajuste", "periodicidade de reajuste"]),
        new(CampoContrato.UltimoReajuste, "Último reajuste", false, "Data do último reajuste já aplicado no valor. Vazio: consideramos o valor atualizado até o último aniversário.", "01/03/2026", 13, FormatoColuna.Data,
            Sinonimos: ["ultimo reajuste", "data do ultimo reajuste", "data ultimo reajuste", "reajustado em"]),
        new(CampoContrato.Situacao, "Situação", false, "Ativo, Suspenso, Encerrado, Cancelado ou Rascunho. Vazio = Ativo.", "Ativo", 11, Lista: ListaSituacao,
            Sinonimos: ["situacao", "status", "situacao do contrato"]),
        new(CampoContrato.Observacao, "Observação", false, "", "", 30,
            Sinonimos: ["observacao", "observacoes", "obs", "anotacoes"])
    };

    /// <summary>"E-mail em cópia (até 5) *" → "emailemcopiaate5". Sem acento, maiúscula, espaço ou pontuação.</summary>
    public static string ChaveCabecalho(string? cabecalho)
    {
        var semAcento = RegrasMunicipio.NormalizarBusca(cabecalho);
        var sb = new StringBuilder(semAcento.Length);
        foreach (var c in semAcento)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Qual campo cada coluna da planilha preenche, as colunas que não reconhecemos e as obrigatórias que faltam.</summary>
    public static MapaColunas<TCampo> Mapear<TCampo>(IReadOnlyList<string> cabecalhos, IReadOnlyList<ColunaImportacao<TCampo>> catalogo)
        where TCampo : struct, Enum
    {
        var porChave = new Dictionary<string, TCampo>(StringComparer.Ordinal);
        foreach (var coluna in catalogo)
        {
            porChave.TryAdd(ChaveCabecalho(coluna.Titulo), coluna.Campo);
            foreach (var sinonimo in coluna.Sinonimos)
                porChave.TryAdd(ChaveCabecalho(sinonimo), coluna.Campo);
        }

        var indices = new Dictionary<TCampo, int>();
        var ignoradas = new List<string>();
        for (var i = 0; i < cabecalhos.Count; i++)
        {
            var titulo = cabecalhos[i]?.Trim() ?? string.Empty;
            if (titulo.Length == 0)
                continue;

            var chave = ChaveCabecalho(titulo);
            if (porChave.TryGetValue(chave, out var campo) && !indices.ContainsKey(campo))
                indices[campo] = i;
            else
                ignoradas.Add(titulo);
        }

        var faltando = catalogo.Where(c => c.Obrigatoria && !indices.ContainsKey(c.Campo)).Select(c => c.Titulo).ToList();
        return new MapaColunas<TCampo>(indices, ignoradas, faltando);
    }

    /// <summary>
    /// Pelo nome da aba ("Clientes", "Contratos") ou, sem nome conhecido
    /// (CSV, planilha de outro sistema), pelos cabeçalhos: com serviço e valor
    /// é contrato; com documento e nome é cliente.
    /// </summary>
    public static TipoAbaImportacao TipoDaAba(string? nome, IReadOnlyList<string> cabecalhos)
    {
        var chaveNome = ChaveCabecalho(nome);
        if (chaveNome.Contains("instruc") || chaveNome.Contains("lista") || chaveNome.Contains("ajuda"))
            return TipoAbaImportacao.Ignorada;
        if (chaveNome.Contains("contrato"))
            return TipoAbaImportacao.Contratos;
        if (chaveNome.Contains("cliente") || chaveNome.Contains("tomador"))
            return TipoAbaImportacao.Clientes;

        var contratos = Mapear(cabecalhos, Contratos);
        if (contratos.Tem(CampoContrato.Servico) && contratos.Tem(CampoContrato.ValorUnitario))
            return TipoAbaImportacao.Contratos;

        var clientes = Mapear(cabecalhos, Clientes);
        if (clientes.Tem(CampoCliente.CpfCnpj) && (clientes.Tem(CampoCliente.Nome) || clientes.Indices.Count >= 3))
            return TipoAbaImportacao.Clientes;

        return TipoAbaImportacao.Ignorada;
    }
}

public sealed record MapaColunas<TCampo>(
    IReadOnlyDictionary<TCampo, int> Indices,
    IReadOnlyList<string> Ignoradas,
    IReadOnlyList<string> Faltando) where TCampo : struct, Enum
{
    public bool Tem(TCampo campo) => Indices.ContainsKey(campo);

    public CelulaLida Celula(LinhaLida linha, TCampo campo) =>
        Indices.TryGetValue(campo, out var i) ? linha.Celula(i) : CelulaLida.Vazia;
}
