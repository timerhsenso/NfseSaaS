using System.Text.RegularExpressions;
using NfseSaaS.Nacional.Models;

namespace NfseSaaS.Nacional.Validation;

/// <summary>
/// Validações locais da DPS antes do envio à SEFIN — checagens estruturais
/// e de dígito verificador que NÃO dependem de tabela oficial (município,
/// NBS, código de tributação municipal específico). Propositalmente NÃO
/// valida contra tabelas externas que este módulo não tem: se a SEFIN
/// rejeitar um cTribNac/NBS/município inválido, ela já retorna erro próprio
/// (E00xx), tratado via NfseValidationException em NfseNacionalService. Não
/// inventar regra fiscal aqui além do que está documentado nos comentários
/// abaixo — cada checagem tem a justificativa ao lado.
///
/// Duplica o algoritmo de dígito verificador de CPF/CNPJ que já existe em
/// NfseSaaS.Application.Validation.DocumentoFiscalValidator (mesma lógica,
/// mesmo suporte a CNPJ alfanumérico da IN RFB 2.229/2024) — de propósito,
/// não referenciado: NfseSaaS.Nacional não conhece Application/Domain/EF/
/// ASP.NET (módulo isolado, ver NfseSaaS.Nacional.csproj sem ProjectReference).
/// </summary>
public sealed class DpsValidator : IDpsValidator
{
    private static readonly Regex FormatoCnpj = new(@"^[0-9A-Z]{12}[0-9]{2}$", RegexOptions.Compiled);
    private static readonly Regex ApenasDigitos = new(@"^\d+$", RegexOptions.Compiled);

    public IReadOnlyCollection<string> Validar(DpsRequest request)
    {
        var erros = new List<string>();

        ValidarPrestador(request.Prestador, erros);
        ValidarTomador(request.Tomador, erros);
        ValidarTributacao(request.Prestador, request.Tributacao, erros);
        ValidarDadosDaDps(request, erros);

        return erros;
    }

    private static void ValidarPrestador(PrestadorDps prestador, List<string> erros)
    {
        if (!CnpjValido(prestador.Cnpj))
            erros.Add($"CNPJ do prestador inválido: '{prestador.Cnpj}'.");

        // CodigoMunicipio (IBGE) tem sempre 7 dígitos — não valida se o
        // código EXISTE na tabela do IBGE (este módulo não tem a tabela),
        // só o formato mínimo antes de gastar uma chamada à SEFIN.
        if (!ApenasDigitos.IsMatch(prestador.CodigoMunicipio) || prestador.CodigoMunicipio.Length != 7)
            erros.Add($"CodigoMunicipio do prestador deve ter 7 dígitos numéricos (IBGE): '{prestador.CodigoMunicipio}'.");

        // "1" Não optante, "2" Optante MEI, "3" Optante Simples Nacional
        // exceto MEI — únicos 3 valores documentados no cadastro de Empresa
        // (ver comentário de Empresa.OpSimpNac). Qualquer outro valor é erro
        // de cadastro, não algo que a SEFIN vá aceitar.
        if (prestador.OpSimpNac is not ("1" or "2" or "3"))
            erros.Add($"OpSimpNac do prestador deve ser '1', '2' ou '3': '{prestador.OpSimpNac}'.");
    }

    private static void ValidarTomador(TomadorDps tomador, List<string> erros)
    {
        if (!CpfOuCnpjValido(tomador.CnpjOuCpf))
            erros.Add($"CPF/CNPJ do tomador inválido: '{tomador.CnpjOuCpf}'.");

        if (string.IsNullOrWhiteSpace(tomador.Nome))
            erros.Add("Nome do tomador é obrigatório.");

        if (!ApenasDigitos.IsMatch(tomador.CodigoMunicipio) || tomador.CodigoMunicipio.Length != 7)
            erros.Add($"CodigoMunicipio do tomador deve ter 7 dígitos numéricos (IBGE): '{tomador.CodigoMunicipio}'.");

        if (!ApenasDigitos.IsMatch(tomador.Cep) || tomador.Cep.Length != 8)
            erros.Add($"CEP do tomador deve ter 8 dígitos numéricos: '{tomador.Cep}'.");

        if (string.IsNullOrWhiteSpace(tomador.Logradouro))
            erros.Add("Logradouro do tomador é obrigatório.");

        if (string.IsNullOrWhiteSpace(tomador.Bairro))
            erros.Add("Bairro do tomador é obrigatório.");
    }

