using NfseSaaS.Application.UseCases.Consultas;
using NfseSaaS.Application.Validation;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Cadastro;

/// <summary>
/// Regras do cadastro em etapas (Account/Registrar), sem banco nem rede:
/// - perfil Empresa: o documento é o CNPJ da empresa que emite (a conta
///   já nasce com essa Empresa cadastrada);
/// - perfil Contador: CPF (contador autônomo) ou CNPJ (escritório); as
///   Empresas são as dos clientes, cadastradas depois.
/// </summary>
public static class RegrasCadastroConta
{
    /// <summary>Só letras e dígitos, maiúsculas (CNPJ alfanumérico mantém as letras).</summary>
    public static string NormalizarDocumento(string? documento) =>
        new((documento ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>Mensagem de erro do documento para o perfil, ou nulo quando está certo.</summary>
    public static string? ErroDocumento(PerfilConta perfil, string? documento) => perfil switch
    {
        PerfilConta.Empresa => DocumentoFiscalValidator.CnpjValido(documento)
            ? null
            : "CNPJ inválido (confira os números).",
        PerfilConta.Contador => DocumentoFiscalValidator.CpfValido(documento) || DocumentoFiscalValidator.CnpjValido(documento)
            ? null
            : "CPF ou CNPJ inválido (confira os números).",
        _ => "Escolha se a conta é da sua empresa ou de contador."
    };

    /// <summary>
    /// Empresa com situação diferente de ATIVA na Receita (inapta, suspensa,
    /// baixada) não consegue emitir nota: o cadastro avisa e não segue.
    /// </summary>
    public static string? ErroSituacao(ConsultaCnpjResponse? consulta) =>
        consulta is { Ativa: false }
            ? $"A Receita Federal mostra este CNPJ como \"{consulta.SituacaoCadastral?.Trim()}\". Empresa nessa situação não emite nota fiscal."
            : null;
}

/// <summary>
/// O que a tela de cadastro mostra ao digitar o CNPJ ("Sua empresa: XYZ
/// LTDA, Salvador/BA"). Só o necessário para a pessoa reconhecer a empresa:
/// sem endereço, telefone ou e-mail (a consulta é pública e não pode virar
/// fonte grátis de dados de terceiros).
/// </summary>
public sealed record ConsultaCnpjCadastroResponse(
    string Cnpj,
    string RazaoSocial,
    string? NomeFantasia,
    string? Municipio,
    string? Uf,
    bool Ativa,
    string? SituacaoCadastral,
    bool JaTemConta);

/// <summary>Consulta do CNPJ na tela pública de cadastro (sem login, com limite por IP).</summary>
public interface IConsultarCnpjCadastroUseCase
{
    Task<ConsultaCnpjCadastroResponse> ExecutarAsync(string cnpj, CancellationToken cancellationToken);
}

/// <summary>
/// Cria a primeira Empresa da conta perfil Empresa, com o que a Receita
/// informou (ou só CNPJ e nome, se a consulta falhou). O que não vem em
/// consulta pública fica pendente (Empresa.PendenciasCadastro). Roda dentro
/// da transação do cadastro; não grava sozinho (quem chama dá o SaveChanges).
/// </summary>
public interface ICriarEmpresaInicialService
{
    void Adicionar(Guid tenantId, string cnpj, string nome, string emailConta, ConsultaCnpjResponse? consulta);
}

/// <summary>
/// Confirmação do e-mail da conta: link enviado no cadastro (e reenviado pelo
/// aviso no topo). Sem e-mail confirmado a conta usa tudo e emite em
/// homologação, mas não emite em produção (a nota, os avisos e a cobrança
/// vão por e-mail).
/// </summary>
public interface IConfirmacaoEmailService
{
    /// <summary>Gera o link e enfileira o e-mail. Não faz nada se já confirmado.</summary>
    Task EnviarAsync(Guid usuarioId, CancellationToken cancellationToken);

    /// <summary>Valida o link. Falso quando o link é inválido ou expirou.</summary>
    Task<bool> ConfirmarAsync(Guid usuarioId, string token, CancellationToken cancellationToken);

    /// <summary>A conta tem algum usuário com e-mail confirmado (exigido para emitir em produção).</summary>
    Task<bool> ContaConfirmadaAsync(Guid tenantId, CancellationToken cancellationToken);
}
