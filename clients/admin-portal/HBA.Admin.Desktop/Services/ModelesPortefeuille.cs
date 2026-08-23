using System.Text.Json.Serialization;

namespace HBA.Admin.Desktop.Services;

/// <summary>Les soldes du portefeuille de la plateforme, `PlatformWalletView`.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// QUATRE POCHES, ET LEUR SOMME NE VEUT RIEN DIRE.
///
/// `CommissionBalance` est un revenu ; `ProviderFeeBalance` est ce que la
/// plateforme a payé au prestataire ; `ShippingBalance` est ce qu'elle a
/// encaissé pour la livraison ; `RefundsBalance` est ce qu'elle a rendu. Les
/// additionner mélangerait des entrées et des sorties et produirait un nombre
/// que personne ne pourrait interpréter.
///
/// L'écran affiche donc les quatre séparément, et ne calcule aucun total.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record PortefeuillePlateforme(
    [property: JsonPropertyName("commissionBalance")] decimal CommissionBalance,
    [property: JsonPropertyName("providerFeeBalance")] decimal ProviderFeeBalance,
    [property: JsonPropertyName("shippingBalance")] decimal ShippingBalance,
    [property: JsonPropertyName("refundsBalance")] decimal RefundsBalance,
    [property: JsonPropertyName("currency")] string Currency);

/// <summary>Le portefeuille d'un vendeur, `SellerWalletView`.</summary>
/// <param name="PendingWithdrawal">
/// Les retraits DÉJÀ RETENUS : demandés et en attente de validation, plus ceux
/// partis chez le prestataire. Le contrat le précise — ne compter que les
/// premiers ferait « disparaître » l'argent entre la validation et la
/// confirmation du prestataire.
/// </param>
public sealed record PortefeuilleVendeur(
    [property: JsonPropertyName("sellerId")] Guid SellerId,
    [property: JsonPropertyName("pendingBalance")] decimal PendingBalance,
    [property: JsonPropertyName("availableBalance")] decimal AvailableBalance,
    [property: JsonPropertyName("pendingWithdrawal")] decimal PendingWithdrawal,
    [property: JsonPropertyName("currency")] string Currency);

/// <summary>Le portefeuille d'un livreur, `DriverWalletView`.</summary>
/// <remarks>
/// PAS DE « SOLDE À VENIR », CONTRAIREMENT AU VENDEUR.
///
/// Le contrat le dit : « une course est faite ou ne l'est pas, et aucun retour
/// produit ne reprend le gain de celui qui a transporté le colis. » En revanche
/// il porte le cumul gagné depuis l'inscription, retraits compris — le solde
/// seul ne dit rien de ce qu'on a gagné, puisqu'un retrait le remet à zéro.
/// </remarks>
public sealed record PortefeuilleLivreur(
    [property: JsonPropertyName("driverId")] Guid DriverId,
    [property: JsonPropertyName("availableBalance")] decimal AvailableBalance,
    [property: JsonPropertyName("lifetimeEarned")] decimal LifetimeEarned,
    [property: JsonPropertyName("currency")] string Currency);

/// <summary>Une écriture du grand livre, `WalletTransactionView`.</summary>
/// <param name="Direction">Sens de l'écriture — crédit ou débit.</param>
/// <param name="ReferenceType">
/// Ce à quoi l'écriture se rattache (commande, retrait, remboursement…), quand
/// elle se rattache à quelque chose. Nul sur les mouvements internes.
/// </param>
public sealed record EcritureWallet(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("account")] string Account,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("referenceType")] string? ReferenceType,
    [property: JsonPropertyName("referenceId")] Guid? ReferenceId,
    [property: JsonPropertyName("createdAtUtc")] DateTime CreatedAtUtc);
