using HBA.Shared.IntegrationEvents;

namespace HBA.Orders.Contracts.IntegrationEvents;

/// <summary>Commande placée (stock réservé, en attente de paiement). Consommé par Cart (clôture) / Payments.</summary>
public sealed record OrderPlacedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid CartId { get; init; }
    public required decimal GrandTotal { get; init; }
    public required string Currency { get; init; }
}

/// <summary>
/// Part d'UN vendeur dans une commande : ce qu'il a vendu, et pour combien.
///
/// Une commande peut réunir les produits de PLUSIEURS vendeurs. Chacun ne doit voir
/// que sa part : lui communiquer le total de la commande lui ferait croire qu'il a
/// vendu pour le montant d'un autre — et révélerait au passage le chiffre d'un
/// concurrent.
/// </summary>
/// <param name="SellerId">Le vendeur concerné.</param>
/// <param name="ItemCount">Nombre d'articles de CE vendeur.</param>
/// <param name="Amount">Montant acheté chez CE vendeur (prix final, remises comprises,
/// AVANT commission de la plateforme).</param>
public sealed record OrderSellerShare(Guid SellerId, int ItemCount, decimal Amount);

/// <summary>
/// Commande confirmée (paiement encaissé). Consommé par Shipping / Notifications /
/// Settlement / Loyalty / Analytics.
///
/// Porte désormais la RÉPARTITION PAR VENDEUR. Sans elle, le module Notifications
/// était incapable de prévenir les vendeurs d'une commande : il ne recevait que
/// l'acheteur et l'identifiant de commande — et c'est exactement pourquoi les
/// vendeurs n'étaient jamais notifiés.
/// </summary>
public sealed record OrderConfirmedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required string Currency { get; init; }

    /// <summary>
    /// Code promo utilisé par cette commande, ou null. Consommé par Pricing, qui décompte
    /// la promotion À CET INSTANT — pas avant.
    ///
    /// Pourquoi à la confirmation et pas au checkout : une commande peut être placée puis
    /// échouer au paiement. Décompter au checkout « brûlerait » un coupon pour une vente
    /// qui n'a jamais eu lieu, et il faudrait le rendre en compensation — une saga de plus,
    /// pour rien. Ici, seule une vente réellement encaissée consomme le coupon.
    /// </summary>
    public string? PromotionCode { get; init; }

    /// <summary>
    /// Les vendeurs concernés, et la part de chacun.
    ///
    /// VIDE POUR UNE COMMANDE DE REPAS, PAR CONSTRUCTION.
    ///
    /// Le commentaire disait « jamais vide en pratique » : c'était vrai tant que
    /// toute commande était de la marchandise. Un repas n'a pas de vendeur au sens
    /// de la marketplace, et `BuildSellerShares` l'écarte délibérément — un
    /// consommateur qui trouve cette liste vide doit d'abord regarder `Kind`.
    /// </summary>
    public required IReadOnlyCollection<OrderSellerShare> SellerShares { get; init; }

    /// <summary>
    /// La nature de la commande : « Goods » ou « Food ».
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// SANS CE CHAMP, SEPT CONSOMMATEURS TRAITENT UN REPAS COMME UN COLIS.
    ///
    /// Cet événement est écouté par Shipping, Notifications, Wallet, Pricing,
    /// Loyalty, Analytics et Sellers. Aucun n'avait de raison de se poser la
    /// question — jusqu'ici, toute commande confirmée était de la marchandise.
    ///
    /// Le plus dangereux est Shipping : il crée une expédition par couple
    /// (vendeur, lieu d'expédition). Pour une commande de repas, ces deux valeurs
    /// sont vides — il produirait donc UNE expédition attribuée au vendeur
    /// « 00000000-… », qu'aucun vendeur ne verrait jamais et qu'aucun livreur ne
    /// viendrait chercher. La commande serait payée, en attente d'un colis qui
    /// n'existe pas, pendant que la cuisine n'a rien reçu.
    ///
    /// Valeur par défaut « Goods » : les commandes déjà en vol dans l'outbox au
    /// moment du déploiement n'ont pas ce champ, et elles sont toutes de la
    /// marchandise. Le défaut dit la vérité sur elles.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public string Kind { get; init; } = "Goods";

    /// <summary>
    /// L'établissement qui doit préparer la commande. Null hors restauration.
    ///
    /// C'est ce que l'adaptateur vers Food lit pour savoir à quelle cuisine
    /// adresser le ticket. Le porter dans l'événement évite une relecture de la
    /// commande pour une valeur que le producteur connaît déjà.
    /// </summary>
    public Guid? RestaurantId { get; init; }
}

/// <summary>Commande annulée. Consommé par Notifications / analytics.</summary>
public sealed record OrderCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required string Reason { get; init; }
}

