using NfseSaaS.Application.Exceptions;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Recursos;

public enum TipoRecurso
{
    /// <summary>Liberado ou bloqueado.</summary>
    LigaDesliga = 1,

    /// <summary>Quantidade máxima (nulo = ilimitado).</summary>
    Limite = 2
}

/// <summary>De onde veio o valor que vale para a conta.</summary>
public enum OrigemRecurso
{
    /// <summary>Padrão do catálogo (sem plano ou plano sem esse recurso configurado).</summary>
    Padrao = 1,

    Plano = 2,

    /// <summary>Exceção da conta, criada pelo operador.</summary>
    Excecao = 3
}

/// <summary>
/// Um recurso que o plano liga/desliga ou limita. O padrão reproduz o
/// comportamento de antes do PL1 (por isso pode depender do perfil da conta):
/// com a cobrança desligada ou sem configuração, nada muda para quem já usa.
/// </summary>
/// <param name="Unidade">Para limite: como se conta ("usuários", "clientes por empresa").</param>
public sealed record RecursoPlano(
    string Codigo,
    string Nome,
    string Descricao,
    TipoRecurso Tipo,
    Func<PerfilConta, bool>? PadraoLigado = null,
    int? PadraoLimite = null,
    string? Unidade = null)
{
    public bool LigadoPorPadrao(PerfilConta perfil) => PadraoLigado?.Invoke(perfil) ?? true;

    /// <summary>Texto do padrão para a grade da Plataforma.</summary>
    public string TextoPadrao => Tipo == TipoRecurso.LigaDesliga
        ? PadraoLigado is null ? "Liberado" : "Só contador"
        : PadraoLimite is { } n ? $"Até {n}" : "Ilimitado";
}

/// <summary>
/// PL1: os recursos que um plano pode liberar ou limitar. Fica no código
/// porque cada um precisa de programação por trás (onde é conferido); o
/// valor de cada plano fica no banco (planos_recursos) e é editado pela
/// tela. Recurso novo entra aqui com o padrão = como funcionava antes.
/// </summary>
public static class CatalogoRecursos
{
    public const string LogoPropria = "logo-propria";
    public const string EmailSemDivulgacao = "email-sem-divulgacao";
    public const string Usuarios = "usuarios";
    public const string ClientesPorEmpresa = "clientes-por-empresa";
    public const string DocumentosEmpresa = "documentos-empresa";

    private static bool SoContador(PerfilConta perfil) => perfil == PerfilConta.Contador;

    public static readonly IReadOnlyList<RecursoPlano> Todos = new[]
    {
        new RecursoPlano(LogoPropria, "Logomarca própria",
            "Logomarca da conta no menu e nas impressões (Configurações da conta).",
            TipoRecurso.LigaDesliga, PadraoLigado: SoContador),
        new RecursoPlano(EmailSemDivulgacao, "E-mail da nota sem o convite do micoempresa",
            "Permite tirar do rodapé do e-mail da nota o convite para conhecer o sistema (Modelos de e-mail).",
            TipoRecurso.LigaDesliga, PadraoLigado: SoContador),
        new RecursoPlano(Usuarios, "Usuários na conta",
            "Quantos usuários a conta pode ter (convites contam). Passou do limite: não convida mais; quem já está continua.",
            TipoRecurso.Limite, Unidade: "usuários"),
        new RecursoPlano(ClientesPorEmpresa, "Clientes por empresa",
            "Quantos clientes ativos cada empresa pode ter. Passou do limite: não cadastra mais; a emissão nunca para.",
            TipoRecurso.Limite, Unidade: "clientes por empresa"),
        new RecursoPlano(DocumentosEmpresa, "Documentos da empresa",
            "Guardar certidões e documentos da empresa com aviso de vencimento. Bloqueado: só vê e baixa o que já foi enviado.",
            TipoRecurso.LigaDesliga)
    };

    public const int LimiteMaximo = 1_000_000;

    public static RecursoPlano? Obter(string? codigo) =>
        Todos.FirstOrDefault(r => string.Equals(r.Codigo, codigo, StringComparison.Ordinal));

    public static RecursoPlano ObterOuErro(string? codigo) =>
        Obter(codigo) ?? throw new RegraNegocioException($"Recurso desconhecido: \"{codigo}\".");
}

/// <summary>Valor configurado (plano ou exceção). Ligado para liga/desliga; Limite para limite (nulo = ilimitado).</summary>
public sealed record ValorRecurso(bool? Ligado, int? Limite);

