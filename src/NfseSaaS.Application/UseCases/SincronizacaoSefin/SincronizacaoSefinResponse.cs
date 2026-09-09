namespace NfseSaaS.Application.UseCases.SincronizacaoSefin;

public sealed record SincronizacaoSefinResponse(
    int NotasImportadas,
    int NotasJaExistentes,
    int NotasIgnoradasPorConflitoNumeracao,
    int ClientesCriados,
    long UltimoNsuProcessado);
