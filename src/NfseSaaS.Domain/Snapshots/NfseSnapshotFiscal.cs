namespace NfseSaaS.Domain.Snapshots;

/// <summary>
/// Formato do snapshot fiscal persistido em Nfse.SnapshotFiscalJson
/// (coluna jsonb). Complementa os campos já colunares da própria Nfse
/// (CodigoTributacaoNacional, TribIssqn, CstPisCofins etc.) com o
/// restante do contexto de Empresa/Cliente/Servico usado na emissão —
/// dados que hoje NÃO têm coluna própria na Nfse mas que, se o cadastro
/// mudar depois (razão social, endereço, regime, descrição do serviço no
/// catálogo), não podem alterar retroativamente o que uma nota já
/// autorizada representa.
///
/// "Versao" existe para permitir evoluir este formato (adicionar campos,
/// mudar estrutura) sem quebrar a leitura de snapshots antigos já
/// persistidos — o código que desserializa deve tratar Versao ausente ou
/// menor como o formato mais antigo conhecido.
/// </summary>
public sealed record NfseSnapshotFiscal(
    int Versao,
    NfseSnapshotEmpresa Empresa,
    NfseSnapshotCliente Cliente,
    NfseSnapshotServico Servico);

public sealed record NfseSnapshotEmpresa(
    string RazaoSocial,
    string? NomeFantasia,
    string Cnpj,
    string InscricaoMunicipal,
    string OpSimpNac,
    string RegApTribSN,
    string RegEspTrib,
    NfseSnapshotEndereco Endereco);

public sealed record NfseSnapshotCliente(
    string Nome,
    string CpfCnpj,
    NfseSnapshotEndereco Endereco);

/// <summary>
/// Descrição e valor padrão do Servico do CATÁLOGO no momento da
/// emissão — não confundir com DescricaoServico/ValorServico da própria
/// Nfse, que são o que foi de fato cobrado nesta nota (podem ter sido
/// ajustados na hora, diferente do padrão cadastrado).
/// </summary>
public sealed record NfseSnapshotServico(
    string DescricaoCadastro,
    decimal ValorPadraoCadastro);

public sealed record NfseSnapshotEndereco(
    string Cep,
    string Logradouro,
    string Numero,
    string? Complemento,
    string Bairro,
    string Uf);