/// <summary>Commande livrée. Consommé par Payments (libération escrow) et Settlement (payout).</summary>
public sealed record OrderDeliveredIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// COMMANDE PAYÉE MAIS DEVENUE INEXÉCUTABLE : ELLE ATTEND UN ARBITRAGE HUMAIN.
///
/// CE N'EST PAS `OrderCancelled`, ET AUCUN CONSOMMATEUR NE DOIT LE TRAITER
///    COMME TEL.
///
/// La vente est VIVANTE : l'argent est encaissé, le stock décrémenté, et une
/// course annulée est le plus souvent réattribuable — livreur en panne, refus
/// après acceptation, erreur de dispatch. Un consommateur qui rembourserait ici
/// détruirait des ventes récupérables, et l'argent rendu ne se reprend pas.
///
/// Le seul consommateur prévu est communication-service, qui prévient l'acheteur
/// que son dossier est PRIS EN CHARGE. Le remboursement, s'il est décidé, passe
/// par `OrderCancelled` — publié seulement si l'exploitation tranche en ce sens.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record OrderUnderReviewIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }

    /// <summary>
    /// En clair, et destiné à être lu par un humain : « la course a été annulée
    /// (livreur indisponible) », « commande expédiée depuis 2 lieux ».
    /// </summary>
    public required string Reason { get; init; }
}

/// <summary>
/// L'arbitrage a conclu à la REPRISE : la commande repart, une course va être
/// redemandée.
///
/// NE PAS LE CONFONDRE AVEC `OrderConfirmed`. La confirmation ouvre le ticket
/// de cuisine, décompte le coupon, comptabilise les gains et prévient les
/// vendeurs — tout cela a déjà eu lieu, et le rejouer paierait deux fois. Celui-ci
/// annonce uniquement la levée d'une suspension.
/// </summary>
public sealed record OrderResumedAfterReviewIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
}

/// <summary>
/// Une ligne qu'un vendeur n'honorera pas.
/// </summary>
/// <param name="ShipFromLocationId">
/// INDISPENSABLE, ET FACILE À OUBLIER. Inventory travaille par
/// (SKU, emplacement, commande) : sans l'emplacement, un consommateur ne peut PAS
/// rendre le stock de cette ligne, et il ne le découvrirait qu'en écrivant son
/// gestionnaire.
/// </param>
public sealed record SellerOrderRefusedLine(
    Guid OrderLineId,
    Guid ProductId,
    string Sku,
    Guid ShipFromLocationId,
    int Quantity,
    decimal LineTotal);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN VENDEUR N'HONORERA PAS SA PART D'UNE COMMANDE DÉJÀ PAYÉE (ISSUE-027).
///
/// IL N'A AUJOURD'HUI AUCUN CONSOMMATEUR. UN REFUS VENDEUR NE REMBOURSE
/// ENCORE PERSONNE, ET IL FAUT LE SAVOIR AVANT DE S'APPUYER DESSUS.
///
/// Le client a payé pour trois articles chez deux vendeurs ; l'un refuse. Trois
/// gestes devraient suivre, et aucun n'est câblé — aucun n'appartient d'ailleurs
/// à order-service :
///
///   • inventory-service : remettre la marchandise en rayon. Le stock a été
///     SOLDÉ à la confirmation, ce n'est donc pas une réservation à libérer ;
///   • financial-service : rembourser la PART. Il sait rembourser une commande
///     entière en consommant `OrderCancelled` ; il ne sait pas rembourser une
///     fraction, et c'est le vrai trou ;
///   • communication-service : le dire à l'acheteur.
///
/// Cet événement porte tout ce qu'il faut pour les écrire sans revenir ici. Il
/// est publié aujourd'hui pour que le fait EXISTE sur le bus le jour où le
/// premier consommateur est branché — et pour que la lacune soit nommée plutôt
/// que découverte par un client qui n'a jamais été remboursé.
///
/// TYPE NEUF, PAS UNE VERSION 2 (D32). Ses champs sont donc `required` : la
/// règle interdit d'ajouter du `required` à un type EXISTANT, pas d'en écrire un
/// nouveau qui exige ce dont il ne peut pas se passer.
///
/// PAS D'ATTRIBUT `[HbaEvent]`, VOLONTAIREMENT. Les huit événements
/// d'order-service publient sur `service.ordering.v1` (D31) ; l'annoter le
/// placerait seul sur `hba.&lt;env&gt;.ordering.sellerorder.v1`, qu'aucun
/// consommateur n'écoute et qu'aucun manifeste ne provisionne. La bascule du
/// §19.2 se fera par service, pas par événement isolé.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record SellerOrderRefusedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid SellerId { get; init; }
    public required string Currency { get; init; }

    /// <summary>
    /// « Rejected » (refusée avant engagement) ou « Cancelled » (dédite après).
    ///
    /// UN SEUL TYPE POUR LES DEUX, ET C'EST UN CHOIX. La conséquence en aval
    /// est identique au mot près : rendre le stock, rendre l'argent, prévenir le
    /// client. Deux types auraient obligé chacun des trois services à s'abonner
    /// deux fois — et le jour où l'un des abonnements serait oublié, la moitié
    /// des refus passerait à travers sans que rien n'échoue.
    /// </summary>
    public required string Outcome { get; init; }

    /// <summary>
    /// En clair, écrit par le vendeur. C'est la seule trace de pourquoi une
    /// commande payée ne sera pas honorée, et elle sera relue par un humain le
    /// jour où le client réclame.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Montant PAYÉ pour la part, remises comprises.
    ///
    /// CE N'EST PAS LE MONTANT À REMBOURSER. Le frais de port est porté par la
    /// COMMANDE : si ce refus la vide entièrement, le client doit aussi récupérer
    /// la livraison. Ce calcul appartient à qui possède le paiement.
    /// </summary>
    public required decimal Amount { get; init; }

    public required IReadOnlyCollection<SellerOrderRefusedLine> Lines { get; init; }
}