/// <summary>Como o recurso vale para a conta.</summary>
public sealed record RecursoResolvido(
    string Codigo,
    string Nome,
    string Descricao,
    TipoRecurso Tipo,
    bool Ligado,
    int? Limite,
    OrigemRecurso Origem,
    DateOnly? ExcecaoValidaAte = null)
{
    /// <summary>"Liberado", "Bloqueado", "Ilimitado" ou "Até N".</summary>
    public string Texto => Tipo == TipoRecurso.LigaDesliga
        ? Ligado ? "Liberado" : "Bloqueado"
        : Limite is { } n ? $"Até {n}" : "Ilimitado";
}

/// <summary>Regras puras do PL1 (sem banco).</summary>
public static class RegrasRecursos
{
    public const int MotivoMinimo = 10;
    public const int MotivoMaximo = 300;

    /// <summary>Exceção vale até o fim do dia ValidaAte (inclusive).</summary>
    public static bool ExcecaoVale(DateOnly? validaAte, DateOnly hoje) => validaAte is null || validaAte >= hoje;

    /// <summary>Ordem: exceção (se vale) → plano (se configurado) → padrão do catálogo.</summary>
    public static RecursoResolvido Resolver(
        RecursoPlano recurso, PerfilConta perfil, ValorRecurso? doPlano, ValorRecurso? excecao, DateOnly? excecaoValidaAte = null)
    {
        if (excecao is not null)
            return Montar(recurso, perfil, excecao, OrigemRecurso.Excecao, excecaoValidaAte);
        if (doPlano is not null)
            return Montar(recurso, perfil, doPlano, OrigemRecurso.Plano, null);
        return new RecursoResolvido(recurso.Codigo, recurso.Nome, recurso.Descricao, recurso.Tipo,
            recurso.Tipo == TipoRecurso.LigaDesliga ? recurso.LigadoPorPadrao(perfil) : true,
            recurso.Tipo == TipoRecurso.Limite ? recurso.PadraoLimite : null,
            OrigemRecurso.Padrao);
    }

    private static RecursoResolvido Montar(RecursoPlano r, PerfilConta perfil, ValorRecurso v, OrigemRecurso origem, DateOnly? validaAte) =>
        new(r.Codigo, r.Nome, r.Descricao, r.Tipo,
            r.Tipo == TipoRecurso.LigaDesliga ? v.Ligado ?? r.LigadoPorPadrao(perfil) : true,
            r.Tipo == TipoRecurso.Limite ? v.Limite : null,
            origem, validaAte);

    /// <summary>Confere o valor para o tipo do recurso e devolve só o campo que vale.</summary>
    public static ValorRecurso Normalizar(RecursoPlano recurso, bool? ligado, int? limite)
    {
        if (recurso.Tipo == TipoRecurso.LigaDesliga)
        {
            if (ligado is null)
                throw new RegraNegocioException($"Escolha se \"{recurso.Nome}\" fica liberado ou bloqueado.");
            return new ValorRecurso(ligado, null);
        }

        if (limite is { } n && (n < 0 || n > CatalogoRecursos.LimiteMaximo))
            throw new RegraNegocioException($"O limite de \"{recurso.Nome}\" vai de 0 a {CatalogoRecursos.LimiteMaximo:N0} (vazio = ilimitado).");
        return new ValorRecurso(null, limite);
    }

    public static string ValidarMotivo(string? motivo)
    {
        var texto = (motivo ?? string.Empty).Trim();
        if (texto.Length < MotivoMinimo || texto.Length > MotivoMaximo)
            throw new RegraNegocioException($"Informe o motivo da exceção ({MotivoMinimo} a {MotivoMaximo} caracteres).");
        return texto;
    }

    /// <summary>Cabe mais um? (atual = quantos já existem).</summary>
    public static bool CabeMaisUm(int? limite, int atual) => limite is not { } n || atual < n;

    /// <summary>Mensagem para quem bateu no limite: diz o que fazer, sem travar o que já existe.</summary>
    public static string MensagemLimite(RecursoResolvido r, int atual) =>
        $"O seu plano permite até {r.Limite} {CatalogoRecursos.Obter(r.Codigo)?.Unidade ?? "itens"} e a conta já tem {atual}. " +
        "Para cadastrar mais, troque de plano em Assinatura (o que já existe continua funcionando).";

    public static string MensagemBloqueado(RecursoResolvido r) =>
        $"\"{r.Nome}\" não está incluído no seu plano. Veja os planos em Assinatura.";
}
