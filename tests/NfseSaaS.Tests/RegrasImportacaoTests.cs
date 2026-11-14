using NfseSaaS.Application.UseCases.Importacao;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Importacao;
using Xunit;
using ClosedXML.Excel;
using System.Text;

namespace NfseSaaS.Tests;

/// <summary>
/// IM1: o que a importação entende de cada célula (como as pessoas preenchem
/// no Excel), o reconhecimento de cabeçalho de outros sistemas e a leitura e
/// geração dos arquivos .xlsx/.csv.
/// </summary>
public class RegrasImportacaoTests
{
    private static CelulaLida T(string texto) => new(texto);
    private static CelulaLida N(decimal numero, string? texto = null) => new(texto ?? numero.ToString(System.Globalization.CultureInfo.InvariantCulture), numero);

    [Fact]
    public void Documento_volta_com_os_zeros_que_o_excel_comeu_e_confere_o_digito()
    {
        Assert.Equal("01234567890", RegrasImportacao.LerDocumento(N(1234567890m)).Valor);
        Assert.Equal("00123456797", RegrasImportacao.LerDocumento(T("123456797")).Valor);
        Assert.Equal("04747304000178", RegrasImportacao.LerDocumento(N(4747304000178m)).Valor);
        Assert.Equal("12345678000195", RegrasImportacao.LerDocumento(T("12.345.678/0001-95")).Valor);
        Assert.Equal("52998224725", RegrasImportacao.LerDocumento(T(" 529.982.247-25 ")).Valor);

        Assert.False(RegrasImportacao.LerDocumento(T("123.456.789-00")).Ok);
        Assert.True(RegrasImportacao.LerDocumento(T("")).Ok);
        Assert.Null(RegrasImportacao.LerDocumento(T("")).Valor);
        Assert.Equal("12.345.678/0001-95", RegrasImportacao.FormatarDocumento("12345678000195"));
        Assert.Equal("529.982.247-25", RegrasImportacao.FormatarDocumento("52998224725"));
    }

    [Fact]
    public void Valores_no_jeito_brasileiro_ou_do_excel()
    {
        Assert.Equal(1500m, RegrasImportacao.LerDecimal(T("R$ 1.500,00")).Valor);
        Assert.Equal(1234.56m, RegrasImportacao.LerDecimal(T("1234,56")).Valor);
        Assert.Equal(1234.56m, RegrasImportacao.LerDecimal(T("1,234.56")).Valor);
        Assert.Equal(1234m, RegrasImportacao.LerDecimal(T("1.234")).Valor);
        Assert.Equal(12.5m, RegrasImportacao.LerDecimal(T("12.5")).Valor);
        // Célula numérica vale pelo número (1,234 não vira mil duzentos e trinta e quatro).
        Assert.Equal(1.234m, RegrasImportacao.LerDecimal(N(1.234m, "1,234")).Valor);
        Assert.Null(RegrasImportacao.LerDecimal(T(" ")).Valor);
        Assert.False(RegrasImportacao.LerDecimal(T("mil reais")).Ok);

        Assert.Equal(1.5m, RegrasImportacao.LerPercentual(T("1,5%")).Valor);
        Assert.Equal(0.65m, RegrasImportacao.LerPercentual(new CelulaLida("0,65%", 0.0065m, EhPercentual: true)).Valor);
        Assert.False(RegrasImportacao.LerPercentual(T("150")).Ok);

        Assert.Equal(12, RegrasImportacao.LerInteiro(T("12")).Valor);
        Assert.False(RegrasImportacao.LerInteiro(T("1,5")).Ok);
    }

    [Fact]
    public void Datas_como_data_texto_ou_numero_do_excel()
    {
        Assert.Equal(new DateOnly(2025, 3, 1), RegrasImportacao.LerData(T("01/03/2025")).Valor);
        Assert.Equal(new DateOnly(2025, 3, 1), RegrasImportacao.LerData(T("1/3/2025")).Valor);
        Assert.Equal(new DateOnly(2025, 3, 1), RegrasImportacao.LerData(T("2025-03-01")).Valor);
        Assert.Equal(new DateOnly(2025, 3, 1), RegrasImportacao.LerData(new CelulaLida("01/03/2025", Data: new DateTime(2025, 3, 1))).Valor);
        Assert.Equal(new DateOnly(2025, 1, 1), RegrasImportacao.LerData(N(45658m)).Valor);
        Assert.False(RegrasImportacao.LerData(T("março")).Ok);
        Assert.False(RegrasImportacao.LerData(T("31/02/2025")).Ok);
    }

