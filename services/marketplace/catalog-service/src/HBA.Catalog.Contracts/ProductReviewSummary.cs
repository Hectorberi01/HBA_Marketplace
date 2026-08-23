namespace HBA.Catalog.Contracts;

/// <summary>Un motif de rejet, tel que le vendeur le reçoit (§16).</summary>
public sealed record ProductReviewReasonSummary(
    string Code,
    string? Field,
    string Message);

/// <summary>
/// Une décision d'administration rendue sur une révision (§16).
///
/// `RevisionId` ET `RevisionVersion` NE SONT PAS DÉCORATIFS.
///
/// Un vendeur qui reçoit un rejet a souvent déjà modifié sa fiche entre-temps. Sans
/// le numéro de version, il ne sait pas si les motifs portent sur ce qu'il voit à
/// l'écran ou sur ce qu'il a soumis trois jours plus tôt — et il corrige à
/// l'aveugle.
/// </summary>
public sealed record ProductReviewSummary(
    Guid Id,
    Guid ProductId,
    Guid RevisionId,
    int RevisionVersion,
    Guid SellerId,
    Guid ReviewedBy,
    string Decision,
    string? Comment,
    DateTimeOffset ReviewedAtUtc,
    IReadOnlyList<ProductReviewReasonSummary> Reasons);
