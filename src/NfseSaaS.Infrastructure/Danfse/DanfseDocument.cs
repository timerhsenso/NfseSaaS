using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace NfseSaaS.Infrastructure.Danfse;

/// <summary>
/// Layout do DANFSe v2.0 conforme NT 008/2026 (item 2.2 a 2.5).
///
/// LIMITAÇÃO DELIBERADA E DOCUMENTADA: a NT 008 especifica coordenadas
/// absolutas (X/Y/altura/largura em cm) pra cada campo — um layout de
/// posicionamento fixo. Este documento usa o DSL de FLUXO do QuestPDF
/// (Column/Row, não Canvas com coordenadas absolutas), na MESMA ordem,
/// coloca os MESMOS blocos, títulos e tamanhos de fonte da nota técnica,
/// mas não reproduz o posicionamento milimétrico exato de cada campo.
/// Ou seja: fidelidade de CONTEÚDO e ESTRUTURA, não fidelidade
/// pixel-a-pixel de coordenada. Se isso vier a ser auditado/certificado
/// contra o Anexo I literal, vai precisar de ajuste posicional fino
/// (Canvas do QuestPDF, ou outra biblioteca com posicionamento absoluto).
///
/// Cabeçalho, QR Code, blocos de identificação/prestador/tomador/serviço/
/// tributação/valores — todos presentes, nesta ordem, com sombreamento
/// nos campos exigidos (item 2.2.3) e marca d'água de cancelamento/
/// homologação (itens 2.4.3 e 2.5.1).
/// </summary>
internal sealed class DanfseDocument : IDocument
{
    private const string CorSombreamento = "#F2F2F2"; // cinza claro 5%, item 2.2.3

    private readonly DanfseXmlDados _d;
    private readonly bool _cancelada;
    private readonly string? _caminhoLogo;

    public DanfseDocument(DanfseXmlDados dados, bool cancelada, string? caminhoLogo)
    {
        _d = dados;
        _cancelada = cancelada;
        _caminhoLogo = caminhoLogo;
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0.4f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(7));

            page.Content().Column(coluna =>
            {
                coluna.Item().Element(Cabecalho);
                coluna.Item().PaddingTop(4).Element(DadosIdentificacao);
                coluna.Item().PaddingTop(4).Element(BlocoPrestador);
                coluna.Item().PaddingTop(4).Element(BlocoTomador);
                coluna.Item().PaddingTop(4).Element(BlocoDestinatario);
                coluna.Item().PaddingTop(4).Element(BlocoServico);
                coluna.Item().PaddingTop(4).Element(BlocoIssqn);
                coluna.Item().PaddingTop(4).Element(BlocoFederal);
                coluna.Item().PaddingTop(4).Element(BlocoIbsCbs);
                coluna.Item().PaddingTop(4).Element(BlocoValorTotal);
                coluna.Item().PaddingTop(4).Element(BlocoInformacoesComplementares);
                coluna.Item().PaddingTop(4).Element(Canhoto);
            });

