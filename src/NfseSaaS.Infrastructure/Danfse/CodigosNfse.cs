namespace NfseSaaS.Infrastructure.Danfse;

/// <summary>
/// A NT 008/2026 exige que campos codificados (opSimpNac, tribISSQN etc.)
/// sejam impressos por EXTENSO ("Utilizar a descrição destas opções"), não
/// pelo código numérico crú. As descrições abaixo seguem a nomenclatura
/// já usada nos manuais/leiaute da NFS-e Nacional — ainda assim, ESTA É
/// A PARTE DESTE ARQUIVO MAIS SUJEITA A REVISÃO: o texto exato de cada
/// enumeração está definido no XSD/dicionário de dados oficial da NFS-e
/// Nacional, que não foi lido campo a campo na implementação deste
/// gerador. Antes de usar em produção, confira as descrições abaixo
/// contra a Documentação Técnica (Portal da NFS-e Nacional) — ver
/// comentário completo em <see cref="DanfsePdfGenerator"/>.
/// </summary>
internal static class CodigosNfse
{
    public static string DescricaoOpSimpNac(string? codigo) => codigo switch
    {
        "1" => "Não Optante",
        "2" => "Optante - Microempreendedor Individual (MEI)",
        "3" => "Optante - Microempresa ou Empresa de Pequeno Porte (Simples Nacional)",
        _ => "-"
    };

    public static string DescricaoRegApTribSN(string? codigo) => codigo switch
    {
        "1" => "Regime de apuração dos tributos federais e municipal pelo Simples Nacional",
        "2" => "Regime de apuração dos tributos federais pelo Simples Nacional e municipal fora do Simples Nacional",
        "3" => "Regime de apuração dos tributos federais e municipal fora do Simples Nacional",
        _ => "-"
    };

    public static string DescricaoRegEspTrib(string? codigo) => codigo switch
    {
        "0" => "Nenhum",
        "1" => "Estimativa",
        "2" => "Sociedade de Profissionais",
        "3" => "Cooperativa",
        "4" => "Microempresário Individual (MEI)",
        "5" => "Microempresário e Empresa de Pequeno Porte (ME/EPP)",
        "6" => "Profissional Autônomo",
        "9" => "Outros",
        _ => "-"
    };

    public static string DescricaoTribIssqn(string? codigo) => codigo switch
    {
        "1" => "Operação Tributável",
        "2" => "Imune",
        "3" => "Exportação de Serviço",
        "4" => "Não Incidência",
        _ => "-"
    };

    public static string DescricaoTpRetIssqn(string? codigo) => codigo switch
    {
        "1" => "Não Retido",
        "2" => "Retido pelo Tomador",
        "3" => "Retido pelo Intermediário",
        _ => "-"
    };

    public static string DescricaoTpRetPisCofins(string? codigo)
    {
        var texto = codigo switch
        {
            "0" => "PIS/COFINS/CSLL Não Retidos",
            "1" => "PIS/COFINS/CSLL Retido",
            _ => null
        };
        return texto is null ? "-" : $"{codigo} - {texto}";
    }

    public static string DescricaoTpEmit(string? codigo) => codigo switch
    {
        "1" => "Prestador",
        "2" => "Tomador",
        "3" => "Intermediário",
        _ => "-"
    };

    /// <summary>cStat — situação da NFS-e. Descrição truncada em 37 caracteres, conforme item 2.4.5.</summary>
    public static string DescricaoCStat(string? codigo)
    {
        var descricao = codigo switch
        {
            "100" => "NFS-e Gerada",
            "101" => "NFS-e Cancelada",
            "102" => "NFS-e Substituída",
            _ => codigo ?? "-"
        };
        return Truncar(descricao, 37);
    }

    /// <summary>
    /// finNFSe não existe no XML pra NFS-e emitidas fora do escopo da
    /// Reforma Tributária (grupo IBSCBS ainda não coletado por este
    /// projeto) — confirmado no XML real: quando ausente, o PDF oficial
    /// mostra "-", não um texto padrão "NFS-e regular" inventado.
    /// </summary>
    public static string DescricaoFinNfse(string? codigo) => codigo switch
    {
        "0" => "NFS-e regular",
        "1" => "NFS-e de ajuste",
        _ => "-"
    };

    /// <summary>
    /// Os 2 primeiros dígitos de qualquer código de município do IBGE (7
    /// dígitos) identificam a UF — tabela pequena e estável, meio-termo
    /// razoável sem embarcar a tabela completa de ~5570 municípios (que
    /// daria o NOME do município, não só a UF). Ver nota em DanfseXmlDados
    /// sobre essa limitação.
    /// </summary>
    public static string UfPorCodigoIbge(string? codigoMunicipio)
    {
        if (string.IsNullOrWhiteSpace(codigoMunicipio) || codigoMunicipio.Length < 2)
            return "-";

        return codigoMunicipio[..2] switch
        {
            "11" => "RO", "12" => "AC", "13" => "AM", "14" => "RR", "15" => "PA", "16" => "AP", "17" => "TO",
            "21" => "MA", "22" => "PI", "23" => "CE", "24" => "RN", "25" => "PB", "26" => "PE", "27" => "AL", "28" => "SE", "29" => "BA",
            "31" => "MG", "32" => "ES", "33" => "RJ", "35" => "SP",
            "41" => "PR", "42" => "SC", "43" => "RS",
            "50" => "MS", "51" => "MT", "52" => "GO", "53" => "DF",
            _ => "-"
        };
    }

    public static string Truncar(string? valor, int tamanhoMaximo)
    {
        if (string.IsNullOrEmpty(valor))
            return "-";

        return valor.Length > tamanhoMaximo ? valor[..tamanhoMaximo] + "..." : valor;
    }
}