    private static void ValidarTributacao(PrestadorDps prestador, TributacaoDps tributacao, List<string> erros)
    {
        // Os valores exatos permitidos para TribIssqn/TpRetIssqn/CstPisCofins/
        // TpRetPisCofins dependem da tabela oficial do layout da NFS-e
        // Nacional — este módulo só garante o TAMANHO do código (o mesmo
        // que já está fixado em NfseConfiguration), não o valor semântico.
        // Validar o valor semântico exige a tabela oficial, que ainda não
        // está disponível aqui — ver aviso no topo do arquivo.
        if (string.IsNullOrWhiteSpace(tributacao.TribIssqn) || tributacao.TribIssqn.Length != 1)
            erros.Add($"TribIssqn deve ter 1 caractere: '{tributacao.TribIssqn}'.");

        if (string.IsNullOrWhiteSpace(tributacao.TpRetIssqn) || tributacao.TpRetIssqn.Length != 1)
            erros.Add($"TpRetIssqn deve ter 1 caractere: '{tributacao.TpRetIssqn}'.");

        if (string.IsNullOrWhiteSpace(tributacao.CstPisCofins) || tributacao.CstPisCofins.Length != 2)
            erros.Add($"CstPisCofins deve ter 2 caracteres: '{tributacao.CstPisCofins}'.");

        if (string.IsNullOrWhiteSpace(tributacao.TpRetPisCofins) || tributacao.TpRetPisCofins.Length != 1)
            erros.Add($"TpRetPisCofins deve ter 1 caractere: '{tributacao.TpRetPisCofins}'.");

        // pTotTribSN só é exigido de fato quando a Empresa é optante pelo
        // Simples Nacional (OpSimpNac "2" MEI ou "3" SN exceto MEI) — para
        // "1" Não optante, o campo é irrelevante na prática, então só
        // valida o FORMATO (decimal 0-100) quando ele vem preenchido, sem
        // exigir presença pra Não optante.
        var percentual = tributacao.PercentualTotalTributosSimplesNacional;
        if (!string.IsNullOrWhiteSpace(percentual))
        {
            var formatoValido = decimal.TryParse(
                percentual,
                System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture,
                out var valor);

            if (!formatoValido || valor < 0 || valor > 100)
                erros.Add($"PercentualTotalTributosSimplesNacional deve ser um decimal entre 0 e 100 (formato '3.00'): '{percentual}'.");
        }
        else if (prestador.OpSimpNac is "2" or "3")
        {
            erros.Add("PercentualTotalTributosSimplesNacional é obrigatório para Empresa optante pelo Simples Nacional (OpSimpNac '2' ou '3').");
        }
    }

