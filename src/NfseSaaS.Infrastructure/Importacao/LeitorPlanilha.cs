using System.Text;
using ClosedXML.Excel;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Importacao;

namespace NfseSaaS.Infrastructure.Importacao;

/// <summary>
/// IM1: lê a planilha enviada (.xlsx do Excel/LibreOffice/Google Planilhas ou
/// .csv) para o formato neutro das regras (<see cref="PlanilhaLida"/>).
/// O cabeçalho não precisa estar na linha 1: planilha exportada de outro
/// sistema costuma vir com título em cima. Vale a primeira linha (até a 10ª)
/// que tem dois ou mais cabeçalhos conhecidos.
/// </summary>
public static class LeitorPlanilha
{
    private const int LinhasProcuraCabecalho = 10;

    public static PlanilhaLida Ler(string nomeArquivo, byte[] conteudo)
    {
        if (conteudo.Length == 0)
            throw new RegraNegocioException("O arquivo está vazio.");
        if (conteudo.Length > RegrasImportacao.TamanhoMaximoArquivo)
            throw new RegraNegocioException(RegrasImportacao.MensagemArquivoGrande);

        var extensao = Path.GetExtension(nomeArquivo ?? string.Empty).ToLowerInvariant();
        var ehZip = conteudo.Length > 4 && conteudo[0] == 0x50 && conteudo[1] == 0x4B;
        var ehXlsAntigo = conteudo.Length > 8 && conteudo[0] == 0xD0 && conteudo[1] == 0xCF && conteudo[2] == 0x11 && conteudo[3] == 0xE0;

        if (ehXlsAntigo || extensao == ".xls")
            throw new RegraNegocioException("Planilha no formato antigo do Excel (.xls). No Excel, use Arquivo › Salvar como › Pasta de Trabalho do Excel (.xlsx) e envie de novo.");
        if (extensao is ".ods" or ".numbers")
            throw new RegraNegocioException("Salve a planilha como .xlsx (Excel) ou .csv e envie de novo.");

        if (ehZip)
            return LerXlsx(conteudo);
        if (extensao is ".csv" or ".txt" or "")
            return LerCsv(nomeArquivo ?? "planilha.csv", conteudo);

        throw new RegraNegocioException("Envie uma planilha .xlsx (Excel) ou .csv.");
    }

    // ---------- .xlsx ----------

    private static PlanilhaLida LerXlsx(byte[] conteudo)
    {
        XLWorkbook pasta;
        try
        {
            pasta = new XLWorkbook(new MemoryStream(conteudo));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RegraNegocioException("Não consegui abrir a planilha. Confira se o arquivo é um .xlsx válido (abra no Excel e salve de novo).");
        }

        using (pasta)
        {
            var abas = new List<AbaLida>();
            foreach (var planilha in pasta.Worksheets)
            {
                if (planilha.Visibility != XLWorksheetVisibility.Visible)
                    continue;

                var usado = planilha.RangeUsed(XLCellsUsedOptions.Contents);
                if (usado is null)
                {
                    abas.Add(new AbaLida(planilha.Name, Array.Empty<string>(), Array.Empty<LinhaLida>()));
                    continue;
                }

                var primeiraLinha = usado.FirstRow().RowNumber();
                var ultimaLinha = usado.LastRow().RowNumber();
                var ultimaColuna = usado.LastColumn().ColumnNumber();

                var linhaCabecalho = AcharCabecalho(
                    Enumerable.Range(primeiraLinha, Math.Min(LinhasProcuraCabecalho, ultimaLinha - primeiraLinha + 1))
                        .Select(n => (n, (IReadOnlyList<string>)Enumerable.Range(1, ultimaColuna)
                            .Select(c => TextoDe(planilha.Cell(n, c))).ToList()))
                        .ToList()) ?? primeiraLinha;

                var cabecalhos = Enumerable.Range(1, ultimaColuna).Select(c => TextoDe(planilha.Cell(linhaCabecalho, c))).ToList();
                // Colunas em branco no fim do cabeçalho não contam.
                while (cabecalhos.Count > 0 && string.IsNullOrWhiteSpace(cabecalhos[^1]))
                    cabecalhos.RemoveAt(cabecalhos.Count - 1);

                var linhas = new List<LinhaLida>();
                for (var n = linhaCabecalho + 1; n <= ultimaLinha; n++)
                {
                    var celulas = new List<CelulaLida>(cabecalhos.Count);
                    for (var c = 1; c <= cabecalhos.Count; c++)
                        celulas.Add(CelulaDe(planilha.Cell(n, c)));
                    var linha = new LinhaLida(n, celulas);
                    if (!linha.EstaVazia)
                        linhas.Add(linha);
                }

                abas.Add(new AbaLida(planilha.Name, cabecalhos, linhas));
            }

            return new PlanilhaLida(abas);
        }
    }

    private static string TextoDe(IXLCell celula)
    {
        try
        {
            return (celula.GetFormattedString() ?? string.Empty).Trim();
        }
        catch (Exception)
        {
            return celula.Value.ToString()?.Trim() ?? string.Empty;
        }
    }

