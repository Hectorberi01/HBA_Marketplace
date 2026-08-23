namespace HBA.Gateway.Application.Contracts.Order;

/// <summary>
/// Miroir MINIMAL d'une commande.
/// </summary>
/// <remarks>
/// `OrderSummary` PORTE 14 CHAMPS ET 13 PAR LIGNE. AUCUN ÉCRAN N'EN VEUT AUTANT.
///
/// Remises vendeur, remises plateforme, prix unitaire de base, lieu d'expédition,
/// identifiant de panier : ce sont des données de facturation. Une bannière
/// « commande en cours » a besoin d'un identifiant, d'un statut et d'un montant.
///
/// Les champs omis ne sont pas perdus : ils restent accessibles par la fiche
/// détaillée, qui les demandera quand quelqu'un les regardera vraiment.
/// </remarks>
public sealed record OrderBrief(
    Guid Id,
    string Status,
    string Currency,
    decimal GrandTotal,
    DateTime CreatedAtUtc);
