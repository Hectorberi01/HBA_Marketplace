namespace HBA.Catalog.Domain.Products;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES CRITÈRES DE LA RECHERCHE PUBLIQUE (§17).
///
/// CE QUI N'EST PAS DANS CETTE LISTE EST AUSSI IMPORTANT QUE CE QUI Y EST.
///
/// Il n'y a PAS de filtre par statut. C'est délibéré, et c'est toute la
/// différence avec `ListPagedAsync`, qui sert la console d'administration : là-bas
/// le statut est un critère, ici il est une CONSTANTE — seul `Published` sort.
///
/// L'ancienne route publique se branchait sur la requête d'administration et lui
/// passait un statut facultatif. Sans paramètre, elle rendait tout : brouillons,
/// fiches en attente de validation, rejetées, suspendues. Un critère facultatif
/// de trop, et la vitrine devenait la console.
///
/// Le §17 liste aussi `attributes` et `rating`. Ils ne sont pas ici :
///   • `attributes` demande les définitions d'attributs de catégorie (§10), qui
///     n'existent pas encore ;
///   • `rating` appartient à review-service — le catalogue n'a pas la note, et
///     l'inventer ici en ferait une seconde vérité.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

/// <summary>
/// Ordres de tri acceptés par la vitrine.
///
/// UNE LISTE BLANCHE, PAS UNE CHAÎNE LIBRE.
///
/// Le tri arrive du client. Le passer tel quel à une expression LINQ dynamique
/// ouvrirait la porte à un tri sur `cost_price` — le coût d'achat du vendeur, que
/// le §17 interdit d'exposer. On ne peut pas le LIRE par cette route, mais on
/// pourrait le DEVINER en triant dessus et en observant l'ordre.
/// </summary>
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