    private static void ValidarDadosDaDps(DpsRequest request, List<string> erros)
    {
        if (request.NumeroDps <= 0)
            erros.Add($"NumeroDps deve ser maior que zero: {request.NumeroDps}.");

        // Faixa 00001-49999 = emissão com aplicativo próprio (tpEmit=1) —
        // mesma faixa documentada em NfseSaaS.Nacional.Builders.DpsBuilder.
        // 50000-99999 é reservado para emissão pelo próprio portal da SEFIN
        // (tpEmit=2), que este SaaS não usa.
        if (!ApenasDigitos.IsMatch(request.SerieDps) || request.SerieDps.Length != 5)
            erros.Add($"SerieDps deve ter 5 dígitos numéricos: '{request.SerieDps}'.");
        else if (int.Parse(request.SerieDps) is < 1 or > 49999)
            erros.Add($"SerieDps '{request.SerieDps}' fora da faixa 00001-49999 (emissão com aplicativo próprio, tpEmit=1).");

        if (request.DataCompetencia > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            erros.Add($"DataCompetencia não pode estar no futuro: {request.DataCompetencia:yyyy-MM-dd}.");

        if (request.Valor <= 0)
            erros.Add($"Valor do serviço deve ser maior que zero: {request.Valor}.");

        // cTribNac (código de tributação nacional) tem 6 dígitos no layout
        // da NFS-e Nacional. Não valida se o código EXISTE na tabela
        // nacional nem se é compatível com o município do prestador — isso
        // exige a tabela oficial completa, fora do alcance deste módulo.
        if (!ApenasDigitos.IsMatch(request.CodigoTributacaoNacional) || request.CodigoTributacaoNacional.Length != 6)
            erros.Add($"CodigoTributacaoNacional deve ter 6 dígitos numéricos: '{request.CodigoTributacaoNacional}'.");

        // cNBS (Nomenclatura Brasileira de Serviços) tem 9 dígitos.
        if (!ApenasDigitos.IsMatch(request.CodigoNbs) || request.CodigoNbs.Length != 9)
            erros.Add($"CodigoNbs deve ter 9 dígitos numéricos: '{request.CodigoNbs}'.");

        if (string.IsNullOrWhiteSpace(request.DescricaoServico))
            erros.Add("DescricaoServico é obrigatória.");
        else if (request.DescricaoServico.Length > 2000)
            erros.Add($"DescricaoServico excede 2000 caracteres ({request.DescricaoServico.Length}).");
    }

    // --- CPF/CNPJ por dígito verificador — mesmo algoritmo de
    // NfseSaaS.Application.Validation.DocumentoFiscalValidator, duplicado
    // aqui deliberadamente (ver comentário no topo do arquivo). Qualquer
    // mudança na regra de negócio (ex.: nova IN da RFB) precisa ser
    // replicada nos dois lugares. ---

    private static bool CnpjValido(string? cnpj)
    {
        if (string.IsNullOrWhiteSpace(cnpj))
            return false;

        var caracteres = NormalizarAlfanumerico(cnpj);

        if (caracteres.Length != 14 || TodosCaracteresIguais(caracteres) || !FormatoCnpj.IsMatch(caracteres))
            return false;

        var multiplicador1 = new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        var multiplicador2 = new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };

        var primeiroDigito = CalcularDigitoVerificador(caracteres[..12], multiplicador1);
        var segundoDigito = CalcularDigitoVerificador(caracteres[..12] + primeiroDigito, multiplicador2);

        return caracteres.EndsWith($"{primeiroDigito}{segundoDigito}");
    }

    private static bool CpfValido(string? cpf)
    {
        if (string.IsNullOrWhiteSpace(cpf))
            return false;

        var digitos = SomenteDigitos(cpf);

        if (digitos.Length != 11 || TodosCaracteresIguais(digitos))
            return false;

        var multiplicador1 = new[] { 10, 9, 8, 7, 6, 5, 4, 3, 2 };
        var multiplicador2 = new[] { 11, 10, 9, 8, 7, 6, 5, 4, 3, 2 };

        var primeiroDigito = CalcularDigitoVerificador(digitos[..9], multiplicador1);
        var segundoDigito = CalcularDigitoVerificador(digitos[..9] + primeiroDigito, multiplicador2);

        return digitos.EndsWith($"{primeiroDigito}{segundoDigito}");
    }

    private static bool CpfOuCnpjValido(string? documento)
    {
        if (string.IsNullOrWhiteSpace(documento))
            return false;

        var caracteres = NormalizarAlfanumerico(documento);

        return caracteres.Length switch
        {
            11 => CpfValido(documento),
            14 => CnpjValido(documento),
            _ => false
        };
    }

    private static int CalcularDigitoVerificador(string baseCaracteres, int[] multiplicadores)
    {
        var soma = 0;
        for (var i = 0; i < multiplicadores.Length; i++)
            soma += (baseCaracteres[i] - '0') * multiplicadores[i]; // valor ASCII - 48; retrocompatível com dígito puro

        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }

    private static string NormalizarAlfanumerico(string valor) =>
        new(valor.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string SomenteDigitos(string valor) => new(valor.Where(char.IsDigit).ToArray());

    private static bool TodosCaracteresIguais(string caracteres) => caracteres.Distinct().Count() == 1;
}
