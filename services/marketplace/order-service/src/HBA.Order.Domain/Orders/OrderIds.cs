namespace HBA.Orders.Domain.Orders;

/// <summary>Identité forte d'une commande.</summary>
public readonly record struct OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// États de la commande, pilotés par le Saga d'orchestration : création →
/// réservation du stock → paiement → confirmation, avec compensations.
/// </summary>
public enum OrderStatus
{
    Pending = 0,
    AwaitingPayment = 1,
    Paid = 2,
    Confirmed = 3,
    Cancelled = 4,
    Failed = 5,
    Delivered = 6,

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA COMMANDE EST PAYÉE MAIS PLUS EXÉCUTABLE : ELLE ATTEND UN ARBITRAGE.
    ///
    /// SANS CET ÉTAT, LA SAGA N'AVAIT AUCUNE SORTIE DE SECOURS.
    ///
    /// Deux chemins amenaient une commande confirmée à devenir inexécutable, et
    /// aucun des deux n'aboutissait nulle part :
    ///
    ///   • la course était annulée — `DeliveryCancelled` n'avait qu'un seul
    ///     consommateur, le webhook partenaire de delivery-service. Rien ne
    ///     remontait à order-service ;
    ///   • la commande partait de PLUSIEURS lieux d'expédition, cas que
    ///     `CreateDeliveryOnOrderConfirmedHandler` refuse à juste titre — mais il
    ///     s'arrêtait sur un `return;` après un journal.
    ///
    /// Dans les deux cas la commande restait `Confirmed` POUR TOUJOURS : ni
    /// livraison, ni annulation, ni remboursement, escrow gelé, stock déjà
    /// décrémenté, et un acheteur qui attend un colis que personne n'apportera.
    ///
    /// POURQUOI PAS UN REMBOURSEMENT AUTOMATIQUE.
    ///
    /// Une course annulée est très souvent RÉATTRIBUABLE : livreur en panne,
    /// refus après acceptation, erreur de dispatch. Rembourser d'office
    /// détruirait des ventes parfaitement récupérables — et l'argent rendu ne se
    /// reprend pas. L'état dit donc « ce n'est plus en cours, quelqu'un doit
    /// trancher », et c'est l'exploitation qui choisit entre relancer et
    /// rembourser.
    ///
    /// VALEUR 7, EN QUEUE D'ÉNUMÉRATION. L'insérer au milieu décalerait toutes
    /// les suivantes. La colonne est stockée en TEXTE (voir `OrderConfiguration`),
    /// ce qui la protège déjà — mais les deux précautions ne coûtent rien et la
    /// seconde ne dépend pas d'un réglage de mapping.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    UnderReview = 7
}
