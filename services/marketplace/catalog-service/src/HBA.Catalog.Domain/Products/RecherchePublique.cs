namespace HBA.Catalog.Domain.Products;

/// <summary>LES CRITÈRES DE LA RECHERCHE PUBLIQUE (§17).</summary>
public sealed record RecherchePublique(
    string? Query = null,
    Guid? CategoryId = null,
    Guid? BrandId = null,
    Guid? SellerId = null,
    ProductConditionType? Condition = null,
    long? MinPrice = null,
    long? MaxPrice = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>Ordres de tri acceptés par la vitrine.</summary>
public static class TriPublic
{
    public const string Nouveaute = "newest";
    public const string PrixCroissant = "price_asc";
    public const string PrixDecroissant = "price_desc";
    public const string Nom = "name";

    public static string Normaliser(string? demande)
        => demande?.Trim().ToLowerInvariant() switch
        {
            PrixCroissant => PrixCroissant,
            PrixDecroissant => PrixDecroissant,
            Nom => Nom,
            _ => Nouveaute
        };
}
