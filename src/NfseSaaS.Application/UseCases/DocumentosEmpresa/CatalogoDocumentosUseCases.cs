using NfseSaaS.Application.Exceptions;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.DocumentosEmpresa;

/// <summary>DOC4: lê o catálogo de tipos de documento do banco (global; uma consulta pequena).</summary>
public interface ICatalogoDocumentos
{
    Task<CatalogoDocumentosEmpresa> ObterAsync(CancellationToken cancellationToken);
}

// ===================== Escolha da empresa =====================

/// <param name="Marcado">A empresa gerencia este documento.</param>
/// <param name="Padrao">Vem marcado para quem ainda não escolheu.</param>
/// <param name="TemArquivo">Já tem arquivo enviado (desmarcar não apaga, mas a tela avisa).</param>
/// <param name="ClientesExigem">Quantos clientes exigem este documento junto com a nota.</param>
/// <param name="Inativo">Saiu do catálogo, mas a empresa ainda usa (continua aparecendo para ela).</param>
public sealed record OpcaoEscolhaDocumento(
    int Tipo,
    string Nome,
    string Orgao,
    AbrangenciaDocumento Abrangencia,
    int? ValidadeDias,
    bool Marcado,
    bool Padrao,
    bool TemArquivo,
    int ClientesExigem,
    bool Inativo);

public sealed record CategoriaEscolhaDocumentos(CategoriaDocumento Categoria, string Nome, IReadOnlyList<OpcaoEscolhaDocumento> Tipos);

public sealed record PacoteDocumentoResponse(Guid Id, string Nome, string? Descricao, IReadOnlyList<int> Tipos);

/// <param name="JaEscolheu">false = a lista atual é o padrão (nada foi salvo ainda).</param>
/// <param name="OutrasEmpresas">Quantas outras empresas a conta tem (mostra "Copiar para as outras empresas").</param>
public sealed record EscolhaDocumentosResponse(
    Guid EmpresaId,
    bool JaEscolheu,
    IReadOnlyList<CategoriaEscolhaDocumentos> Categorias,
    IReadOnlyList<PacoteDocumentoResponse> Pacotes,
    int OutrasEmpresas);

public sealed record SalvarEscolhaDocumentosRequest(Guid EmpresaId, IReadOnlyList<int>? Tipos);

public sealed record CopiarEscolhaDocumentosRequest(Guid EmpresaOrigemId);

public sealed record SalvarLinkDocumentoRequest(Guid EmpresaId, int Tipo, string? Link);

/// <summary>
/// DOC4: quais documentos a empresa gerencia e os links dela. Desmarcar
/// nunca apaga arquivo nem a exigência dos clientes.
/// </summary>
public interface IEscolhaDocumentosEmpresaUseCase
{
    Task<EscolhaDocumentosResponse> ObterAsync(Guid empresaId, CancellationToken cancellationToken);

    Task SalvarAsync(SalvarEscolhaDocumentosRequest request, CancellationToken cancellationToken);

    /// <summary>Copia a escolha da empresa para todas as outras empresas da conta (os links próprios não vão: cada lugar tem o seu).</summary>
    Task<int> CopiarParaOutrasAsync(CopiarEscolhaDocumentosRequest request, CancellationToken cancellationToken);

    /// <summary>Link oficial próprio da empresa para um documento que ela gerencia (vazio = volta a usar o do catálogo).</summary>
    Task<LinkDocumentoResolvido?> SalvarLinkAsync(SalvarLinkDocumentoRequest request, CancellationToken cancellationToken);
}

// ===================== Operador da plataforma =====================

public sealed record TipoDocumentoAdminResponse(
    int Tipo,
    string? Codigo,
    string Nome,
    CategoriaDocumento Categoria,
    string CategoriaNome,
    AbrangenciaDocumento Abrangencia,
    string? Orgao,
    string? LinkEmissao,
    int? ValidadeDias,
    bool ExigeValidade,
    bool Ativo,
    bool PadraoNovasEmpresas,
    int Ordem,
    bool Sistema,
    int EmpresasUsando,
    int LinksLocais);

