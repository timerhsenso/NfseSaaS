using ClosedXML.Excel;
using NfseSaaS.Application.UseCases.Importacao;

namespace NfseSaaS.Infrastructure.Importacao;

/// <summary>
/// IM1: monta o modelo .xlsx para baixar e a planilha das linhas com erro.
/// Larguras fixas (sem AdjustToContents, que precisa de fonte instalada no
/// servidor). CPF/CNPJ, CEP e código IBGE ficam como texto, para o Excel não
/// comer o zero da frente.
/// </summary>
public static class GeradorPlanilhaImportacao
{
    private const int LinhasComLista = 2000;
    private static readonly XLColor CorCabecalho = XLColor.FromHtml("#1F4E79");
    private static readonly XLColor CorObrigatoria = XLColor.FromHtml("#C55A11");
    private static readonly XLColor CorErro = XLColor.FromHtml("#C00000");

    /// <param name="servicos">Descrições dos serviços ativos da empresa (lista suspensa da coluna Serviço).</param>
    public static byte[] Modelo(string nomeEmpresa, IReadOnlyList<string> servicos)
    {
        using var pasta = new XLWorkbook();

        var clientes = pasta.AddWorksheet(ColunasImportacao.NomeAbaClientes);
        var contratos = pasta.AddWorksheet(ColunasImportacao.NomeAbaContratos);
        var instrucoes = pasta.AddWorksheet(ColunasImportacao.NomeAbaInstrucoes);
        var listas = pasta.AddWorksheet(ColunasImportacao.NomeAbaListas);

        var faixas = MontarListas(listas, servicos);
        listas.Hide();

        MontarAba(clientes, ColunasImportacao.Clientes, faixas);
        MontarAba(contratos, ColunasImportacao.Contratos, faixas);
        MontarInstrucoes(instrucoes, nomeEmpresa, servicos.Count);

        clientes.SetTabActive();
        return Salvar(pasta);
    }

    public static byte[] RelatorioErros(RelatorioErrosImportacaoRequest request)
    {
        using var pasta = new XLWorkbook();
        var nomesUsados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var aba in request.Abas.Where(a => a.Linhas.Count > 0))
        {
            var nome = NomeAbaUnico(aba.Nome, nomesUsados);
            var planilha = pasta.AddWorksheet(nome);
            var cabecalhos = aba.Cabecalhos.Concat(["Erro"]).ToList();

            for (var c = 0; c < cabecalhos.Count; c++)
            {
                var celula = planilha.Cell(1, c + 1);
                celula.Value = cabecalhos[c];
                EstiloCabecalho(celula, c == cabecalhos.Count - 1 ? CorErro : CorCabecalho);
                planilha.Column(c + 1).Width = c == cabecalhos.Count - 1 ? 70 : Math.Clamp(cabecalhos[c].Length + 6, 12, 36);
                planilha.Column(c + 1).Style.NumberFormat.Format = "@";
            }

            var linha = 2;
            foreach (var erro in aba.Linhas)
            {
                for (var c = 0; c < aba.Cabecalhos.Count; c++)
                    planilha.Cell(linha, c + 1).SetValue(c < erro.Valores.Count ? erro.Valores[c] ?? string.Empty : string.Empty);
                var celulaErro = planilha.Cell(linha, cabecalhos.Count);
                celulaErro.SetValue(erro.Erro);
                celulaErro.Style.Font.FontColor = CorErro;
                celulaErro.Style.Alignment.WrapText = true;
                linha++;
            }

            planilha.SheetView.FreezeRows(1);
        }

        if (pasta.Worksheets.Count == 0)
            pasta.AddWorksheet("Sem erros").Cell(1, 1).Value = "Nenhuma linha com erro.";

