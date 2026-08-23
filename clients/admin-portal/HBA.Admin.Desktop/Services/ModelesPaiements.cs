using System.Text.Json.Serialization;

namespace HBA.Admin.Desktop.Services;

/// <summary>Page brute du socle : `PagedResult&lt;T&gt;`, sans enveloppe.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// TROISIÈME FORME DE RÉPONSE DU DÉPÔT, ET IL FAUT LES DISTINGUER.
///
///   • `ApiResults.Page(...)` → { data: [...], meta: { page, total, … } }
///     C'est ce que rend la liste des vendeurs.
///
///   • `Results.Ok(pagedResult)` → { items: [...], total, page, pageSize }
///     C'est ce que rend la liste des paiements : le handler écrit
///     `Match(Results.Ok)`, donc l'objet `PagedResult` part TEL QUEL.
///
///   • `Results.Ok(tableau)` → [ … ]
///     C'est ce que rendent les files de retrait.
///
/// Les trois coexistent dans le même service financier. Lire l'une avec le
/// modèle d'une autre ne lève pas : les propriétés restent nulles, la liste
/// s'affiche vide, et rien ne dit pourquoi.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record PageBrute<T>(
    [property: JsonPropertyName("items")] IReadOnlyList<T>? Items,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize);

/// <summary>Vue publique d'un paiement, telle que payment-service la rend.</summary>
public sealed record PaiementAdmin(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("orderId")] Guid OrderId,
    [property: JsonPropertyName("buyerId")] Guid BuyerId,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("providerReference")] string? ProviderReference,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdAtUtc")] DateTime CreatedAtUtc,
    [property: JsonPropertyName("capturedAtUtc")] DateTime? CapturedAtUtc);

/// <summary>Le résumé chiffré de la file, tel que `/payments/stats` le rend.</summary>
public sealed record StatsPaiements(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("capturedCount")] int CapturedCount,
    [property: JsonPropertyName("capturedAmount")] decimal CapturedAmount,
    [property: JsonPropertyName("pendingCount")] int PendingCount,
    [property: JsonPropertyName("failedCount")] int FailedCount,
    [property: JsonPropertyName("refundedCount")] int RefundedCount,
    [property: JsonPropertyName("refundedAmount")] decimal RefundedAmount);

/// <summary>Les gestes de rattrapage sur un paiement.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// CE SONT DES GESTES DE RATTRAPAGE, PAS LE CYCLE DE VIE NORMAL.
///
/// Un paiement Mobile Money se capture par le webhook du prestataire. Ces trois
/// routes existent pour le jour où le webhook n'arrive pas : elles forcent
/// l'état à la main.
///
/// D'où `providerReference` OBLIGATOIRE sur la capture — c'est la référence chez
/// FedaPay qui prouve que l'argent est bien arrivé. La capture sans elle serait
/// une affirmation sans pièce.
///
/// `refund` NE PREND AUCUN CORPS, ET REMBOURSE LA TOTALITÉ.
///
/// Il n'y a pas de remboursement partiel par ce chemin. Le remboursement partiel
/// passe par le parcours de retour (`/api/v1/admin/returns`), qui décide du
/// montant. Les deux existent, et l'écran doit dire lequel il emprunte.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record GestePaiement(
    string Cle,
    string Libelle,
    string Chemin,
    SaisieRequise Saisie,
    bool Destructeur)
{
    public static readonly GestePaiement Capturer =
        new("capturer", "Capturer", "capture", SaisieRequise.Reference, true);

    public static readonly GestePaiement Echouer =
        new("echouer", "Marquer en échec", "fail", SaisieRequise.Motif, true);

    public static readonly GestePaiement Rembourser =
        new("rembourser", "Rembourser la totalité", "refund", SaisieRequise.Aucune, true);

    public static readonly IReadOnlyList<GestePaiement> Tous =
        [Capturer, Echouer, Rembourser];

    /// <summary>Ce geste s'applique-t-il à un paiement dans cet état ?</summary>
    /// <remarks>
    /// PROPOSER UN GESTE IMPOSSIBLE FAIT CHERCHER UNE PANNE LÀ OÙ IL Y A UNE RÈGLE.
    ///
    /// Capturer un paiement déjà capturé, rembourser un paiement en échec : le
    /// domaine refuse, à juste titre, et l'administrateur voit un message
    /// d'erreur là où il fallait un bouton grisé. La règle est ici parce que
    /// l'écran la connaît — et non parce qu'il la décide : c'est le service qui
    /// tranche, on lui évite seulement des appels qu'il rejettera.
    /// </remarks>
    public bool ApplicableA(string statut) => Cle switch
    {
        "capturer" => statut is "Pending" or "Authorized",
        "echouer" => statut is "Pending" or "Authorized",
        _ => statut is "Captured",
    };
}
