namespace HBA.Marketplace.ReturnRefund.Domain.Enums;

public enum ReturnStatus
{
    Requested = 0,
    EligibilityCheck = 1,
    AwaitingApproval = 2,
    Approved = 3,
    AwaitingReturn = 4,
    InReturnTransit = 5,
    Received = 6,
    InspectionPending = 7,
    RefundPending = 8,
    Refunded = 9,
    Closed = 10,
    Rejected = 11,
    RejectedAfterInspection = 12,
    Cancelled = 13,
    Expired = 14,
    ManualReview = 15
}

public enum RefundStatus
{
    Pending = 0,
    Processing = 1,
    Succeeded = 2,

    /// <summary>
    /// ÉTAT INATTEIGNABLE : aucun chemin ne rend un remboursement PARTIELLEMENT
    /// réussi (lot 9.2).
    ///
    /// Une tentative aboutit ou échoue en bloc. Le cas qu'il décrit — plusieurs
    /// tentatives dont certaines seulement ont abouti — est pourtant possible :
    /// `RefundAttempt` est une collection. Tant que rien ne le pose, un
    /// remboursement à deux tentatives dont une a réussi se lit `Failed`, et
    /// l'argent déjà rendu ne se voit qu'en descendant dans les tentatives.
    /// </summary>
    PartiallySucceeded = 3,
    Failed = 4,
    Cancelled = 5
}

/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUNE DES CINQ VALEURS N'EST JAMAIS POSÉE (lot 9.2).
///
/// La résolution d'un retour n'est décidée nulle part : le parcours va du retour
/// approuvé au remboursement exécuté sans jamais nommer CE QUI a été décidé.
/// Remplacement et remboursement partiel n'existent donc pas — c'est cohérent
/// avec le reste du service, qui ne sait faire qu'un remboursement total.
///
/// ET `Refund` ET `RefundOnly` SONT DEUX NOMS POUR LA MÊME CHOSE.
///
/// Le doublon ne se voit pas tant que personne n'assigne : le jour où quelqu'un
/// écrira la décision, il choisira l'un des deux au hasard, et les lectures
/// filtreront sur l'autre. C'est le genre d'écart qui ne casse rien et fausse
/// tout — un écran qui compte les remboursements en oubliera la moitié.
///
/// À trancher AVANT le premier usage, pas après.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public enum ReturnResolution
{
    Refund = 0,
    RefundOnly = 1,
    Replacement = 2,
    PartialRefund = 3,
    Reject = 4
}

public enum ReturnReasonCode
{
    Defective = 0,
    DamagedOnArrival = 1,
    WrongItem = 2,
    NotAsDescribed = 3,
    MissingParts = 4,
    SizeNotFit = 5,
    QualityNotExpected = 6,
    ChangedMind = 7,
    DuplicateOrder = 8,
    Other = 9
}

public enum InspectionCondition
{
    Unknown = 0,
    Sealed = 1,
    New = 2,
    OpenedGood = 3,
    Damaged = 4,
    CustomerDamaged = 5,
    MissingParts = 6
}

public enum StockDisposition
{
    None = 0,
    RestockSellable = 1,
    RestockOpenBox = 2,
    Quarantine = 3,
    Dispose = 4
}