    private static CelulaLida CelulaDe(IXLCell celula)
    {
        var valor = celula.Value;
        if (valor.IsBlank)
            return CelulaLida.Vazia;

        var texto = TextoDe(celula);
        if (valor.IsDateTime)
            return new CelulaLida(texto, Data: valor.GetDateTime());
        if (valor.IsNumber)
        {
            var numero = valor.GetNumber();
            decimal? comoDecimal = numero is > (double)decimal.MinValue and < (double)decimal.MaxValue
                ? decimal.Round((decimal)numero, 10)
                : null;
            var formato = celula.Style.NumberFormat.Format ?? string.Empty;
            var ehPercentual = formato.Contains('%') || celula.Style.NumberFormat.NumberFormatId is 9 or 10;
            return new CelulaLida(texto, comoDecimal, EhPercentual: ehPercentual);
        }
        if (valor.IsBoolean)
            return new CelulaLida(valor.GetBoolean() ? "Sim" : "Não");
        if (valor.IsError)
            return new CelulaLida(texto);
        return new CelulaLida(valor.IsText ? valor.GetText().Trim() : texto);
    }

    /// <summary>Primeira linha com 2+ cabeçalhos conhecidos (de cliente ou de contrato).</summary>
    private static int? AcharCabecalho(IReadOnlyList<(int Numero, IReadOnlyList<string> Textos)> candidatas)
    {
        foreach (var (numero, textos) in candidatas)
        {
            var clientes = ColunasImportacao.Mapear(textos, ColunasImportacao.Clientes).Indices.Count;
            var contratos = ColunasImportacao.Mapear(textos, ColunasImportacao.Contratos).Indices.Count;
            if (Math.Max(clientes, contratos) >= 2)
                return numero;
        }
        return null;
    }

    // ---------- .csv ----------

    private static PlanilhaLida LerCsv(string nomeArquivo, byte[] conteudo)
    {
        var texto = Decodificar(conteudo);
        var registros = SepararCsv(texto, DetectarSeparador(texto));

        var candidatas = registros.Take(LinhasProcuraCabecalho)
            .Select((r, i) => (i + 1, (IReadOnlyList<string>)r.Select(c => c.Trim()).ToList()))
            .ToList();
        var linhaCabecalho = AcharCabecalho(candidatas) ?? 1;

        var nomeAba = Path.GetFileNameWithoutExtension(nomeArquivo);
        if (registros.Count < linhaCabecalho)
            return new PlanilhaLida([new AbaLida(nomeAba, Array.Empty<string>(), Array.Empty<LinhaLida>())]);

        var cabecalhos = registros[linhaCabecalho - 1].Select(c => c.Trim()).ToList();
        while (cabecalhos.Count > 0 && string.IsNullOrWhiteSpace(cabecalhos[^1]))
            cabecalhos.RemoveAt(cabecalhos.Count - 1);

        var linhas = new List<LinhaLida>();
        for (var i = linhaCabecalho; i < registros.Count; i++)
        {
            var celulas = Enumerable.Range(0, cabecalhos.Count)
                .Select(c => c < registros[i].Count ? new CelulaLida(registros[i][c].Trim()) : CelulaLida.Vazia)
                .ToList();
            var linha = new LinhaLida(i + 1, celulas);
            if (!linha.EstaVazia)
                linhas.Add(linha);
        }

        return new PlanilhaLida([new AbaLida(nomeAba, cabecalhos, linhas)]);
    }

    /// <summary>UTF-8 (com ou sem BOM) ou, se não for UTF-8 válido, Windows-1252 — o "CSV (separado por vírgulas)" do Excel em português.</summary>
    internal static string Decodificar(byte[] conteudo)
    {
        if (conteudo.Length >= 3 && conteudo[0] == 0xEF && conteudo[1] == 0xBB && conteudo[2] == 0xBF)
            return Encoding.UTF8.GetString(conteudo, 3, conteudo.Length - 3);
        if (conteudo.Length >= 2 && conteudo[0] == 0xFF && conteudo[1] == 0xFE)
            return Encoding.Unicode.GetString(conteudo, 2, conteudo.Length - 2);

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(conteudo);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(conteudo);
        }
    }

    /// <summary>O separador que mais aparece na primeira linha fora de aspas: ";" (Excel em português), "," ou tab.</summary>
    internal static char DetectarSeparador(string texto)
    {
        var primeira = texto.Split('\n', 2)[0];
        var contagem = new Dictionary<char, int> { [';'] = 0, [','] = 0, ['\t'] = 0 };
        var dentroDeAspas = false;
        foreach (var c in primeira)
        {
            if (c == '"')
                dentroDeAspas = !dentroDeAspas;
            else if (!dentroDeAspas && contagem.ContainsKey(c))
                contagem[c]++;
        }
        var melhor = contagem.OrderByDescending(kv => kv.Value).First();
        return melhor.Value == 0 ? ';' : melhor.Key;
    }

    /// <summary>CSV com aspas (RFC 4180): campo entre aspas pode ter separador, quebra de linha e "" para aspas.</summary>
    internal static List<List<string>> SepararCsv(string texto, char separador)
    {
        var registros = new List<List<string>>();
        var atual = new List<string>();
        var campo = new StringBuilder();
        var dentroDeAspas = false;

        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (dentroDeAspas)
            {
                if (c == '"')
                {
                    if (i + 1 < texto.Length && texto[i + 1] == '"')
                    {
                        campo.Append('"');
                        i++;
                    }
                    else
                    {
                        dentroDeAspas = false;
                    }
                }
                else
                {
                    campo.Append(c);
                }
                continue;
            }

            if (c == '"' && campo.Length == 0)
            {
                dentroDeAspas = true;
            }
            else if (c == separador)
            {
                atual.Add(campo.ToString());
                campo.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n')
                    i++;
                atual.Add(campo.ToString());
                campo.Clear();
                registros.Add(atual);
                atual = new List<string>();
            }
            else
            {
                campo.Append(c);
            }
        }

        if (campo.Length > 0 || atual.Count > 0)
        {
            atual.Add(campo.ToString());
            registros.Add(atual);
        }

        return registros;
    }
}
