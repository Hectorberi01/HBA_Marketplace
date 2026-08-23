using System.Text.Json.Serialization;

namespace HBA.Admin.Desktop.Services;

/// <summary>Une demande de retrait d'un vendeur ou d'un livreur.</summary>
/// <remarks>
/// Reprend `WithdrawalView` de wallet-service, sans y ajouter de champ.
/// </remarks>
public sealed record RetraitVendeur(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("sellerId")] Guid SellerId,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("providerRef")] string? ProviderRef,
    [property: JsonPropertyName("failureReason")] string? FailureReason,
    [property: JsonPropertyName("createdAtUtc")] DateTime CreatedAtUtc);

/// <summary>Une demande de virement d'un client vers son Mobile Money.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// `Msisdn` EST FIGÉ À LA DEMANDE, ET C'EST LUI QUE L'ADMINISTRATEUR RECOPIE.
///
/// Le contrat de wallet-service le dit : « destination FIGÉE à la demande :
/// c'est elle, et rien d'autre, que l'administrateur recopie chez le
/// prestataire ». Un écran qui afficherait le numéro COURANT du client — celui
/// de son profil — enverrait l'argent à un numéro changé après la demande.
///
/// D'où un champ à part dans ce modèle, et non une lecture du profil.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record RetraitClient(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("customerId")] Guid CustomerId,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("msisdn")] string Msisdn,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("externalReference")] string? ExternalReference,
    [property: JsonPropertyName("requestedAtUtc")] DateTime RequestedAtUtc);

/// <summary>Les deux files de retrait, qui ne sont PAS interchangeables.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// DEUX FILES, DEUX SÉRIES DE ROUTES, DEUX SÉRIES D'ÉTATS.
///
///   • Vendeurs et livreurs : `approve` puis `reject`. Le prestataire exécute.
///   • Clients             : `paid` puis `reject`. AUCUN WEBHOOK NE CONFIRMERA —
///                           l'administrateur exécute le virement lui-même et
///                           saisit la référence, qui est la seule preuve que
///                           l'argent est parti.
///
/// Les fondre dans un seul tableau ferait approuver un virement client avec le
/// geste d'un retrait vendeur. Ce sont des chemins différents, et le second
/// exige une saisie que le premier n'a pas.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public enum FileRetraits
{
    /// <summary>Vendeurs et livreurs, demandes en attente d'arbitrage.</summary>
    PartenairesEnAttente,

    /// <summary>Vendeurs et livreurs, virements engagés chez le prestataire.</summary>
    PartenairesEnCours,

    /// <summary>Clients, virements à exécuter à la main.</summary>
    Clients,
}

/// <summary>Ce qu'une saisie demande à l'administrateur avant un geste.</summary>
public enum SaisieRequise
{
    /// <summary>Rien : le geste part tel quel.</summary>
    Aucune,

    /// <summary>Un motif, EN CLAIR — il est transmis au demandeur.</summary>
    Motif,

    /// <summary>Une référence de virement, en clair.</summary>
    Reference,
}

/// <summary>Un geste applicable à une demande de retrait.</summary>
/// <param name="Chemin">Suffixe ajouté à la base de la file.</param>
/// <param name="Destructeur">
/// Exige une ré-authentification. Vrai pour LES QUATRE.
/// </param>
/// <remarks>
/// APPROUVER EST AUSSI DESTRUCTEUR QUE REFUSER, ET C'EST LE POINT.
///
/// Ailleurs dans la console, « destructeur » désigne ce qui retire un droit.
/// Ici, les deux sens du geste engagent de l'argent réel : approuver le fait
/// sortir, refuser le retient. Aucun des quatre ne doit passer sans que
/// l'administrateur ait retapé son mot de passe.
/// </remarks>
public sealed record GesteRetrait(
    string Cle,
    string Libelle,
    string Chemin,
    SaisieRequise Saisie,
    bool Destructeur)
{
    public static readonly GesteRetrait Approuver =
        new("approuver", "Approuver le retrait", "approve", SaisieRequise.Aucune, true);

    public static readonly GesteRetrait Refuser =
        new("refuser", "Refuser", "reject", SaisieRequise.Motif, true);

    public static readonly GesteRetrait MarquerPaye =
        new("paye", "Marquer comme payé", "paid", SaisieRequise.Reference, true);

    public static readonly GesteRetrait RefuserClient =
        new("refuser-client", "Refuser", "reject", SaisieRequise.Motif, true);

    /// <summary>Les gestes ouverts sur une file donnée.</summary>
    public static IReadOnlyList<GesteRetrait> Pour(FileRetraits file) => file switch
    {
        FileRetraits.PartenairesEnAttente => [Approuver, Refuser],

        // UNE DEMANDE DÉJÀ ENGAGÉE NE SE RÉ-APPROUVE PAS.
        //
        // `processing` signifie que le virement est parti chez le prestataire.
        // Proposer « approuver » ferait rejouer une opération que le service
        // refusera — et l'administrateur chercherait pourquoi.
        FileRetraits.PartenairesEnCours => [],

        _ => [MarquerPaye, RefuserClient],
    };
}
