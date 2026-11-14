using System.Globalization;
using System.Text.RegularExpressions;
using NfseSaaS.Application.UseCases.Municipios;
using NfseSaaS.Application.Validation;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Importacao;

/// <summary>Valor lido de uma célula: o valor (nulo = célula vazia) ou o motivo de não ter entendido.</summary>
public readonly record struct Lido<T>(T? Valor, string? Erro)
{
    public bool Ok => Erro is null;

    public static Lido<T> Vazio => new(default, null);

    public static Lido<T> De(T valor) => new(valor, null);

    public static Lido<T> Falha(string erro) => new(default, erro);
}

/// <summary>
/// IM1: regras puras da importação por planilha. Aceitam o que as pessoas
/// costumam ter no Excel: CPF que perdeu o zero da frente, "R$ 1.500,00",
/// "1,5%", data como texto ou como data, "Sim"/"X"/"S".
/// </summary>
public static partial class RegrasImportacao
{
    public const int MaximoLinhas = 2000;
    public const long TamanhoMaximoArquivo = 5 * 1024 * 1024;

    /// <summary>Consultas à Receita (BrasilAPI) por importação, para completar nome e endereço de CNPJ.</summary>
    public const int MaximoConsultasCnpj = 100;

    public const string MensagemArquivoGrande = "A planilha passa de 5 MB. Divida em arquivos menores.";
    public static string MensagemMuitasLinhas(int linhas) =>
        $"A planilha tem {linhas} linhas preenchidas; o máximo por vez é {MaximoLinhas}. Divida em arquivos menores.";

    // ---------- Documento ----------

