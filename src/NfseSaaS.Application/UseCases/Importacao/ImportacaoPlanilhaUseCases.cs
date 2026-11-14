namespace NfseSaaS.Application.UseCases.Importacao;

/// <summary>Como fica cada linha da planilha.</summary>
public enum SituacaoLinhaImportacao
{
    /// <summary>Vai ser cadastrada (na conferência) / foi cadastrada (na gravação).</summary>
    Nova = 1,

    /// <summary>Cliente já cadastrado com mudanças, e "Atualizar os que já existem" marcado.</summary>
    Atualiza = 2,

    /// <summary>Já está cadastrado: a linha não muda nada.</summary>
    JaExiste = 3,

    /// <summary>Já está cadastrado, com "Atualizar" marcado, e a planilha traz o mesmo que o cadastro.</summary>
    SemMudanca = 4,

    Erro = 5
}

/// <param name="AtualizarExistentes">Cliente já cadastrado recebe as células preenchidas da planilha (célula vazia nunca apaga).</param>
public sealed record OpcoesImportacao(bool AtualizarExistentes = false);

/// <summary>O que o usuário logado pode fazer (conferido no controller pelas permissões do grupo).</summary>
public sealed record PermissoesImportacao(bool IncluirClientes, bool AlterarClientes, bool IncluirContratos);

public sealed record ArquivoImportacao(string NomeArquivo, byte[] Conteudo);

/// <param name="Aba">"clientes" ou "contratos".</param>
/// <param name="Linha">Número da linha na planilha (o cabeçalho é a 1).</param>
/// <param name="Chave">CPF/CNPJ formatado (cliente) ou número do contrato.</param>
/// <param name="Descricao">Apelido e nome (cliente) ou cliente e serviço (contrato).</param>
/// <param name="Mudancas">Atualização: o que muda no cadastro ("E-mail: a@x → b@x").</param>
/// <param name="Valores">O que veio em cada coluna, na ordem do cabeçalho (para o relatório de erros).</param>
public sealed record LinhaImportacaoResponse(
    string Aba,
    int Linha,
    SituacaoLinhaImportacao Situacao,
    string SituacaoTexto,
    string Chave,
    string Descricao,
    IReadOnlyList<string> Erros,
    IReadOnlyList<string> Avisos,
    IReadOnlyList<string> Mudancas,
    IReadOnlyList<string> Valores);

/// <param name="Tipo">"clientes" ou "contratos".</param>
/// <param name="ColunasReconhecidas">"Cabeçalho da planilha → campo do sistema" (só quando o nome é diferente).</param>
public sealed record AbaImportacaoResponse(
    string Tipo,
    string Nome,
    IReadOnlyList<string> Cabecalhos,
    IReadOnlyList<string> ColunasIgnoradas,
    IReadOnlyList<string> ColunasFaltando,
    IReadOnlyList<string> ColunasReconhecidas,
    IReadOnlyList<LinhaImportacaoResponse> Linhas);

public sealed record TotaisImportacao(int Novas, int Atualiza, int JaExiste, int SemMudanca, int Erros)
{
    public int Total => Novas + Atualiza + JaExiste + SemMudanca + Erros;
}

/// <param name="Gravado">false = conferência (nada foi gravado); true = resultado da gravação.</param>
/// <param name="Avisos">Avisos gerais (aba ignorada, limite de consultas à Receita…).</param>
public sealed record ResultadoImportacaoResponse(
    bool Gravado,
    string Arquivo,
    IReadOnlyList<AbaImportacaoResponse> Abas,
    TotaisImportacao Clientes,
    TotaisImportacao Contratos,
    IReadOnlyList<string> Avisos);

/// <summary>Linhas com erro para devolver numa planilha ("Baixar linhas com erro").</summary>
public sealed record RelatorioErrosImportacaoRequest(IReadOnlyList<AbaRelatorioErros> Abas);

public sealed record AbaRelatorioErros(string Nome, IReadOnlyList<string> Cabecalhos, IReadOnlyList<LinhaRelatorioErros> Linhas);

public sealed record LinhaRelatorioErros(int Linha, IReadOnlyList<string> Valores, string Erro);

/// <summary>
/// IM1: importar clientes e contratos por planilha (.xlsx ou .csv) na empresa
/// selecionada. Conferir não grava nada; gravar confere de novo o mesmo
/// arquivo e grava as linhas sem erro pelos mesmos casos de uso das telas.
/// </summary>
public interface IImportacaoPlanilhaUseCase
{
    /// <summary>Modelo .xlsx para baixar: abas Clientes, Contratos e Instruções, com listas (inclusive os serviços da empresa).</summary>
    Task<byte[]> ModeloAsync(Guid empresaId, CancellationToken cancellationToken);

    Task<ResultadoImportacaoResponse> ConferirAsync(
        Guid empresaId, ArquivoImportacao arquivo, OpcoesImportacao opcoes, PermissoesImportacao permissoes, CancellationToken cancellationToken);

    Task<ResultadoImportacaoResponse> GravarAsync(
        Guid empresaId, ArquivoImportacao arquivo, OpcoesImportacao opcoes, PermissoesImportacao permissoes, CancellationToken cancellationToken);

    /// <summary>A planilha das linhas com erro, com a coluna "Erro" no fim, para corrigir e mandar de novo.</summary>
    byte[] RelatorioErros(RelatorioErrosImportacaoRequest request);
}
