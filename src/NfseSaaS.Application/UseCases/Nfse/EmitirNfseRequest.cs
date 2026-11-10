namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>DTO de entrada para o caso de uso de emissão de NFS-e (implementação na Fase 2).</summary>
/// <param name="IdempotencyKey">
/// Opcional. Se o chamador reenviar a mesma requisição com a mesma chave
/// (ex.: retry após timeout de rede), a emissão original é retornada em
/// vez de criar uma segunda Nfse.
/// </param>
/// <param name="ReenvioDeNfseId">
/// Opcional. Id de uma Nfse Rejeitada que esta emissão está reenviando.
/// Gera uma Nfse NOVA (novo NumeroDps), relendo Empresa/Cliente/Servico do
/// cadastro atual; Cliente e Contrato precisam ser os mesmos da original.
/// Só aceito quando a rejeição permite reenvio (ver
/// Nfse.RejeicaoPermiteReenvio).
/// </param>
public sealed record EmitirNfseRequest(
    Guid EmpresaId,
    Guid ClienteId,
    Guid ServicoId,
    decimal ValorServico,
    string DescricaoServico,
    DateOnly DataCompetencia,
    Guid? ContratoId = null,
    string? IdempotencyKey = null,
    Guid? ReenvioDeNfseId = null,
    // ISS retido pelo tomador NESTA nota. Nulo = padrão do Cliente
    // (Cliente.RetemIss) — é o que a Nota Mensal e a automação usam.
    bool? IssRetido = null,
    // Alíquota de ISS (%) NESTA nota. Nula = alíquota da Empresa
    // (Empresa.AliquotaIss). Obrigatória (por um dos dois) quando retido.
    decimal? AliquotaIss = null,
    // Local da prestação (IBGE) NESTA nota (C2). Nulo = padrão do Contrato
    // ou, sem ele, o município da Empresa; "" = o da Empresa (mesmo com contrato).
    string? CodigoMunicipioPrestacao = null,
    // C7: Id da nota Autorizada que esta substitui, com o motivo oficial
    // (cMotivo) e a descrição (xMotivo, obrigatória no 99). Ver RegrasSubstituicao.
    Guid? SubstituirNfseId = null,
    string? MotivoSubstituicao = null,
    string? DescricaoMotivoSubstituicao = null);