public sealed record PacoteDocumentoAdminResponse(Guid Id, string? Codigo, string Nome, string? Descricao, IReadOnlyList<int> Tipos, bool Ativo, int Ordem);

public sealed record OpcaoCategoriaResponse(CategoriaDocumento Valor, string Nome);

public sealed record CatalogoDocumentosAdminResponse(
    IReadOnlyList<TipoDocumentoAdminResponse> Tipos,
    IReadOnlyList<PacoteDocumentoAdminResponse> Pacotes,
    IReadOnlyList<OpcaoCategoriaResponse> Categorias);

public sealed record SalvarTipoDocumentoRequest(
    string? Nome,
    CategoriaDocumento Categoria,
    AbrangenciaDocumento Abrangencia,
    string? Orgao,
    string? LinkEmissao,
    int? ValidadeDias,
    bool ExigeValidade,
    bool Ativo,
    bool PadraoNovasEmpresas,
    int Ordem);

public sealed record SalvarPacoteDocumentoRequest(string? Nome, string? Descricao, IReadOnlyList<int>? Tipos, bool Ativo, int Ordem);

/// <summary>Link oficial cadastrado pelo operador para uma UF ou um município.</summary>
public sealed record LinkLocalDocumentoResponse(Guid Id, string? Uf, string? CodigoMunicipio, string Local, string Link);

/// <param name="Oficial">O operador já cadastrou exatamente este link para o lugar.</param>
/// <param name="Sugerido">Passa no filtro de sugestão (as empresas do lugar já veem).</param>
public sealed record SugestaoLinkDocumentoResponse(string? Uf, string? CodigoMunicipio, string Local, string Link, int Contas, bool Oficial, bool Sugerido);

public sealed record LinksTipoDocumentoResponse(
    int Tipo,
    string Nome,
    AbrangenciaDocumento Abrangencia,
    IReadOnlyList<LinkLocalDocumentoResponse> Oficiais,
    IReadOnlyList<SugestaoLinkDocumentoResponse> UsadosPelasEmpresas);

/// <summary>Uf (estadual) ou CodigoMunicipio (municipal). Link vazio = remove o link oficial do lugar.</summary>
public sealed record SalvarLinkLocalDocumentoRequest(string? Uf, string? CodigoMunicipio, string? Link);

/// <summary>
/// DOC4: Plataforma › Documentos. Tipos e pacotes nunca são apagados (só
/// inativados): documentos enviados e escolhas das empresas apontam para eles.
/// </summary>
public interface IAdministrarCatalogoDocumentosUseCase
{
    Task<CatalogoDocumentosAdminResponse> ListarAsync(CancellationToken cancellationToken);

    /// <summary>tipo nulo = novo. Devolve o Id.</summary>
    Task<int> SalvarTipoAsync(int? tipo, SalvarTipoDocumentoRequest request, CancellationToken cancellationToken);

    Task<Guid> SalvarPacoteAsync(Guid? pacoteId, SalvarPacoteDocumentoRequest request, CancellationToken cancellationToken);

    Task<LinksTipoDocumentoResponse> LinksAsync(int tipo, CancellationToken cancellationToken);

    Task SalvarLinkLocalAsync(int tipo, SalvarLinkLocalDocumentoRequest request, CancellationToken cancellationToken);
}

/// <summary>DOC4: conferências do cadastro do operador (puras).</summary>
public static class RegrasCatalogoDocumentos
{
    public const int NomeMinimo = 3;
    public const int NomeMaximo = 100;
    public const int OrgaoMaximo = 100;
    public const int PacoteNomeMaximo = 80;
    public const int PacoteDescricaoMaxima = 200;
    public const int PacoteMaximoTipos = 60;

    public sealed record TipoValidado(string Nome, string? Orgao, string? LinkEmissao, int? ValidadeDias);