            if (_cancelada)
                page.Foreground().AlignCenter().AlignMiddle().Rotate(-30)
                    .Text("CANCELADA").FontSize(50).FontColor(Colors.Grey.Medium).Bold();
        });
    }

    private void Cabecalho(IContainer container)
    {
        container.Border(1).Padding(4).Row(row =>
        {
            row.ConstantItem(120).AlignMiddle().Element(c =>
            {
                if (_caminhoLogo is not null && File.Exists(_caminhoLogo))
                    c.Image(_caminhoLogo).FitArea();
                else
                    c.Text("NFS-e").Bold().FontSize(14);
            });

            row.RelativeItem().AlignCenter().AlignMiddle().Column(col =>
            {
                col.Item().AlignCenter().Text("DANFSe v2.0").Bold().FontSize(9).FontFamily("Arial");
                col.Item().AlignCenter().Text("Documento Auxiliar da NFS-e").Bold().FontSize(9).FontFamily("Arial");
                if (_d.AmbienteDeHomologacao())
                    col.Item().AlignCenter().Text("NFS-e SEM VALIDADE JURÍDICA").Bold().FontSize(9).FontColor(Colors.Red.Medium);
            });

            row.ConstantItem(140).AlignMiddle().Column(col =>
            {
                col.Item().AlignRight().Text($"Município: {_d.MunicipioEmitenteUf()}").FontSize(8).FontFamily("Microsoft Sans Serif");
                col.Item().AlignRight().Text($"Ambiente Gerador: {_d.AmbienteGerador()}").FontSize(6).FontFamily("Microsoft Sans Serif");
                col.Item().AlignRight().Text($"Tipo de Ambiente: {_d.TipoAmbiente()}").FontSize(6).FontFamily("Microsoft Sans Serif");
            });

            row.ConstantItem(70).AlignMiddle().AlignCenter().Element(GerarQrCode);
        });
    }

    private void GerarQrCode(IContainer container)
    {
        var url = $"https://www.nfse.gov.br/ConsultaPublica/?tpc=1&chave={_d.ChaveAcesso()}";
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var pngBytes = new PngByteQRCode(qrCodeData).GetGraphic(6);

        container.Column(col =>
        {
            col.Item().Height(55).Width(55).Image(pngBytes).FitArea();
            col.Item().PaddingTop(2).Text(
                "A autenticidade desta NFS-e pode ser verificada pela leitura deste código QR ou pela consulta da chave de acesso no portal nacional da NFS-e")
                .FontSize(5).FontFamily("Microsoft Sans Serif");
        });
    }

    private void DadosIdentificacao(IContainer container)
    {
        BlocoComTitulo(container, "DADOS DA NFS-e", corpo =>
        {
            LinhaCampos(corpo, ("Chave de Acesso da NFS-e", _d.ChaveAcesso()));
            LinhaCampos(corpo,
                ("Número da NFS-e", _d.NumeroNfse()),
                ("Competência da NFS-e", _d.Competencia()),
                ("Data e Hora da Emissão da NFS-e", _d.DataHoraProcessamento()));
            LinhaCampos(corpo,
                ("Número da DPS", _d.NumeroDps()),
                ("Série da DPS", _d.SerieDps()),
                ("Data e Hora da Emissão da DPS", _d.DataHoraEmissaoDps()));
            LinhaCampos(corpo,
                ("Emitente da NFS-e", _d.TipoEmitente()),
                ("Situação da NFS-e", _d.Situacao()),
                ("Finalidade", _d.Finalidade()));
        });
    }

    private void BlocoPrestador(IContainer container)
    {
        BlocoComTitulo(container, "PRESTADOR / FORNECEDOR", corpo =>
        {
            LinhaCampos(corpo,
                ("CNPJ / CPF / NIF", _d.PrestadorDocumento()),
                ("Indicador Municipal (Inscrição)", _d.PrestadorInscricaoMunicipal()),
                ("Telefone", _d.PrestadorTelefone()));
            LinhaCampos(corpo,
                ("Nome / Nome Empresarial", _d.PrestadorNome()),
                ("Município / Sigla UF", _d.PrestadorMunicipioUf()));
            LinhaCampos(corpo,
                ("Endereço", _d.PrestadorEndereco()),
                ("Email", _d.PrestadorEmail()));
            LinhaCampos(corpo,
                ("Simples Nacional na Data de Competência", _d.PrestadorOpSimpNac()),
                ("Regime de Apuração Tributária pelo SN", _d.PrestadorRegApTribSN()));
        });
    }

    private void BlocoTomador(IContainer container)
    {
        BlocoComTitulo(container, "TOMADOR / ADQUIRENTE", corpo =>
        {
            // Nota 2 da NT 008: bloco pode ser suprimido, informando só a frase abaixo.
            if (!_d.TomadorExiste())
            {
                corpo.Item().Text("TOMADOR/ADQUIRENTE DA OPERAÇÃO NÃO IDENTIFICADO NA NFS-e").FontSize(7);
                return;
            }

            LinhaCampos(corpo,
                ("CNPJ / CPF / NIF", _d.TomadorDocumento()),
                ("Indicador Municipal (Inscrição)", _d.TomadorInscricaoMunicipal()),
                ("Telefone", _d.TomadorTelefone()));
            LinhaCampos(corpo,
                ("Nome / Nome Empresarial", _d.TomadorNome()),
                ("Município / Sigla UF", _d.TomadorMunicipioUf()));
            LinhaCampos(corpo,
                ("Endereço", _d.TomadorEndereco()),
                ("E-mail", _d.TomadorEmail()));
        });
    }

    /// <summary>
    /// Este SaaS não coleta um "destinatário da operação" distinto do
    /// tomador — não há esse conceito na tela de emissão. Nota 3 da NT 008
    /// cobre exatamente esse caso: informar a frase padrão em vez de
    /// duplicar os dados do tomador.
    /// </summary>
    private void BlocoDestinatario(IContainer container)
    {
        BlocoComTitulo(container, "DESTINATÁRIO DA OPERAÇÃO", corpo =>
            corpo.Item().Text("O DESTINATÁRIO É O PRÓPRIO TOMADOR/ADQUIRENTE DA OPERAÇÃO").FontSize(7));
    }

    private void BlocoServico(IContainer container)
    {
        BlocoComTitulo(container, "SERVIÇO PRESTADO", corpo =>
        {
            LinhaCampos(corpo,
                ("Código de Tributação Nacional / Municipal", _d.CodigoTributacaoNacionalMunicipal()),
                ("Código da NBS", _d.CodigoNbs()),
                ("Local da Prestação / Sigla UF / País", _d.LocalPrestacao()));
            corpo.Item().Text(_d.DescricaoTribNacionalMunicipal()).FontSize(7);
            corpo.Item().PaddingTop(2).Column(col =>
            {
                col.Item().Text("Descrição do Serviço").FontSize(6).Bold();
                col.Item().Text(_d.DescricaoServico()).FontSize(7);
            });
        });
    }

    private void BlocoIssqn(IContainer container)
    {
        BlocoComTitulo(container, "TRIBUTAÇÃO MUNICIPAL (ISSQN)", corpo =>
        {
            // Nota 4 da NT 008: operação não sujeita ao ISSQN.
            if (!_d.IssqnAplicavel())
            {
                corpo.Item().Text("TRIBUTAÇÃO MUNICIPAL (ISSQN) - OPERAÇÃO NÃO SUJEITA AO ISSQN").FontSize(7);
                return;
            }

            LinhaCampos(corpo,
                ("Tipo de Tributação do ISSQN", _d.TipoTributacaoIssqn()),
                ("Município / Sigla UF / País da Incidência do ISSQN", _d.MunicipioIncidenciaIssqn()));
            LinhaCampos(corpo,
                ("BC ISSQN", _d.BaseCalculoIssqn()),
                ("Alíquota Aplicada", _d.AliquotaIssqn()),
                ("Retenção do ISSQN", _d.RetencaoIssqn()),
                ("ISSQN Apurado", _d.IssqnApurado()));
        });
    }

    private void BlocoFederal(IContainer container)
    {
        BlocoComTitulo(container, "TRIBUTAÇÃO FEDERAL (EXCETO CBS)", corpo =>
        {
            LinhaCampos(corpo,
                ("IRRF", _d.Irrf()),
                ("Contribuição Previdenciária - Retida", _d.ContribuicaoPrevidenciariaRetida()),
                ("Contribuições Sociais - Retidas", _d.ContribuicoesSociaisRetidas()));
            LinhaCampos(corpo,
                ("PIS - Débito Apuração Própria", _d.PisDebitoApuracaoPropria()),
                ("COFINS - Débito Apuração Própria", _d.CofinsDebitoApuracaoPropria()),
                ("Descrição Contrib. Sociais - Retidas", _d.DescricaoContribSociaisRetidas()));
        });
    }

    /// <summary>
    /// Confirmado contra um PDF oficial real (comparação direta, não só a
    /// nota técnica): o bloco IBS/CBS SEMPRE aparece, com "-" nos campos
    /// ausentes — eu tinha decidido omitir esse bloco inteiro antes de
    /// ver um exemplo real, e estava errado. Este projeto ainda não
    /// coleta o grupo IBSCBS na DPS (Reforma Tributária), então tudo sai
    /// "-" (ou "R$ 0,00" nos 2 campos de total, confirmado no PDF real)
    /// até que a emissão passe a preencher esse grupo.
    /// </summary>
    private void BlocoIbsCbs(IContainer container)
    {
        BlocoComTitulo(container, "TRIBUTAÇÃO IBS / CBS", corpo =>
        {
            LinhaCampos(corpo,
                ("CST / cClassTrib", _d.IbsCbsCstEClasseTrib()),
                ("Indicador de Operação / Código IBGE Incidência / Município Incidência / Sigla UF", _d.IbsCbsIndicadorOperacao()));
            LinhaCampos(corpo,
                ("Exclusões e Reduções da Base de Cálculo", _d.IbsCbsExclusoesReducoesBc()),
                ("Base de Cálculo Após Exclusões e Reduções", _d.IbsCbsBaseCalculo()),
                ("Red. Alíquota IBS / Red. Alíquota CBS", _d.IbsCbsReducaoAliquota()),
                ("Alíquota - IBS UF / IBS Mun", _d.IbsCbsAliquotaUfMun()));
            LinhaCampos(corpo,
                ("Alíq. Efetiva Municipal - IBS", _d.IbsCbsAliquotaEfetivaMunicipal()),
                ("Valor Apurado Municipal - IBS", _d.IbsCbsValorApuradoMunicipal()),
                ("Alíq. Efetiva Estadual - IBS", _d.IbsCbsAliquotaEfetivaEstadual()),
                ("Valor Apurado Estadual - IBS", _d.IbsCbsValorApuradoEstadual()));
            LinhaCampos(corpo,
                ("Valor Total Apurado - IBS", _d.IbsCbsValorTotalApuradoIbs()),
                ("Alíquota - CBS", _d.IbsCbsAliquota()),
                ("Alíquota Efetiva - CBS", _d.IbsCbsAliquotaEfetiva()),
                ("Valor Total Apurado - CBS", _d.IbsCbsValorTotalApuradoCbs()));
        });
    }

    private void BlocoValorTotal(IContainer container)
    {
        container.Border(1).Padding(3).Background(CorSombreamento).Row(row =>
        {
            void Campo(string rotulo, string valor) => row.RelativeItem().Column(col =>
            {
                col.Item().Text(rotulo).FontSize(6).Bold();
                col.Item().Text(valor == "-" ? valor : $"R$ {valor}").FontSize(7);
            });

            Campo("Valor da Operação / Serviço", _d.ValorOperacaoServico());
            Campo("Desconto Incondicionado", _d.DescontoIncondicionado());
            Campo("Desconto Condicionado", _d.DescontoCondicionado());
            Campo("Total das Retenções (ISSQN / Federais)", _d.TotalRetencoes());
            Campo("Valor Líquido da NFS-e", _d.ValorLiquido());
            Campo("Total do IBS/CBS", _d.TotalIbsCbs());
            Campo("Valor Líquido da NFS-e + IBS/CBS", _d.ValorLiquidoMaisIbsCbs());
        });
    }

    private void BlocoInformacoesComplementares(IContainer container)
    {
        BlocoComTitulo(container, "INFORMAÇÕES COMPLEMENTARES", corpo =>
        {
            var partes = new List<string>();

            if (!string.IsNullOrWhiteSpace(_d.ChaveNfseSubstituida()))
                partes.Add($"NFS-e Subst.: {_d.ChaveNfseSubstituida()}");

            // Totais Aproximados dos Tributos é obrigatório (nota 10).
            partes.Add(_d.TotaisAproximadosTributos());

            corpo.Item().Text(string.Join(" | ", partes)).FontSize(7);
        });
    }

    private void Canhoto(IContainer container)
    {
        BlocoComTitulo(container, "CANHOTO", corpo =>
        {
            LinhaCampos(corpo,
                ("Data Cientificação", "-"),
                ("Identificação e Assinatura", "-"),
                ("Nº NFS-e / Chave NFS-e", $"{_d.NumeroNfse()} / {_d.ChaveAcesso()}"));
        });
    }

    // ---- Helpers de layout ----

    private static void BlocoComTitulo(IContainer container, string titulo, Action<ColumnDescriptor> corpo)
    {
        container.Border(1).Column(col =>
        {
            col.Item().Background(CorSombreamento).Padding(2)
                .Text(titulo).Bold().FontSize(7);
            col.Item().Padding(3).Column(corpo);
        });
    }

    private static void LinhaCampos(ColumnDescriptor coluna, params (string Rotulo, string Valor)[] campos)
    {
        coluna.Item().PaddingBottom(2).Row(row =>
        {
            foreach (var (rotulo, valor) in campos)
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(rotulo).FontSize(6).Bold().FontColor(Colors.Grey.Darken1);
                    col.Item().Text(valor).FontSize(7).FontFamily("Microsoft Sans Serif");
                });
            }
        });
    }
}
