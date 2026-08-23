namespace HBA.FoodOrders.Domain.Orders;

/// <summary>Identité forte d'une commande de repas.</summary>
public readonly record struct MealOrderId(Guid Value)
{
    public static MealOrderId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// États de la commande de repas.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI IL N'Y A NI « RÉSERVÉ » NI « EXPÉDIÉ », ET POURQUOI LA DÉCISION
///    DU RESTAURANT VIENT APRÈS LE PAIEMENT.
///
/// La marketplace réserve du stock avant d'encaisser : la vente n'est certaine
/// qu'une fois l'article mis de côté. Un plat ne se réserve pas — il se cuisine,
/// et il n'existe pas avant d'être commandé. La chronologie s'inverse donc : le
/// client paie, la commande est confirmée, ET ALORS la cuisine accepte ou refuse.
///
/// Un refus n'est pas un incident, c'est une issue prévue — plus de riz, four en
/// panne, fermeture imprévue. C'est ce qui rend
/// <see cref="MealOrder.RejectByRestaurant"/> nécessaire : `Cancel` refuse à
/// juste titre d'annuler une commande confirmée, parce que côté marchandise cela
/// reviendrait à défaire une vente conclue.
///
/// ET IL N'Y A PAS DE STATUT DE CUISINE ICI.
///
/// « Acceptée », « en préparation », « prête », « retirée » appartiennent à
/// `FoodOrder`, dans restaurant-service, qui tient le ticket et ses postes. Les
/// recopier ici donnerait deux vérités sur le même repas, et il faudrait décider
/// laquelle ment. Celui-ci est le statut COMMERCIAL : ce que le client a payé et
/// ce qu'il en advient.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum MealOrderStatus
{
    Pending = 0,
    AwaitingPayment = 1,
    Paid = 2,
    Confirmed = 3,
    Cancelled = 4,
    Failed = 5,
    Delivered = 6,

    /// <summary>
    /// Payée mais plus exécutable : elle attend un arbitrage humain.
    ///
    /// PAS UN REMBOURSEMENT AUTOMATIQUE. Une course annulée est le plus
    /// souvent réattribuable — livreur en panne, refus après acceptation, erreur
    /// de dispatch. Rembourser d'office détruirait des ventes récupérables, et
    /// l'argent rendu ne se reprend pas. L'exploitation tranche : relancer ou
    /// retourner.
    /// </summary>
    UnderReview = 7
}
