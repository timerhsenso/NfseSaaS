namespace NfseSaaS.Application.UseCases.NotaMensal;

public sealed record ItemResultadoNotaMensalResponse(
    Guid ContratoId,
    // Nome de EXIBIÇÃO do Cliente = Cliente.Apelido (não a razão social).
    string ClienteNome,
    string DescricaoNota,
    bool Sucesso,
    Guid? NfseId,
    string? NumeroNfse,
    string? Mensagem,
    // "Gravar no contrato" foi aplicado (acontece antes de emitir, então
    // vale mesmo com a nota recusada).
    bool ContratoAtualizado = false);