    [Fact]
    public void Escolhas_aceitam_os_jeitos_comuns_de_escrever()
    {
        Assert.True(RegrasImportacao.LerSimNao(T("SIM")).Valor);
        Assert.True(RegrasImportacao.LerSimNao(T("x")).Valor);
        Assert.False(RegrasImportacao.LerSimNao(T("Não")).Valor);
        Assert.Null(RegrasImportacao.LerSimNao(T("")).Valor);
        Assert.False(RegrasImportacao.LerSimNao(T("talvez")).Ok);

        Assert.True(RegrasImportacao.LerDisponivelPara(T("Todas as empresas")).Valor);
        Assert.False(RegrasImportacao.LerDisponivelPara(T("Só esta empresa")).Valor);

        Assert.Equal(TipoCobrancaContrato.Avulso, RegrasImportacao.LerCobranca(T("avulsa")).Valor);
        Assert.Equal(IndiceReajusteContrato.Igpm, RegrasImportacao.LerIndice(T("IGP-M")).Valor);
        Assert.Equal(IndiceReajusteContrato.Outro, RegrasImportacao.LerIndice(T("nenhum")).Valor);
        Assert.False(RegrasImportacao.LerIndice(T("dólar")).Ok);
        Assert.Equal(StatusContrato.Suspenso, RegrasImportacao.LerSituacao(T("Suspenso")).Valor);
        Assert.Equal(StatusContrato.Cancelado, RegrasImportacao.LerSituacao(T("inativo")).Valor);
    }

    [Fact]
    public void Endereco_cep_ibge_uf_e_municipio()
    {
        Assert.Equal("01310100", RegrasImportacao.LerCep(N(1310100m)).Valor);
        Assert.Equal("40010000", RegrasImportacao.LerCep(T("40010-000")).Valor);
        Assert.False(RegrasImportacao.LerCep(T("4001")).Ok);
        Assert.Equal("2927408", RegrasImportacao.LerCodigoMunicipio(N(2927408m)).Valor);
        Assert.False(RegrasImportacao.LerCodigoMunicipio(T("29274")).Ok);
        Assert.Equal("BA", RegrasImportacao.LerUf(T("ba")).Valor);
        Assert.False(RegrasImportacao.LerUf(T("Bahia")).Ok);

        Assert.Equal(("Salvador", "BA"), RegrasImportacao.SepararMunicipio("Salvador/BA", null));
        Assert.Equal(("Salvador", "BA"), RegrasImportacao.SepararMunicipio("Salvador - BA", null));
        Assert.Equal(("Salvador", "SP"), RegrasImportacao.SepararMunicipio("Salvador (BA)", "SP"));
        Assert.Equal(("Embu-Guaçu", null), RegrasImportacao.SepararMunicipio("Embu-Guaçu", null));
        Assert.Equal(RegrasImportacao.ChaveMunicipio("EMBU GUACU"), RegrasImportacao.ChaveMunicipio("Embu-Guaçu"));

        Assert.Equal(new[] { "a@x.com", "b@x.com" }, RegrasImportacao.LerEmails(T("a@x.com; b@x.com, A@x.com")));
    }

    [Fact]
    public void Cabecalho_de_outro_sistema_e_reconhecido()
    {
        var mapa = ColunasImportacao.Mapear(
            ["CNPJ/CPF", "Razão Social", "Nome Fantasia", "E-mail", "Cidade", "Estado", "Código Interno", ""],
            ColunasImportacao.Clientes);
        Assert.Equal(0, mapa.Indices[CampoCliente.CpfCnpj]);
        Assert.Equal(1, mapa.Indices[CampoCliente.Nome]);
        Assert.Equal(2, mapa.Indices[CampoCliente.Apelido]);
        Assert.Equal(3, mapa.Indices[CampoCliente.Email]);
        Assert.Equal(4, mapa.Indices[CampoCliente.Municipio]);
        Assert.Equal(5, mapa.Indices[CampoCliente.Uf]);
        Assert.Equal(new[] { "Código Interno" }, mapa.Ignoradas);
        Assert.Empty(mapa.Faltando);

        var contratos = ColunasImportacao.Mapear(["CNPJ do cliente", "Serviço", "Valor mensal", "Data de início"], ColunasImportacao.Contratos);
        Assert.Empty(contratos.Faltando);

        Assert.Equal(TipoAbaImportacao.Contratos, ColunasImportacao.TipoDaAba("export", ["CNPJ", "Serviço", "Valor"]));
        Assert.Equal(TipoAbaImportacao.Clientes, ColunasImportacao.TipoDaAba("export", ["CNPJ", "Razão social"]));
        Assert.Equal(TipoAbaImportacao.Ignorada, ColunasImportacao.TipoDaAba("Instruções", ["Coluna", "Obrigatória"]));
        Assert.Equal(TipoAbaImportacao.Clientes, ColunasImportacao.TipoDaAba("Clientes", []));
    }

