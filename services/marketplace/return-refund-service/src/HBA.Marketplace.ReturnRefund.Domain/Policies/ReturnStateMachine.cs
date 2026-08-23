using HBA.Marketplace.ReturnRefund.Domain.Enums;

namespace HBA.Marketplace.ReturnRefund.Domain.Policies;

public static class ReturnStateMachine
{
    private static readonly IReadOnlyDictionary<ReturnStatus, ReturnStatus[]> Transitions =
        new Dictionary<ReturnStatus, ReturnStatus[]>
        {
            [ReturnStatus.Requested] = [ReturnStatus.EligibilityCheck, ReturnStatus.AwaitingApproval, ReturnStatus.Approved, ReturnStatus.Rejected, ReturnStatus.Cancelled],
            [ReturnStatus.EligibilityCheck] = [ReturnStatus.AwaitingApproval, ReturnStatus.Approved, ReturnStatus.Rejected, ReturnStatus.ManualReview],
            [ReturnStatus.AwaitingApproval] = [ReturnStatus.Approved, ReturnStatus.Rejected, ReturnStatus.Cancelled, ReturnStatus.Expired],
            [ReturnStatus.Approved] = [ReturnStatus.AwaitingReturn, ReturnStatus.RefundPending, ReturnStatus.Cancelled],
            [ReturnStatus.AwaitingReturn] = [ReturnStatus.InReturnTransit, ReturnStatus.Received, ReturnStatus.Expired],
            [ReturnStatus.InReturnTransit] = [ReturnStatus.Received, ReturnStatus.ManualReview],
            [ReturnStatus.Received] = [ReturnStatus.InspectionPending, ReturnStatus.RefundPending, ReturnStatus.RejectedAfterInspection],
            [ReturnStatus.InspectionPending] = [ReturnStatus.RefundPending, ReturnStatus.RejectedAfterInspection],
            [ReturnStatus.RefundPending] = [ReturnStatus.Refunded, ReturnStatus.ManualReview],
            [ReturnStatus.Refunded] = [ReturnStatus.Closed],
            [ReturnStatus.ManualReview] = [ReturnStatus.Approved, ReturnStatus.Rejected, ReturnStatus.RefundPending, ReturnStatus.Closed],
            [ReturnStatus.Rejected] = [ReturnStatus.Closed],
            [ReturnStatus.RejectedAfterInspection] = [ReturnStatus.Closed],
            [ReturnStatus.Cancelled] = [ReturnStatus.Closed],
            [ReturnStatus.Expired] = [ReturnStatus.Closed],
            [ReturnStatus.Closed] = []
        };

    /// <summary>
    /// Cette transition est-elle permise ?
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// `from == to` ÉTAIT ACCEPTÉ. LE CLIENT POUVAIT ÊTRE REMBOURSÉ DEUX FOIS.
    ///
    /// La méthode s'écrivait :
    ///
    ///     from == to || Transitions.TryGetValue(from, out var allowed) &amp;&amp; allowed.Contains(to)
    ///
    /// La table, elle, ne déclare AUCUNE boucle : `RefundPending` mène à
    /// `Refunded` ou `ManualReview`, jamais à `RefundPending`. Le premier terme
    /// rouvrait donc, pour les seize états, une porte que la table refermait.
    ///
    /// Ce qu'il coûtait, très concrètement : un dossier déjà passé en
    /// `RefundPending` — donc portant déjà un remboursement décidé, en attente de
    /// versement — acceptait un SECOND `DecideRefund`. La transition « passe »
    /// puisqu'elle ne bouge pas, un deuxième `Refund` est écrit, et le vendeur
    /// n'a rien vu d'autre qu'un double-clic.
    ///
    /// Combiné à `TotalRefunded()` qui ignorait les remboursements `Pending`
    /// (voir `ReturnRequest`), le plafond ne voyait rien non plus : les deux
    /// décisions lisaient « rien encore remboursé » et validaient chacune la
    /// TOTALITÉ du montant.
    ///
    /// CE QUE CE DURCISSEMENT CHANGE POUR LES APPELANTS.
    ///
    /// Rejouer un geste déjà accompli rend désormais un échec `Conflict` au lieu
    /// d'un succès silencieux. C'est le comportement voulu — un « oui » rendu
    /// pour une opération qui n'a rien fait est la pire réponse possible sur un
    /// mouvement d'argent. Le seul appelant qui s'appuyait sur la boucle,
    /// `ReturnRequest.Inspect`, teste déjà l'état courant après l'échec et
    /// continue : une seconde inspection reste possible, comme avant.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public static bool CanTransition(ReturnStatus from, ReturnStatus to)
        => Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
}
