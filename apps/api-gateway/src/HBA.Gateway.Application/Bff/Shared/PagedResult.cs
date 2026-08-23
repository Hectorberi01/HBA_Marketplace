namespace HBA.Gateway.Application.Bff.Shared;

/// <summary>
/// Page d'une liste. Convention UNIQUE de toute la passerelle (§37).
/// </summary>
/// <remarks>
/// `page` / `pageSize`, ET PAS DE CURSEUR — un seul mécanisme.
///
/// Le curseur est meilleur sur un flux qui bouge pendant la pagination : il évite
/// les doublons quand un élément s'insère en tête. Il est aussi plus coûteux — il
/// suppose un tri stable côté service, et aucun des treize ne l'expose.
///
/// Faire coexister les deux conventions obligerait chaque écran client à savoir
/// laquelle s'applique. Une seule, appliquée partout, se retient.
/// </remarks>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int? TotalCount)
{
    /// <summary>
    /// `TotalCount` EST NULLABLE, ET CE N'EST PAS UN OUBLI.
    ///
    /// Aucun des services amont ne rend de total aujourd'hui. Le calculer côté
    /// passerelle exigerait une seconde requête de comptage par page — un coût
    /// réel pour une information qu'aucun écran mobile n'affiche. `null` signifie
    /// « inconnu », et le client s'en tient au bouton « voir plus ».
    /// </summary>
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

    /// <summary>
    /// LES BORNES SONT APPLIQUÉES ICI, PAS LAISSÉES AU SERVICE AMONT.
    ///
    /// `pageSize=100000` doit être ramené à 100 AVANT de partir vers un service
    /// interne. Faire confiance à l'amont supposerait que les treize services
    /// bornent tous, de la même façon, pour toujours — c'est-à-dire supposer une
    /// discipline qu'aucun mécanisme ne garantit.
    /// </summary>
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
