using System.Text;
using System.Text.Json;
using NfseSaaS.Application.UseCases.DadosConta;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.DadosConta;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>D1 — exportação dos dados da conta: regras, CSV/JSON e nomes dentro do ZIP.</summary>
public class ExportacaoDadosTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void So_administrador_um_pedido_por_vez_e_ate_3_por_dia()
    {
        Assert.Equal(RegrasExportacaoDados.MotivoSoAdministrador, RegrasExportacaoDados.MotivoParaNaoPedir(false, false, null, 0));
        Assert.Equal(RegrasExportacaoDados.MotivoEmAndamento, RegrasExportacaoDados.MotivoParaNaoPedir(true, true, null, 0));
        Assert.Equal(RegrasExportacaoDados.MotivoLimiteDiario, RegrasExportacaoDados.MotivoParaNaoPedir(true, false, null, 3));
        Assert.Equal(RegrasExportacaoDados.MotivoServidorCheio, RegrasExportacaoDados.MotivoParaNaoPedir(true, false, null, 0, discoCheio: true));
        Assert.Null(RegrasExportacaoDados.MotivoParaNaoPedir(true, false, null, 2));
    }

    [Fact]
    public void Com_uma_pronta_valendo_baixa_essa_em_vez_de_montar_outra()
    {
        var ate = new DateTimeOffset(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);
        var motivo = RegrasExportacaoDados.MotivoParaNaoPedir(true, false, ate, 0);
        Assert.Equal(RegrasExportacaoDados.MotivoJaTemPronta(ate), motivo);
        Assert.Contains("até 16/11/2026", motivo);
        Assert.Contains("XML do mês", motivo);
        // A pronta pesa mais que o limite diário e o disco (a mensagem útil é "baixe essa").
        Assert.Equal(motivo, RegrasExportacaoDados.MotivoParaNaoPedir(true, false, ate, 5, discoCheio: true));
    }

    [Fact]
    public void Nome_do_zip_do_xml_do_mes()
    {
        Assert.Equal("XML-NFSe-04747304000178-2026-10.zip",
            NfseSaaS.Application.UseCases.Nfse.RegrasXmlMes.NomeArquivo("04.747.304/0001-78", new DateOnly(2026, 10, 1), TipoAmbiente.Producao));
        Assert.Equal("XML-NFSe-04747304000178-2026-10-homologacao.zip",
            NfseSaaS.Application.UseCases.Nfse.RegrasXmlMes.NomeArquivo("04747304000178", new DateOnly(2026, 10, 1), TipoAmbiente.Homologacao));
    }

    [Fact]
    public void Baixa_so_pronta_e_dentro_dos_7_dias()
    {
        var expira = RegrasExportacaoDados.Expiracao(Agora);
        Assert.Equal(Agora.AddDays(7), expira);
        Assert.True(RegrasExportacaoDados.PodeBaixar(StatusExportacaoDados.Pronta, expira, Agora.AddDays(6)));
        Assert.False(RegrasExportacaoDados.PodeBaixar(StatusExportacaoDados.Pronta, expira, expira));
        Assert.False(RegrasExportacaoDados.PodeBaixar(StatusExportacaoDados.Gerando, expira, Agora));
        Assert.False(RegrasExportacaoDados.PodeBaixar(StatusExportacaoDados.Expirada, expira, Agora));
        Assert.False(RegrasExportacaoDados.PodeBaixar(StatusExportacaoDados.Pronta, null, Agora));
    }

    [Fact]
    public void Nome_do_arquivo_leva_o_documento_e_a_hora_de_brasilia()
    {
        Assert.Equal("micoempresa-dados-04747304000178-20261109-1200.zip", RegrasExportacaoDados.NomeArquivo("04.747.304/0001-78", Agora));
        Assert.Equal("micoempresa-dados-conta-20261109-1200.zip", RegrasExportacaoDados.NomeArquivo("", Agora));
    }

    [Fact]
    public void Csv_para_o_excel_brasileiro()
    {
        var t = new TabelaExportacao("clientes", "Nome", "Valor", "Data", "Quando", "Ativo", "Status", "Vazio");
        t.Adicionar("Padaria; \"Pão\" Bom", 1234.5m, new DateOnly(2026, 11, 9), Agora, true, StatusExportacaoDados.Pronta, null);

        var bytes = t.ParaCsv();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var linhas = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n");
        Assert.Equal("Nome;Valor;Data;Quando;Ativo;Status;Vazio", linhas[0]);
        Assert.Equal("\"Padaria; \"\"Pão\"\" Bom\";1234,50;09/11/2026;09/11/2026 12:00;Sim;Pronta;", linhas[1]);
    }

    [Fact]
    public void Csv_nao_vira_formula_no_excel_mas_numero_negativo_fica()
    {
        var t = new TabelaExportacao("x", "A", "B", "C", "D");
        t.Adicionar("=HYPERLINK(\"http://mal\")", "+55 71 9999", "-12,50", -3.5m);
        var linha = Encoding.UTF8.GetString(t.ParaCsv()[3..]).Split("\r\n")[1];
        Assert.Equal("\"'=HYPERLINK(\"\"http://mal\"\")\";'+55 71 9999;-12,50;-3,50", linha);
    }

    [Fact]
    public void Json_mantem_os_tipos()
    {
        var id = Guid.NewGuid();
        var t = new TabelaExportacao("x", "Id", "Valor", "Data", "Ativo", "Status", "Nada");
        t.Adicionar(id, 10.25m, new DateOnly(2026, 1, 2), false, StatusExportacaoDados.Erro, null);

        using var doc = JsonDocument.Parse(t.ParaJson());
        var linha = doc.RootElement[0];
        Assert.Equal(id, linha.GetProperty("Id").GetGuid());
        Assert.Equal(10.25m, linha.GetProperty("Valor").GetDecimal());
        Assert.Equal("2026-01-02", linha.GetProperty("Data").GetString());
        Assert.False(linha.GetProperty("Ativo").GetBoolean());
        Assert.Equal("Erro", linha.GetProperty("Status").GetString());
        Assert.Equal(JsonValueKind.Null, linha.GetProperty("Nada").ValueKind);
    }

    [Fact]
    public void Linha_com_numero_errado_de_colunas_e_erro_de_programacao()
    {
        var t = new TabelaExportacao("x", "A", "B");
        Assert.Throws<ArgumentException>(() => t.Adicionar("so um"));
    }

    [Fact]
    public void Nomes_dentro_do_zip_sao_seguros_e_unicos()
    {
        Assert.Equal("_.._segredo", MontadorExportacaoDados.NomeSeguro("/../segredo", 50));
        Assert.Equal("arquivo", MontadorExportacaoDados.NomeSeguro("  ..  ", 50));
        Assert.Equal("abcde", MontadorExportacaoDados.NomeSeguro("abcdefgh", 5));

        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal("notas/1/A_1.xml", MontadorExportacaoDados.Unico(usados, "notas/1", "A_1", ".xml"));
        Assert.Equal("notas/1/a_1 (2).xml", MontadorExportacaoDados.Unico(usados, "notas/1", "a_1", ".xml"));
    }

    [Fact]
    public void Leiame_explica_o_que_ficou_de_fora_e_as_pendencias()
    {
        var texto = MontadorExportacaoDados.Leiame("Padaria Ltda", "123", Agora, 5, ["PDF da nota 7 não gerado."]);
        Assert.Contains("Notas: 5", texto);
        Assert.Contains("09/11/2026 12:00", texto);
        Assert.Contains("Certificados digitais e senhas", texto);
        Assert.Contains("PENDÊNCIAS", texto);
        Assert.Contains("PDF da nota 7", texto);
        Assert.DoesNotContain("PENDÊNCIAS", MontadorExportacaoDados.Leiame("P", "1", Agora, 0, []));
    }
}
