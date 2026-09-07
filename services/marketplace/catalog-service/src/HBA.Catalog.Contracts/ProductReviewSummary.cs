namespace HBA.Catalog.Contracts;

/// <summary>Un motif de rejet, tel que le vendeur le reçoit (§16).</summary>
public sealed record ProductReviewReasonSummary(
    string Code,
    string? Field,
    string Message);

/// <summary>Une décision d'administration rendue sur une révision (§16).</summary>
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
