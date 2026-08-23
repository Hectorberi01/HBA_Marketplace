using System.Text.Json.Serialization;

namespace HBA.Admin.Desktop.Services;

/// <summary>Une fiche produit, telle que la liste d'administration l'affiche.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// CE MODÈLE NE REPREND QUE SEPT DES QUINZE CHAMPS DE `ProductSummary`.
///
/// L'amont rend aussi les attributs, les étiquettes, les déclinaisons et les
/// médias — de quoi peser plusieurs kilo-octets par ligne, pour une liste de
/// vingt-cinq. `System.Text.Json` ignore silencieusement ce qu'on ne déclare
/// pas : les champs absents ici ne sont pas perdus, ils ne sont pas lus.
///
/// Le jour où l'écran montrera une vignette, `media` s'ajoutera. Le déclarer
/// avant d'en avoir l'usage ferait désérialiser des listes que personne
/// n'affiche.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record ProduitAdmin(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("sellerId")] Guid SellerId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("gtin")] string? Gtin,
    [property: JsonPropertyName("brandId")] Guid? BrandId);

/// <summary>Un motif de rejet, tel que catalog-service l'attend.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// LA CONSOLE N'A PAS ENCORE DE TAXONOMIE DE MOTIFS, ET LE DIT.
///
/// `MotifSaisi(Code, Field, Message)` est prévu pour une liste fermée de codes —
/// « photo illisible », « catégorie erronée » — que le vendeur pourrait traduire
/// et filtrer. Cette console n'en a pas : elle envoie donc UN motif, de code
/// `ADMIN`, portant le texte que l'administrateur a écrit.
///
/// CE QUE CELA NE COUVRE PAS : le vendeur reçoit une phrase, pas un code. Il ne
/// peut ni la filtrer, ni la voir traduite. Le jour où la taxonomie existera,
/// c'est ici que la liste déroulante se branchera — et le code `ADMIN` servira
/// à retrouver les rejets écrits avant elle.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record MotifRejet(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("field")] string? Field,
    [property: JsonPropertyName("message")] string Message);

/// <summary>Les gestes de modération sur une fiche produit.</summary>
public sealed record GesteProduit(
    string Cle,
    string Libelle,
    string Chemin,
    SaisieRequise Saisie,
    bool Destructeur)
{
    /// <summary>
    /// Approuver ne demande rien et n'est PAS destructeur.
    /// </summary>
    /// <remarks>
    /// C'est le seul geste de toute la console qui OUVRE un droit sans en retirer
    /// aucun : la fiche devient publiable par son vendeur. Exiger une
    /// ré-authentification sur le geste le plus fréquent de la file la plus
    /// volumineuse ferait retaper le mot de passe cent fois par jour — et un mot
    /// de passe tapé cent fois par jour se tape sans lire.
    /// </remarks>
    public static readonly GesteProduit Approuver =
        new("approuver", "Approuver la fiche", "approve", SaisieRequise.Aucune, false);

    public static readonly GesteProduit Rejeter =
        new("rejeter", "Rejeter", "reject", SaisieRequise.Motif, true);

    public static readonly GesteProduit Suspendre =
        new("suspendre", "Suspendre", "suspend", SaisieRequise.Motif, true);

    public static readonly GesteProduit Retablir =
        new("retablir", "Rétablir", "restore", SaisieRequise.Aucune, false);

    public static readonly IReadOnlyList<GesteProduit> Tous =
        [Approuver, Rejeter, Suspendre, Retablir];

    /// <summary>Ce geste s'applique-t-il à une fiche dans cet état ?</summary>
    /// <remarks>
    /// LA MACHINE À ÉTATS EST DANS LE DOMAINE — `ProductStatusTransitions` —
    /// ET ELLE EST UNE LISTE BLANCHE.
    ///
    /// Ce qui suit n'est pas une seconde source de vérité : c'est un filtre
    /// d'affichage qui évite de proposer un geste que le service refusera. Le
    /// service reste seul juge ; si les deux divergent, c'est lui qui a raison,
    /// et l'écran affichera son refus.
    /// </remarks>
    public bool ApplicableA(string statut) => Cle switch
    {
        "approuver" or "rejeter" => statut == "PendingReview",
        "suspendre" => statut is "Published" or "Approved" or "Unpublished",
        _ => statut == "Suspended",
    };
}