    /// <summary>
    /// CPF/CNPJ só com letras e números, em maiúsculas. Número que perdeu os
    /// zeros da frente no Excel (célula numérica ou CSV) volta com eles: até
    /// 11 posições tenta CPF; até 14, CNPJ. Fica com o que tiver dígito certo.
    /// </summary>
    public static Lido<string> LerDocumento(CelulaLida celula)
    {
        string bruto;
        if (celula.Numero is { } numero && numero == decimal.Truncate(numero) && numero > 0)
            bruto = numero.ToString("0", CultureInfo.InvariantCulture);
        else
            bruto = celula.Texto ?? string.Empty;

        var limpo = new string(bruto.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (limpo.Length == 0)
            return Lido<string>.Vazio;

        if (limpo.All(char.IsDigit) && limpo.Length < 14)
        {
            if (limpo.Length <= 11)
            {
                var cpf = limpo.PadLeft(11, '0');
                if (DocumentoFiscalValidator.CpfValido(cpf))
                    return Lido<string>.De(cpf);
            }
            var cnpj = limpo.PadLeft(14, '0');
            if (DocumentoFiscalValidator.CnpjValido(cnpj))
                return Lido<string>.De(cnpj);
            return Lido<string>.Falha($"CPF/CNPJ inválido: {bruto.Trim()}.");
        }

        return DocumentoFiscalValidator.CpfOuCnpjValido(limpo)
            ? Lido<string>.De(limpo)
            : Lido<string>.Falha($"CPF/CNPJ inválido: {bruto.Trim()}.");
    }

    /// <summary>"12345678000195" → "12.345.678/0001-95"; CPF → "123.456.789-09".</summary>
    public static string FormatarDocumento(string? documento)
    {
        var d = documento ?? string.Empty;
        return d.Length switch
        {
            11 => $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}",
            14 => $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}",
            _ => d
        };
    }

    // ---------- Texto ----------

    public static string? LerTexto(CelulaLida celula, int tamanhoMaximo = int.MaxValue)
    {
        string texto;
        if (string.IsNullOrWhiteSpace(celula.Texto) && celula.Numero is { } numero)
            texto = numero.ToString(CultureInfo.InvariantCulture);
        else
            texto = celula.Texto ?? string.Empty;

        texto = EspacosRepetidos().Replace(texto.Trim(), " ");
        if (texto.Length == 0)
            return null;
        return texto.Length > tamanhoMaximo ? texto[..tamanhoMaximo] : texto;
    }

    /// <summary>Só os números (telefone, CEP, IBGE): "(71) 9 9999-0000" → "71999990000". Célula numérica vale pelo número.</summary>
    public static string? LerSoNumeros(CelulaLida celula)
    {
        var texto = celula.Numero is { } n && n == decimal.Truncate(n) && n >= 0
            ? n.ToString("0", CultureInfo.InvariantCulture)
            : celula.Texto ?? string.Empty;
        var digitos = new string(texto.Where(char.IsDigit).ToArray());
        return digitos.Length == 0 ? null : digitos;
    }

    /// <summary>CEP com 8 números; o que perdeu o zero da frente (01310-100 → 1310100) volta com ele.</summary>
    public static Lido<string> LerCep(CelulaLida celula)
    {
        var digitos = LerSoNumeros(celula);
        if (digitos is null)
            return Lido<string>.Vazio;
        if (digitos.Length is 7 or 8)
            return Lido<string>.De(digitos.PadLeft(8, '0'));
        return Lido<string>.Falha($"CEP inválido: {celula.Texto?.Trim()} (são 8 números).");
    }

    /// <summary>Código IBGE de 7 números.</summary>
    public static Lido<string> LerCodigoMunicipio(CelulaLida celula)
    {
        var digitos = LerSoNumeros(celula);
        if (digitos is null)
            return Lido<string>.Vazio;
        return digitos.Length == 7
            ? Lido<string>.De(digitos)
            : Lido<string>.Falha($"Código IBGE do município inválido: {celula.Texto?.Trim()} (são 7 números).");
    }

    public static Lido<string> LerUf(CelulaLida celula)
    {
        var texto = LerTexto(celula);
        if (texto is null)
            return Lido<string>.Vazio;
        var uf = texto.ToUpperInvariant();
        return uf.Length == 2 && uf.All(c => c is >= 'A' and <= 'Z')
            ? Lido<string>.De(uf)
            : Lido<string>.Falha($"UF inválida: {texto} (use a sigla de 2 letras, como BA).");
    }

    /// <summary>"a@x.com; b@x.com, c@x.com" → lista (sem repetidos).</summary>
    public static IReadOnlyList<string> LerEmails(CelulaLida celula)
    {
        var texto = celula.Texto ?? string.Empty;
        return texto
            .Split([';', ',', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => e.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ---------- Números ----------

    /// <summary>
    /// "R$ 1.234,56", "1234,56", "1234.56", "1.234" (milhar) ou célula
    /// numérica. Com ponto e vírgula juntos, o último é o decimal.
    /// </summary>
    public static Lido<decimal?> LerDecimal(CelulaLida celula)
    {
        if (celula.Numero is { } numero)
            return Lido<decimal?>.De(numero);

        var texto = (celula.Texto ?? string.Empty)
            .Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("%", string.Empty)
            .Replace(" ", string.Empty)
            .Replace(" ", string.Empty)
            .Trim();
        if (texto.Length == 0)
            return Lido<decimal?>.Vazio;

        var negativo = texto.StartsWith('-') || (texto.StartsWith('(') && texto.EndsWith(')'));
        texto = texto.Trim('-', '(', ')');

        var ponto = texto.LastIndexOf('.');
        var virgula = texto.LastIndexOf(',');
        string normalizado;
        if (ponto >= 0 && virgula >= 0)
            normalizado = virgula > ponto
                ? texto.Replace(".", string.Empty).Replace(',', '.')
                : texto.Replace(",", string.Empty);
        else if (virgula >= 0)
            normalizado = texto.Count(c => c == ',') > 1 ? texto.Replace(",", string.Empty) : texto.Replace(',', '.');
        else if (ponto >= 0 && MilharComPonto().IsMatch(texto))
            normalizado = texto.Replace(".", string.Empty);
        else
            normalizado = texto;

        if (!decimal.TryParse(normalizado, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var valor))
            return Lido<decimal?>.Falha($"Número inválido: {celula.Texto?.Trim()}.");
        return Lido<decimal?>.De(negativo ? -valor : valor);
    }

    /// <summary>Percentual: "1,5", "1,5%" ou a célula de porcentagem do Excel (0,015 = 1,5%).</summary>
    public static Lido<decimal?> LerPercentual(CelulaLida celula)
    {
        var lido = LerDecimal(celula);
        if (!lido.Ok || lido.Valor is not { } valor)
            return lido;
        if (celula.EhPercentual)
            valor *= 100m;
        valor = decimal.Round(valor, 4);
        if (valor < 0 || valor > 100)
            return Lido<decimal?>.Falha($"Percentual fora de 0 a 100: {celula.Texto?.Trim()}.");
        return Lido<decimal?>.De(valor);
    }

    public static Lido<int?> LerInteiro(CelulaLida celula)
    {
        var lido = LerDecimal(celula);
        if (!lido.Ok)
            return Lido<int?>.Falha(lido.Erro!);
        if (lido.Valor is not { } valor)
            return Lido<int?>.Vazio;
        if (valor != decimal.Truncate(valor) || valor < int.MinValue || valor > int.MaxValue)
            return Lido<int?>.Falha($"Número inteiro inválido: {celula.Texto?.Trim()}.");
        return Lido<int?>.De((int)valor);
    }

    // ---------- Datas ----------

    private static readonly string[] FormatosData =
    [
        "d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "dd/MM/yy", "d-M-yyyy", "dd-MM-yyyy", "d.M.yyyy", "dd.MM.yyyy",
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss"
    ];

    /// <summary>Data como data do Excel, número de série do Excel ou texto (dd/mm/aaaa, aaaa-mm-dd…).</summary>
    public static Lido<DateOnly?> LerData(CelulaLida celula)
    {
        if (celula.Data is { } data)
            return Lido<DateOnly?>.De(DateOnly.FromDateTime(data));

        var texto = (celula.Texto ?? string.Empty).Trim();
        if (texto.Length == 0 && celula.Numero is null)
            return Lido<DateOnly?>.Vazio;

        if (DateTime.TryParseExact(texto, FormatosData, CultureInfo.InvariantCulture, DateTimeStyles.None, out var lida))
            return Lido<DateOnly?>.De(DateOnly.FromDateTime(lida));

        // Data que virou número (célula "Geral" no Excel): 45658 = 01/01/2025.
        if (celula.Numero is { } serie && serie == decimal.Truncate(serie) && serie is > 10000 and < 80000)
            return Lido<DateOnly?>.De(DateOnly.FromDateTime(DateTime.FromOADate((double)serie)));

        return Lido<DateOnly?>.Falha($"Data inválida: {texto} (use dd/mm/aaaa).");
    }

    // ---------- Escolhas ----------

    private static string Chave(CelulaLida celula) => ColunasImportacao.ChaveCabecalho(celula.Texto);

    /// <summary>Sim/Não, S/N, X, Verdadeiro/Falso, 1/0. Vazio = nulo.</summary>
    public static Lido<bool?> LerSimNao(CelulaLida celula)
    {
        if (celula.Numero is { } n && string.IsNullOrWhiteSpace(celula.Texto))
            return n == 1 ? Lido<bool?>.De(true) : n == 0 ? Lido<bool?>.De(false) : Lido<bool?>.Falha($"Use Sim ou Não (veio {n}).");

        return Chave(celula) switch
        {
            "" => Lido<bool?>.Vazio,
            "sim" or "s" or "x" or "verdadeiro" or "true" or "1" or "yes" or "y" or "v" => Lido<bool?>.De(true),
            "nao" or "n" or "falso" or "false" or "0" or "no" or "f" or "-" => Lido<bool?>.De(false),
            _ => Lido<bool?>.Falha($"Use Sim ou Não (veio \"{celula.Texto?.Trim()}\").")
        };
    }

    public static readonly IReadOnlyList<string> OpcoesSimNao = ["Sim", "Não"];

    public const string TextoEstaEmpresa = "Esta empresa";
    public const string TextoTodasEmpresas = "Todas as empresas";
    public static readonly IReadOnlyList<string> OpcoesDisponivelPara = [TextoEstaEmpresa, TextoTodasEmpresas];

    /// <summary>true = compartilhado ("Todas as empresas"); false = só esta empresa; nulo = padrão da conta.</summary>
    public static Lido<bool?> LerDisponivelPara(CelulaLida celula)
    {
        var chave = Chave(celula);
        if (chave.Length == 0)
            return Lido<bool?>.Vazio;
        if (chave.Contains("toda") || chave.Contains("compartilh") || chave is "sim")
            return Lido<bool?>.De(true);
        if (chave.Contains("esta") || chave.Contains("so") || chave.Contains("exclusiv") || chave is "nao")
            return Lido<bool?>.De(false);
        return Lido<bool?>.Falha($"\"Disponível para\": use \"{TextoEstaEmpresa}\" ou \"{TextoTodasEmpresas}\".");
    }

    public static readonly IReadOnlyList<string> OpcoesCobranca = ["Mensal", "Avulso"];

    public static Lido<TipoCobrancaContrato?> LerCobranca(CelulaLida celula) => Chave(celula) switch
    {
        "" => Lido<TipoCobrancaContrato?>.Vazio,
        "mensal" or "mensalidade" or "recorrente" or "mes" or "m" => Lido<TipoCobrancaContrato?>.De(TipoCobrancaContrato.Mensal),
        "avulso" or "avulsa" or "unica" or "unico" or "a" or "pontual" => Lido<TipoCobrancaContrato?>.De(TipoCobrancaContrato.Avulso),
        _ => Lido<TipoCobrancaContrato?>.Falha($"Cobrança: use Mensal ou Avulso (veio \"{celula.Texto?.Trim()}\").")
    };

    public static readonly IReadOnlyList<string> OpcoesIndice = ["IPCA", "IGP-M", "INCC", "INPC", "Selic", "CDI", "Outro"];

    public static Lido<IndiceReajusteContrato?> LerIndice(CelulaLida celula) => Chave(celula) switch
    {
        "" => Lido<IndiceReajusteContrato?>.Vazio,
        "ipca" or "ipcaibge" => Lido<IndiceReajusteContrato?>.De(IndiceReajusteContrato.Ipca),
        "igpm" or "igpmfgv" => Lido<IndiceReajusteContrato?>.De(IndiceReajusteContrato.Igpm),
        "incc" or "inccm" or "inccdi" => Lido<IndiceReajusteContrato?>.De(IndiceReajusteContrato.Incc),
        "inpc" => Lido<IndiceReajusteContrato?>.De(IndiceReajusteContrato.Inpc),
        "selic" => Lido<IndiceReajusteContrato?>.De(IndiceReajusteContrato.Selic),
        "cdi" => Lido<IndiceReajusteContrato?>.De(IndiceReajusteContrato.Cdi),
        "outro" or "outros" or "nenhum" or "semindice" or "livre" or "negociado" => Lido<IndiceReajusteContrato?>.De(IndiceReajusteContrato.Outro),
        _ => Lido<IndiceReajusteContrato?>.Falha($"Índice de reajuste desconhecido: \"{celula.Texto?.Trim()}\" (use {string.Join(", ", OpcoesIndice)}).")
    };

    public static string TextoIndice(IndiceReajusteContrato indice) => indice switch
    {
        IndiceReajusteContrato.Ipca => "IPCA",
        IndiceReajusteContrato.Igpm => "IGP-M",
        IndiceReajusteContrato.Incc => "INCC",
        IndiceReajusteContrato.Inpc => "INPC",
        IndiceReajusteContrato.Selic => "Selic",
        IndiceReajusteContrato.Cdi => "CDI",
        _ => "Outro"
    };

    public static readonly IReadOnlyList<string> OpcoesSituacao = ["Ativo", "Suspenso", "Encerrado", "Cancelado", "Rascunho"];

    public static Lido<StatusContrato?> LerSituacao(CelulaLida celula) => Chave(celula) switch
    {
        "" => Lido<StatusContrato?>.Vazio,
        "ativo" or "ativa" or "vigente" or "emvigor" => Lido<StatusContrato?>.De(StatusContrato.Ativo),
        "suspenso" or "suspensa" or "pausado" or "pausada" => Lido<StatusContrato?>.De(StatusContrato.Suspenso),
        "encerrado" or "encerrada" or "finalizado" or "finalizada" or "concluido" or "concluida" => Lido<StatusContrato?>.De(StatusContrato.Encerrado),
        "cancelado" or "cancelada" or "inativo" or "inativa" => Lido<StatusContrato?>.De(StatusContrato.Cancelado),
        "rascunho" => Lido<StatusContrato?>.De(StatusContrato.Rascunho),
        _ => Lido<StatusContrato?>.Falha($"Situação desconhecida: \"{celula.Texto?.Trim()}\" (use {string.Join(", ", OpcoesSituacao)}).")
    };

    // ---------- Contratos ----------

    /// <summary>
    /// Contrato mensal vindo de outro sistema sem a data do último reajuste:
    /// o valor da planilha é o de hoje, então consideramos o reajuste feito no
    /// último aniversário que já passou (início + N × periodicidade ≤ hoje).
    /// Sem isso, um contrato de 2023 entraria com três reajustes "pendentes".
    /// Nulo = ainda não fez aniversário (nada a assumir).
    /// </summary>
    public static DateOnly? UltimoAniversario(DateOnly inicio, int periodicidadeMeses, DateOnly hoje)
    {
        if (periodicidadeMeses <= 0 || inicio > hoje)
            return null;

        DateOnly? ultimo = null;
        for (var n = 1; n <= 1200; n++)
        {
            var aniversario = inicio.AddMonths(n * periodicidadeMeses);
            if (aniversario > hoje)
                break;
            ultimo = aniversario;
        }
        return ultimo;
    }

    /// <summary>Chave para achar o serviço pela descrição: sem acento, maiúscula ou espaço repetido.</summary>
    public static string ChaveServico(string? descricao) => RegrasMunicipio.NormalizarBusca(descricao);

    // ---------- Apelido ----------

    /// <summary>
    /// Candidatos de apelido a partir de um texto (nome fantasia ou nome), do
    /// mais natural ao mais garantido: o texto; o texto + 6 últimos números do
    /// documento; o mesmo com contador. Todos cabem no tamanho máximo.
    /// </summary>
    public static IEnumerable<string> CandidatosApelido(string baseApelido, string documento, int tamanhoMaximo)
    {
        var texto = string.IsNullOrWhiteSpace(baseApelido) ? documento : EspacosRepetidos().Replace(baseApelido.Trim(), " ");
        yield return Cortar(texto, tamanhoMaximo).TrimEnd();

        var final = documento.Length > 6 ? documento[^6..] : documento;
        for (var tentativa = 1; tentativa <= 99; tentativa++)
        {
            var sufixo = tentativa == 1 ? $" {final}" : $" {final}-{tentativa}";
            yield return Cortar(texto, tamanhoMaximo - sufixo.Length).TrimEnd() + sufixo;
        }
    }

    private static string Cortar(string texto, int tamanho) =>
        tamanho <= 0 ? string.Empty : texto.Length <= tamanho ? texto : texto[..tamanho];

    // ---------- Município ----------

    private static readonly HashSet<string> Ufs = new(StringComparer.OrdinalIgnoreCase)
    {
        "AC", "AL", "AM", "AP", "BA", "CE", "DF", "ES", "GO", "MA", "MG", "MS", "MT", "PA", "PB", "PE",
        "PI", "PR", "RJ", "RN", "RO", "RR", "RS", "SC", "SE", "SP", "TO"
    };

    /// <summary>
    /// "Salvador/BA", "Salvador - BA", "Salvador BA" ou "Salvador" + coluna UF
    /// → ("Salvador", "BA"). A UF da coluna vence a do texto.
    /// </summary>
    public static (string Nome, string? Uf) SepararMunicipio(string? municipio, string? uf)
    {
        var texto = (municipio ?? string.Empty).Trim();
        string? ufDoTexto = null;
        var m = MunicipioComUf().Match(texto);
        if (m.Success && Ufs.Contains(m.Groups[2].Value))
        {
            texto = m.Groups[1].Value.Trim();
            ufDoTexto = m.Groups[2].Value.ToUpperInvariant();
        }
        return (texto, uf ?? ufDoTexto);
    }

    /// <summary>Chave para comparar nomes de município: sem acento, espaço, hífen ou apóstrofo ("Embu-Guaçu" = "embu guacu").</summary>
    public static string ChaveMunicipio(string? nome) => ColunasImportacao.ChaveCabecalho(nome);

    [GeneratedRegex(@"^(.*?)[\s/\-,(]+([A-Za-z]{2})\)?$")]
    private static partial Regex MunicipioComUf();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex EspacosRepetidos();

    [GeneratedRegex(@"^\d{1,3}(\.\d{3})+$")]
    private static partial Regex MilharComPonto();
}
