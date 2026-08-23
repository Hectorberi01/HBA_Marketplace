namespace HBA.DeliveryPricing.Contracts;

/// <summary>
/// Un devis de course DÉJÀ ÉTABLI, relu par son identifiant.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE TYPE VIVAIT DANS `HBA.Deliveries.Contracts`, ET IL N'Y AVAIT PAS SA PLACE.
///
/// Il y décrivait le résultat de `IDeliveryDispatchApi.LookupQuoteAsync` — un RPC
/// de delivery-service QUI N'A JAMAIS EU DE CORPS DE SERVEUR. Les deux checkouts,
/// marchandise et repas, l'appelaient : les deux rendaient `UNIMPLEMENTED`, et
/// aucune commande de repas ne pouvait aboutir, le devis y étant obligatoire.
///
/// delivery-service n'a plus de domaine de tarification depuis la séparation des
/// deux services : `DeliveryQuote`, `DeliveryZone` et `PricingRule` n'existent
/// plus dans son code. Le contrat est donc porté là où vit le magasin de devis.
///
/// POURQUOI CE TYPE PORTE PLUS QU'UN MONTANT.
///
/// C'est ce qu'on rend à qui VÉRIFIE un devis présenté par un tiers — un
/// acheteur — et qui ne peut donc rien tenir pour acquis. Il lui faut répondre à
/// quatre questions, et un montant seul n'en couvre aucune : ce devis est-il
/// encore valable, a-t-il déjà servi, a-t-il été établi pour CETTE adresse, et
/// pour CE niveau de service.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="Total">
/// EN `decimal`, ALORS QUE delivery-pricing COMPTE EN ENTIERS (D39).
///
/// La conversion a lieu UNE FOIS, dans le client gRPC, et elle est exacte :
/// 1500 devient 1500,00, les deux côtés comptant en francs. Il n'y a nulle part
/// dans le dépôt de multiplication ni de division par cent.
/// </param>
/// <param name="IsExpired">
/// CALCULÉ PAR LE SERVEUR, PAS DÉDUIT DE <paramref name="ExpiresAtUtc"/>.
///
/// Comparer soi-même l'horodatage déplacerait la règle chez chaque appelant, avec
/// son horloge et sa dérive. La date reste rendue — elle sert au message affiché
/// au client — mais la décision appartient au service qui tient le devis.
/// </param>
/// <param name="PartnerId">
/// TOUJOURS NUL AUJOURD'HUI, ET C'EST UN MANQUE CONNU, PAS UN OUBLI.
///
/// `PlaceOrderCommandHandler` refuse un devis dont ce champ n'est pas nul — sans
/// quoi un acheteur ayant mis la main sur un identifiant de partenaire paierait
/// son colis au tarif de gros. Or delivery-pricing n'a AUCUNE notion de
/// partenaire, et il n'existe aucune route de devis partenaire dans le dépôt.
///
/// La garde est donc inerte — elle l'était déjà avant, puisque rien ne répondait.
/// Elle est conservée : le jour où une grille partenaire existera, c'est le champ
/// qu'il faudra porter jusqu'ici, et elle mordra. La retirer obligerait à la
/// réécrire de mémoire, et c'est ainsi qu'on perd une règle.
/// </param>
public sealed record DeliveryQuoteDetails(
    string QuoteId,
    decimal Total,
    string Currency,
    int EstimatedMinutes,
    double DistanceKm,
    DateTime ExpiresAtUtc,

    // Deux états distincts, jamais fondus en « invalide » : un devis expiré se
    // redemande, un devis consommé signale un rejeu ou un défaut d'intégration.
    bool IsExpired,
    bool IsConsumed,
    double PickupLatitude,
    double PickupLongitude,
    double DropoffLatitude,
    double DropoffLongitude,

    // « EXPRESS », « STANDARD », « SCHEDULED » — la chaîne du contrat, pas une
    // énumération : les contrats ne connaissent pas les énumérations du domaine.
    string DeliveryType,

    Guid? PartnerId);

/// <summary>
/// Relire un devis de course, pour opposer son montant à l'acheteur.
/// </summary>
/// <remarks>
/// LECTURE SEULE, ET DÉLIBÉRÉMENT SÉPARÉE DE LA DEMANDE DE DEVIS.
///
/// Redemander un devis au checkout (`QuoteDelivery`, qui ÉCRIT) produirait un
/// SECOND prix, calculé sur la grille de l'instant : on facturerait à l'acheteur
/// un montant qu'il n'a jamais vu ni accepté. La relecture est la seule opération
/// qui satisfasse les deux exigences à la fois — le serveur impose le prix, ET
/// c'est le prix affiché.
/// </remarks>
public interface IDeliveryQuoteLookup
{
    /// <summary>
    /// Le devis, ou <c>null</c> s'il n'existe pas.
    /// </summary>
    /// <remarks>
    /// `null` N'EST PAS UNE ERREUR : identifiant recopié de travers, devis
    /// purgé, identifiant inventé. Dans tous les cas il n'y a aucun montant
    /// opposable, et l'acheteur doit repasser par un devis — ce n'est pas une
    /// panne à journaliser.
    /// </remarks>
    Task<DeliveryQuoteDetails?> LookupQuoteAsync(
        string? quoteId, CancellationToken cancellationToken = default);
}