        return Salvar(pasta);
    }

    // ---------- partes do modelo ----------

    private static Dictionary<string, IXLRange> MontarListas(IXLWorksheet listas, IReadOnlyList<string> servicos)
    {
        var conteudo = new (string Nome, IReadOnlyList<string> Itens)[]
        {
            (ColunasImportacao.ListaSimNao, RegrasImportacao.OpcoesSimNao),
            (ColunasImportacao.ListaDisponivelPara, RegrasImportacao.OpcoesDisponivelPara),
            (ColunasImportacao.ListaCobranca, RegrasImportacao.OpcoesCobranca),
            (ColunasImportacao.ListaIndice, RegrasImportacao.OpcoesIndice),
            (ColunasImportacao.ListaSituacao, RegrasImportacao.OpcoesSituacao),
            (ColunasImportacao.ListaServicos, servicos.Count > 0 ? servicos : ["(cadastre os serviços antes)"])
        };

        var faixas = new Dictionary<string, IXLRange>(StringComparer.Ordinal);
        for (var c = 0; c < conteudo.Length; c++)
        {
            var (nome, itens) = conteudo[c];
            listas.Cell(1, c + 1).Value = nome;
            for (var i = 0; i < itens.Count; i++)
                listas.Cell(i + 2, c + 1).SetValue(itens[i]);
            faixas[nome] = listas.Range(2, c + 1, itens.Count + 1, c + 1);
        }
        return faixas;
    }

    private static void MontarAba<TCampo>(IXLWorksheet planilha, IReadOnlyList<ColunaImportacao<TCampo>> colunas, IReadOnlyDictionary<string, IXLRange> listas)
        where TCampo : struct, Enum
    {
        for (var c = 0; c < colunas.Count; c++)
        {
            var coluna = colunas[c];
            var numero = c + 1;
            var celula = planilha.Cell(1, numero);
            celula.Value = coluna.Titulo;
            EstiloCabecalho(celula, coluna.Obrigatoria ? CorObrigatoria : CorCabecalho);

            var ajuda = (coluna.Obrigatoria ? "Obrigatória. " : string.Empty) + coluna.Ajuda;
            if (!string.IsNullOrWhiteSpace(ajuda))
                celula.CreateComment().AddText(ajuda.Trim());

            var colunaPlanilha = planilha.Column(numero);
            colunaPlanilha.Width = coluna.Largura;
            var dados = planilha.Range(2, numero, LinhasComLista + 1, numero);
            dados.Style.NumberFormat.Format = coluna.Formato switch
            {
                FormatoColuna.Data => "dd/mm/yyyy",
                FormatoColuna.Valor => "#,##0.00",
                FormatoColuna.Percentual => "0.00##",
                FormatoColuna.Numero => "0",
                _ => "@"
            };

            if (coluna.Lista is { } nomeLista && listas.TryGetValue(nomeLista, out var faixa))
            {
                var validacao = dados.CreateDataValidation();
                validacao.List(faixa, true);
                // Serviço fora da lista: avisa, mas deixa digitar (o sistema confere de novo).
                validacao.ErrorStyle = nomeLista == ColunasImportacao.ListaServicos ? XLErrorStyle.Warning : XLErrorStyle.Stop;
                validacao.ErrorTitle = coluna.Titulo;
                validacao.ErrorMessage = "Escolha um item da lista.";
            }
        }

        planilha.SheetView.FreezeRows(1);
        planilha.Row(1).Height = 30;
    }

    private static void MontarInstrucoes(IXLWorksheet planilha, string nomeEmpresa, int quantidadeServicos)
    {
        planilha.Column(1).Width = 30;
        planilha.Column(2).Width = 13;
        planilha.Column(3).Width = 90;
        planilha.Column(4).Width = 28;

        var linha = 1;
        void Texto(string texto, bool negrito = false, double tamanho = 11)
        {
            var celula = planilha.Cell(linha, 1);
            celula.Value = texto;
            celula.Style.Font.Bold = negrito;
            celula.Style.Font.FontSize = tamanho;
            linha++;
        }

        Texto("Importar clientes e contratos — micoempresa", true, 15);
        Texto($"Modelo gerado para a empresa {nomeEmpresa}.");
        linha++;
        Texto("Como usar", true, 12);
        Texto("1. Preencha a aba Clientes (uma linha por cliente) e, se quiser, a aba Contratos.");
        Texto("2. As colunas em laranja são obrigatórias. Passe o mouse no título para ver como preencher.");
        Texto("3. No sistema: Clientes › Importar, envie este arquivo e confira linha por linha antes de gravar.");
        Texto("4. Cliente que já está cadastrado (mesmo CPF/CNPJ) não é duplicado. Para atualizar, marque \"Atualizar os que já existem\": só as células preenchidas mudam.");
        Texto("5. Contrato com vários serviços: repita o Nº do contrato em várias linhas, uma por serviço.");
        Texto(quantidadeServicos > 0
            ? "6. A coluna Serviço da aba Contratos tem a lista dos serviços desta empresa."
            : "6. Esta empresa ainda não tem serviços: cadastre em Serviços antes de importar contratos e baixe o modelo de novo.");
        Texto("Também aceitamos a planilha exportada de outro sistema (.xlsx ou .csv): reconhecemos os nomes de coluna mais comuns.");
        linha++;

        TabelaColunas(planilha, ref linha, "Aba Clientes", ColunasImportacao.Clientes);
        linha++;
        TabelaColunas(planilha, ref linha, "Aba Contratos", ColunasImportacao.Contratos);
    }

    private static void TabelaColunas<TCampo>(IXLWorksheet planilha, ref int linha, string titulo, IReadOnlyList<ColunaImportacao<TCampo>> colunas)
        where TCampo : struct, Enum
    {
        var tituloCelula = planilha.Cell(linha, 1);
        tituloCelula.Value = titulo;
        tituloCelula.Style.Font.Bold = true;
        tituloCelula.Style.Font.FontSize = 12;
        linha++;

        string[] cabecalho = ["Coluna", "Obrigatória", "Como preencher", "Exemplo"];
        for (var c = 0; c < cabecalho.Length; c++)
            EstiloCabecalho(planilha.Cell(linha, c + 1).SetValue(cabecalho[c]), CorCabecalho);
        linha++;

        foreach (var coluna in colunas)
        {
            planilha.Cell(linha, 1).SetValue(coluna.Titulo);
            planilha.Cell(linha, 2).SetValue(coluna.Obrigatoria ? "Sim" : "");
            planilha.Cell(linha, 3).SetValue(coluna.Ajuda);
            planilha.Cell(linha, 3).Style.Alignment.WrapText = true;
            planilha.Cell(linha, 4).SetValue(coluna.Exemplo);
            if (coluna.Obrigatoria)
                planilha.Cell(linha, 1).Style.Font.FontColor = CorObrigatoria;
            linha++;
        }
    }

    // ---------- comum ----------

    private static void EstiloCabecalho(IXLCell celula, XLColor cor)
    {
        celula.Style.Font.Bold = true;
        celula.Style.Font.FontColor = XLColor.White;
        celula.Style.Fill.BackgroundColor = cor;
        celula.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        celula.Style.Alignment.WrapText = true;
    }

    /// <summary>Nome de aba do Excel: até 31 letras, sem : \ / ? * [ ], sem repetir.</summary>
    private static string NomeAbaUnico(string? nome, HashSet<string> usados)
    {
        var limpo = new string((nome ?? "Planilha").Where(c => c is not (':' or '\\' or '/' or '?' or '*' or '[' or ']')).ToArray()).Trim();
        if (limpo.Length == 0)
            limpo = "Planilha";
        if (limpo.Length > 31)
            limpo = limpo[..31];

        var candidato = limpo;
        for (var i = 2; !usados.Add(candidato); i++)
            candidato = (limpo.Length > 27 ? limpo[..27] : limpo) + $" ({i})";
        return candidato;
    }

    private static byte[] Salvar(XLWorkbook pasta)
    {
        using var memoria = new MemoryStream();
        pasta.SaveAs(memoria);
        return memoria.ToArray();
    }
}