    [Fact]
    public void Cada_titulo_e_sinonimo_aponta_para_um_campo_so()
    {
        foreach (var catalogo in new[] { ColunasImportacao.Clientes.Select(c => (c.Titulo, c.Sinonimos, c.Campo.ToString())), ColunasImportacao.Contratos.Select(c => (c.Titulo, c.Sinonimos, c.Campo.ToString())) })
        {
            var vistos = new Dictionary<string, string>();
            foreach (var (titulo, sinonimos, campo) in catalogo)
            {
                foreach (var nome in sinonimos.Prepend(titulo))
                {
                    var chave = ColunasImportacao.ChaveCabecalho(nome);
                    if (vistos.TryGetValue(chave, out var outro))
                        Assert.True(outro == campo, $"\"{nome}\" aparece em {outro} e em {campo}.");
                    vistos[chave] = campo;
                }
            }
        }
    }

    [Fact]
    public void Ultimo_aniversario_do_contrato()
    {
        var hoje = new DateOnly(2026, 11, 14);
        Assert.Equal(new DateOnly(2026, 3, 1), RegrasImportacao.UltimoAniversario(new DateOnly(2023, 3, 1), 12, hoje));
        Assert.Equal(new DateOnly(2026, 11, 14), RegrasImportacao.UltimoAniversario(new DateOnly(2025, 11, 14), 12, hoje));
        Assert.Null(RegrasImportacao.UltimoAniversario(new DateOnly(2026, 1, 10), 12, hoje));
        Assert.Equal(new DateOnly(2026, 7, 10), RegrasImportacao.UltimoAniversario(new DateOnly(2025, 7, 10), 6, hoje));
        Assert.Null(RegrasImportacao.UltimoAniversario(new DateOnly(2020, 1, 1), 0, hoje));
    }

    [Fact]
    public void Apelido_candidatos_cabem_no_tamanho()
    {
        var candidatos = RegrasImportacao.CandidatosApelido("Uma Empresa Com Um Nome Muito Comprido Demais Ltda", "12345678000195", 40).Take(3).ToList();
        Assert.Equal("Uma Empresa Com Um Nome Muito Comprido D", candidatos[0]);
        Assert.EndsWith(" 000195", candidatos[1]);
        Assert.EndsWith(" 000195-2", candidatos[2]);
        Assert.All(candidatos, c => Assert.True(c.Length <= 40));
    }

    // ---------- Arquivos ----------

    [Fact]
    public void Modelo_xlsx_tem_as_abas_e_volta_lido_sem_linhas()
    {
        var modelo = GeradorPlanilhaImportacao.Modelo("Minha Empresa", ["Manutenção mensal", "Consultoria"]);
        var lida = LeitorPlanilha.Ler("modelo.xlsx", modelo);

        // A aba Listas fica escondida e não é lida.
        Assert.Equal(new[] { "Clientes", "Contratos", "Instruções" }, lida.Abas.Select(a => a.Nome));
        var clientes = lida.Abas[0];
        Assert.Equal(ColunasImportacao.Clientes.Select(c => c.Titulo), clientes.Cabecalhos);
        Assert.Empty(clientes.Linhas);
        Assert.Equal(TipoAbaImportacao.Clientes, ColunasImportacao.TipoDaAba(clientes.Nome, clientes.Cabecalhos));
        Assert.Equal(TipoAbaImportacao.Ignorada, ColunasImportacao.TipoDaAba(lida.Abas[2].Nome, lida.Abas[2].Cabecalhos));

        using var pasta = new XLWorkbook(new MemoryStream(modelo));
        Assert.Equal("Consultoria", pasta.Worksheet("Listas").Column(6).Cell(3).GetString());
        Assert.Equal("@", pasta.Worksheet("Clientes").Cell(2, 1).Style.NumberFormat.Format);
    }

