using System.Text.Json.Serialization;

namespace HBA.Admin.Desktop.Services;

/// <summary>Réponse paginée du socle HBA : `success`, `data`, `meta`.</summary>
/// <remarks>
/// LA FORME N'EST PAS CELLE DES FAÇADES BFF, ET LES DEUX COEXISTENT.
///
/// Les services relayés répondent par `ApiEnvelope` — `data` plus `meta`. Les
/// façades BFF répondent par `BffEnvelope` — `data` plus `warnings`. Ce sont deux
/// enveloppes différentes pour deux natures d'appel, et les confondre rend un
/// `Data` nul sans erreur : la liste s'affiche vide, et l'on cherche du côté du
/// filtre.
/// </remarks>
public sealed record PageApi<T>(
    [property: JsonPropertyName("data")] IReadOnlyList<T>? Data,
    [property: JsonPropertyName("meta")] MetaPage? Meta);

/// <summary>Pagination telle que le socle la rend.</summary>
/// <param name="Facets">
/// Le décompte par valeur — statut le plus souvent — calculé sur l'ENSEMBLE
/// filtré et non sur la page servie.
///
/// CALCULÉ AVANT LE FILTRE DE STATUT, CÔTÉ SERVEUR.
///
/// C'est ce qui permet à un onglet « Suspendus (3) » d'afficher son compte alors
/// même qu'on regarde les actifs. Le calculer après ne montrerait qu'un seul
/// chiffre : celui qu'on vient de choisir, et qui n'apprend rien.
///
/// Nul sur les routes qui n'en rendent pas — la plupart.
/// </param>
public sealed record MetaPage(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("total")] long Total,
    [property: JsonPropertyName("hasNext")] bool HasNext,
    [property: JsonPropertyName("facets")] IReadOnlyDictionary<string, int>? Facets = null);

/// <summary>Un vendeur, tel que la gouvernance le liste.</summary>
/// <remarks>
/// LES CHAMPS SUIVENT `SellerSummary` DE merchant-service, SANS EN AJOUTER.
///
/// Le dossier KYB — RCCM, IFU, téléphone du gérant — n'est PAS dans cette liste,
/// et ne doit pas y entrer. Il est servi par la fiche détaillée, derrière une
/// garde d'autorisation qui lui est propre. Une liste d'administration qui
/// porterait ces champs les exposerait pour TOUS les vendeurs à chaque
/// affichage, alors qu'on n'en consulte qu'un à la fois.
/// </remarks>
public sealed record VendeurAdmin(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("shopName")] string ShopName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("kybStatus")] string KybStatus,
    [property: JsonPropertyName("commissionRate")] decimal CommissionRate);

/// <summary>Les gestes de gouvernance ouverts sur un vendeur.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// CHAQUE ENTRÉE PORTE SON CHEMIN, S'IL EXIGE UN MOTIF, ET S'IL EST DESTRUCTEUR.
///
/// Écrire ces trois propriétés à côté du nom, plutôt que dans trois `switch`
/// répartis dans l'écran, ferme une classe entière d'erreurs : celle où l'on
/// ajoute un geste et où l'on oublie de l'inscrire dans la liste des gestes
/// destructeurs. Un tel oubli ne casse rien — il retire simplement la
/// ré-authentification, en silence.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="Chemin">Suffixe ajouté à `/api/v1/merchants/{id}`.</param>
/// <param name="MotifExige">
/// Le corps doit-il porter `{ reason }` ? merchant-service refuse en 400 sinon.
/// </param>
/// <param name="Destructeur">
/// Exige une ré-authentification. Vrai pour tout ce qui retire un droit ou
/// coupe un revenu.
/// </param>
public sealed record GesteVendeur(
    string Cle,
    string Libelle,
    string Chemin,
    bool MotifExige,
    bool Destructeur)
{
    public static readonly GesteVendeur ApprouverKyb =
        new("kyb-approuver", "Approuver le dossier", "kyb/approve", false, false);

    /// <summary>
    /// REJETER UN KYB EST DESTRUCTEUR, MÊME SI RIEN N'EST SUPPRIMÉ.
    ///
    /// Le vendeur ne peut plus vendre tant qu'il n'a pas repris son dossier.
    /// C'est un revenu coupé, et la décision se prend une seule fois.
    /// </summary>
    public static readonly GesteVendeur RejeterKyb =
        new("kyb-rejeter", "Rejeter le dossier", "kyb/reject", true, true);

    public static readonly GesteVendeur Activer =
        new("activer", "Activer le compte", "activate", false, false);

    public static readonly GesteVendeur Suspendre =
        new("suspendre", "Suspendre", "suspend", true, true);

    public static readonly GesteVendeur LeverSuspension =
        new("lever", "Lever la suspension", "lift-suspension", false, false);

    public static readonly GesteVendeur ApprouverReactivation =
        new("reactivation", "Approuver la réactivation", "reactivation/approve", false, false);

    /// <summary>Tous les gestes, dans l'ordre d'affichage.</summary>
    public static readonly IReadOnlyList<GesteVendeur> Tous =
    [
        ApprouverKyb,
        RejeterKyb,
        Activer,
        Suspendre,
        LeverSuspension,
        ApprouverReactivation,
    ];
}
