namespace NfseSaaS.Application.UseCases.SincronizacaoSefin;

public sealed record SincronizacaoSefinResponse(
    int NotasImportadas,
    int NotasJaExistentes,
    int NotasIgnoradasPorConflitoNumeracao,
    int ClientesCriados,
    int NotasAtualizadasPorEvento,
    long UltimoNsuProcessado);
