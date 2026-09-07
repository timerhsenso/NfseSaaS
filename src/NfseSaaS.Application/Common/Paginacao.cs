namespace NfseSaaS.Application.Common;

/// <summary>Normaliza page/pageSize vindos da API para valores seguros.</summary>
public static class Paginacao
{
    public const int PageSizeDefault = 20;
    public const int PageSizeMaximo = 100;

    public static (int Page, int PageSize) Normalizar(int page, int pageSize)
    {
        var paginaNormalizada = page < 1 ? 1 : page;
        var tamanhoNormalizado = pageSize switch
        {
            <= 0 => PageSizeDefault,
            > PageSizeMaximo => PageSizeMaximo,
            _ => pageSize
        };
        return (paginaNormalizada, tamanhoNormalizado);
    }
}
