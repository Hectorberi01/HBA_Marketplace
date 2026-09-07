namespace HBA.Gateway.Application.Bff.Shared;

/// <summary>Page d'une liste. Convention UNIQUE de toute la passerelle (§37).</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int? TotalCount)
{
    /// <summary>`TotalCount` EST NULLABLE, ET CE N'EST PAS UN OUBLI.</summary>
    public static PagedResult<T> Of(IReadOnlyList<T> items, PageRequest request)
        => new(items, request.Page, request.PageSize, null);
}

/// <summary>Paramètres de pagination reçus du client, bornés.</summary>
public sealed record PageRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Page { get; }
    public int PageSize { get; }

    /// <summary>LES BORNES SONT APPLIQUÉES ICI, PAS LAISSÉES AU SERVICE AMONT.</summary>
    public PageRequest(int? page, int? pageSize)
    {
        Page = page is null or < 1 ? 1 : page.Value;
        PageSize = pageSize switch
        {
            null or < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize.Value,
        };
    }

    public IReadOnlyList<T> Apply<T>(IReadOnlyList<T> source)
        => source.Skip((Page - 1) * PageSize).Take(PageSize).ToList();
}