    public static TipoValidado ValidarTipo(int? tipo, SalvarTipoDocumentoRequest r)
    {
        var nome = (r.Nome ?? string.Empty).Trim();
        if (nome.Length < NomeMinimo || nome.Length > NomeMaximo)
            throw new RegraNegocioException($"O nome do documento tem de ter de {NomeMinimo} a {NomeMaximo} caracteres.");
        if (!Enum.IsDefined(r.Categoria))
            throw new RegraNegocioException("Escolha a categoria.");
        if (!Enum.IsDefined(r.Abrangencia))
            throw new RegraNegocioException("Escolha de onde sai o documento (nacional, estadual ou municipal).");

        var orgao = string.IsNullOrWhiteSpace(r.Orgao) ? null : r.Orgao.Trim();
        if (orgao is { Length: > OrgaoMaximo })
            throw new RegraNegocioException($"O órgão pode ter até {OrgaoMaximo} caracteres.");

        if (r.ValidadeDias is { } dias && (dias < 1 || dias > RegrasDocumentoEmpresa.ValidadeMaximaDias))
            throw new RegraNegocioException($"A validade em dias vai de 1 a {RegrasDocumentoEmpresa.ValidadeMaximaDias} (vazio = varia ou não vence).");

        if (tipo == TiposDocumentoSistema.Outro && (!r.Ativo || r.PadraoNovasEmpresas))
            throw new RegraNegocioException("O \"Outro documento\" é do sistema: fica sempre ativo e fora da lista padrão.");

        return new TipoValidado(nome, orgao, RegrasLinkDocumento.Normalizar(r.LinkEmissao), r.ValidadeDias);
    }

    public sealed record PacoteValidado(string Nome, string? Descricao, List<int> Tipos);

    public static PacoteValidado ValidarPacote(SalvarPacoteDocumentoRequest r, CatalogoDocumentosEmpresa catalogo)
    {
        var nome = (r.Nome ?? string.Empty).Trim();
        if (nome.Length < NomeMinimo || nome.Length > PacoteNomeMaximo)
            throw new RegraNegocioException($"O nome do pacote tem de ter de {NomeMinimo} a {PacoteNomeMaximo} caracteres.");
        var descricao = string.IsNullOrWhiteSpace(r.Descricao) ? null : r.Descricao.Trim();
        if (descricao is { Length: > PacoteDescricaoMaxima })
            throw new RegraNegocioException($"A descrição pode ter até {PacoteDescricaoMaxima} caracteres.");

        var tipos = (r.Tipos ?? Array.Empty<int>()).Distinct().ToList();
        if (tipos.Count == 0)
            throw new RegraNegocioException("Marque pelo menos um documento no pacote.");
        if (tipos.Count > PacoteMaximoTipos)
            throw new RegraNegocioException($"Um pacote pode ter até {PacoteMaximoTipos} documentos.");
        if (tipos.Any(t => t == TiposDocumentoSistema.Outro || catalogo.Buscar(t) is null))
            throw new RegraNegocioException("O pacote tem um documento que não existe no catálogo.");

        return new PacoteValidado(nome, descricao, tipos);
    }

    /// <summary>Lista padrão de quem ainda não escolheu: os tipos padrão ativos e os que a empresa já tem arquivo.</summary>
    public static IReadOnlyList<int> PadraoDaEmpresa(CatalogoDocumentosEmpresa catalogo, IEnumerable<int> tiposComArquivo) =>
        catalogo.Selecionaveis.Where(t => t.PadraoNovasEmpresas).Select(t => t.Tipo)
            .Concat(tiposComArquivo.Where(t => t != TiposDocumentoSistema.Outro && catalogo.Buscar(t) is not null))
            .Distinct()
            .ToList();

    /// <summary>Confere a escolha da empresa: só tipos do catálogo, sem o "Outro"; inativo só se ela já usava.</summary>
    public static IReadOnlyList<int> ValidarEscolha(IEnumerable<int>? tipos, CatalogoDocumentosEmpresa catalogo, IReadOnlyCollection<int> jaUsados)
    {
        var lista = (tipos ?? Array.Empty<int>()).Distinct().ToList();
        foreach (var t in lista)
        {
            var info = catalogo.Buscar(t);
            if (info is null || t == TiposDocumentoSistema.Outro)
                throw new RegraNegocioException("Documento desconhecido na escolha.");
            if (!info.Ativo && !jaUsados.Contains(t))
                throw new RegraNegocioException($"\"{info.Nome}\" saiu do catálogo e não pode ser escolhido.");
        }
        return lista;
    }
}