    [Fact]
    public void Xlsx_com_titulo_em_cima_numeros_datas_e_porcentagem()
    {
        using var pasta = new XLWorkbook();
        var aba = pasta.AddWorksheet("Exportação");
        aba.Cell(1, 1).Value = "Relatório de clientes — sistema antigo";
        aba.Cell(3, 1).Value = "CPF";
        aba.Cell(3, 2).Value = "Nome";
        aba.Cell(3, 3).Value = "Desde";
        aba.Cell(3, 4).Value = "IRRF";
        aba.Cell(4, 1).Value = 1234567890;
        aba.Cell(4, 2).Value = "Fulano";
        aba.Cell(4, 3).Value = new DateTime(2024, 5, 2);
        aba.Cell(4, 4).Value = 0.015;
        aba.Cell(4, 4).Style.NumberFormat.Format = "0.0%";
        aba.Cell(6, 2).Value = "Beltrano"; // linha 5 em branco é pulada
        using var memoria = new MemoryStream();
        pasta.SaveAs(memoria);

        var lida = LeitorPlanilha.Ler("export.xlsx", memoria.ToArray()).Abas.Single();
        Assert.Equal(new[] { "CPF", "Nome", "Desde", "IRRF" }, lida.Cabecalhos);
        Assert.Equal(new[] { 4, 6 }, lida.Linhas.Select(l => l.Numero));
        var linha = lida.Linhas[0];
        Assert.Equal("01234567890", RegrasImportacao.LerDocumento(linha.Celula(0)).Valor);
        Assert.Equal(new DateOnly(2024, 5, 2), RegrasImportacao.LerData(linha.Celula(2)).Valor);
        Assert.Equal(1.5m, RegrasImportacao.LerPercentual(linha.Celula(3)).Valor);
    }

    [Fact]
    public void Csv_do_excel_em_portugues_com_ponto_e_virgula_e_acento()
    {
        var texto = "CNPJ;Razão social;Município\r\n\"12.345.678/0001-95\";\"Padaria São João; Filial\";Salvador\r\n;;\r\n52998224725;José;Feira de Santana\r\n";
        var ansi = CodificarWindows1252(texto);

        var lida = LeitorPlanilha.Ler("clientes.csv", ansi).Abas.Single();
        Assert.Equal("clientes", lida.Nome);
        Assert.Equal(new[] { "CNPJ", "Razão social", "Município" }, lida.Cabecalhos);
        Assert.Equal(2, lida.Linhas.Count);
        Assert.Equal("Padaria São João; Filial", lida.Linhas[0].Celula(1).Texto);
        Assert.Equal("José", lida.Linhas[1].Celula(1).Texto);
        Assert.Equal(4, lida.Linhas[1].Numero);

        var utf8 = LeitorPlanilha.Ler("c.csv", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("cnpj,nome\n12345678000195,Ação\n")).ToArray()).Abas.Single();
        Assert.Equal("Ação", utf8.Linhas[0].Celula(1).Texto);
    }

    [Fact]
    public void Arquivo_que_nao_e_planilha_tem_mensagem_clara()
    {
        Assert.Contains(".xlsx", Assert.Throws<NfseSaaS.Application.Exceptions.RegraNegocioException>(
            () => LeitorPlanilha.Ler("antigo.xls", [0xD0, 0xCF, 0x11, 0xE0, 0, 0, 0, 0, 0])).Message);
        Assert.Throws<NfseSaaS.Application.Exceptions.RegraNegocioException>(() => LeitorPlanilha.Ler("x.xlsx", [0x50, 0x4B, 1, 2, 3, 4]));
        Assert.Throws<NfseSaaS.Application.Exceptions.RegraNegocioException>(() => LeitorPlanilha.Ler("foto.png", [1, 2, 3]));
        Assert.Throws<NfseSaaS.Application.Exceptions.RegraNegocioException>(() => LeitorPlanilha.Ler("vazio.csv", []));
    }

    [Fact]
    public void Relatorio_de_erros_devolve_as_linhas_com_a_coluna_erro()
    {
        var arquivo = GeradorPlanilhaImportacao.RelatorioErros(new RelatorioErrosImportacaoRequest([
            new AbaRelatorioErros("Clientes", ["CPF/CNPJ", "Nome"], [new LinhaRelatorioErros(3, ["01234567890", "Fulano"], "Falta o município.")])
        ]));
        var lida = LeitorPlanilha.Ler("erros.xlsx", arquivo).Abas.Single();
        Assert.Equal(new[] { "CPF/CNPJ", "Nome", "Erro" }, lida.Cabecalhos);
        // Texto: o zero da frente continua lá.
        Assert.Equal("01234567890", lida.Linhas[0].Celula(0).Texto);
        Assert.Equal("Falta o município.", lida.Linhas[0].Celula(2).Texto);
    }

    private static byte[] CodificarWindows1252(string texto)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252).GetBytes(texto);
    }
}
