using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Reviews;

/// <summary>La décision rendue par l'administrateur (§16).</summary>
public enum ReviewDecision
{
    Approved = 0,
    Rejected = 1
}

/// <summary>Un motif de rejet, tel qu'il arrive du formulaire d'administration (§16).</summary>
public readonly record struct MotifDeRejet(string Code, string? Field, string Message);

/// <summary>Motifs de rejet standards.</summary>
public static class MotifsDeRejet
{
    public const string ImagesInvalides = "INVALID_IMAGES";
    public const string DescriptionInsuffisante = "INSUFFICIENT_DESCRIPTION";
    public const string CategorieIncorrecte = "WRONG_CATEGORY";
    public const string PrixSuspect = "SUSPICIOUS_PRICE";
    public const string ContenuInterdit = "PROHIBITED_CONTENT";
}

/// <summary>Un motif attaché à une décision.</summary>
public sealed class ProductReviewReason : Entity<Guid>
{
    private ProductReviewReason()
    {
    }

    internal ProductReviewReason(Guid id, Guid reviewId, string code, string? field, string message)
        : base(id)
    {
        ReviewId = reviewId;
        Code = code;
        Field = field;
        Message = message;
    }

    public Guid ReviewId { get; private set; }

    /// <summary>Code stable, en SCREAMING_SNAKE. Voir <see cref="MotifsDeRejet"/>.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Le champ visé — « images », « description »… Nul si le motif est global.</summary>
    public string? Field { get; private set; }

    /// <summary>Le message destiné au vendeur, en clair.</summary>
    public string Message { get; private set; } = string.Empty;

    internal void AttacherA(Guid reviewId) => ReviewId = reviewId;
}

/// <summary>
/// UNE DÉCISION D'ADMINISTRATION SUR UNE RÉVISION — TABLE <c> product_reviews</c>.
/// </summary>
public sealed class ProductReview : AggregateRoot<Guid>
{
    private readonly List<ProductReviewReason> _reasons = new();

    private ProductReview()
    {
    }

    private ProductReview(
        Guid id,
        Guid productId,
        Guid revisionId,
        int revisionVersion,
        Guid sellerId,
        Guid reviewedBy,
        ReviewDecision decision,
        string? comment,
        DateTimeOffset reviewedAtUtc)
        : base(id)
    {
        ProductId = productId;
        RevisionId = revisionId;
        RevisionVersion = revisionVersion;
        SellerId = sellerId;
        ReviewedBy = reviewedBy;
        Decision = decision;
        Comment = comment;
        ReviewedAtUtc = reviewedAtUtc;
    }

    public Guid ProductId { get; private set; }

    /// <summary>La révision jugée.</summary>
    public Guid RevisionId { get; private set; }

    /// <summary>Le numéro de version, recopié pour que la file se lise sans jointure.</summary>
    public int RevisionVersion { get; private set; }

    /// <summary>Le vendeur, recopié pour la même raison — la file affiche son nom.</summary>
    public Guid SellerId { get; private set; }

    public Guid ReviewedBy { get; private set; }
    public ReviewDecision Decision { get; private set; }
    public string? Comment { get; private set; }
    public DateTimeOffset ReviewedAtUtc { get; private set; }

    public IReadOnlyCollection<ProductReviewReason> Reasons => _reasons.AsReadOnly();

    public static Result<ProductReview> Approbation(
        Guid productId,
        Guid revisionId,
        int revisionVersion,
        Guid sellerId,
        Guid reviewedBy,
        string? comment,
        DateTimeOffset nowUtc)
    {
        if (reviewedBy == Guid.Empty)
        {
            return Error.Validation("catalog.review.reviewer_required", "La décision doit désigner son auteur.");
        }

        return new ProductReview(
            Guid.NewGuid(), productId, revisionId, revisionVersion, sellerId, reviewedBy,
            ReviewDecision.Approved, Nettoyer(comment), nowUtc);
    }

    /// <summary>UN REJET SANS MOTIF EST REFUSÉ.</summary>
    public static Result<ProductReview> Rejet(
        Guid productId,
        Guid revisionId,
        int revisionVersion,
        Guid sellerId,
        Guid reviewedBy,
        string? comment,
        IEnumerable<MotifDeRejet> motifs,
        DateTimeOffset nowUtc)
    {
        if (reviewedBy == Guid.Empty)
        {
            return Error.Validation("catalog.review.reviewer_required", "La décision doit désigner son auteur.");
        }

        var declares = (motifs ?? Enumerable.Empty<MotifDeRejet>()).ToList();

        if (declares.Count == 0)
        {
            return Error.Validation(
                "catalog.review.reason_required",
                "Un rejet doit indiquer au moins un motif : sans cela le vendeur ne sait pas quoi corriger.");
        }

        var review = new ProductReview(
            Guid.NewGuid(), productId, revisionId, revisionVersion, sellerId, reviewedBy,
            ReviewDecision.Rejected, Nettoyer(comment), nowUtc);

        foreach (var motif in declares)
        {
            if (string.IsNullOrWhiteSpace(motif.Code))
            {
                return Error.Validation("catalog.review.reason_code_required", "Chaque motif doit porter un code.");
            }

            if (string.IsNullOrWhiteSpace(motif.Message))
            {
                return Error.Validation(
                    "catalog.review.reason_message_required",
                    "Chaque motif doit porter un message lisible par le vendeur.");
            }

            review._reasons.Add(new ProductReviewReason(
                Guid.NewGuid(),
                review.Id,
                // Normalisé : le client mobile compare des codes, pas de la casse.
                motif.Code.Trim().ToUpperInvariant().Replace(' ', '_'),
                Nettoyer(motif.Field),
                motif.Message.Trim()));
        }

        return review;
    }

    private static string? Nettoyer(string? valeur)
        => string.IsNullOrWhiteSpace(valeur) ? null : valeur.Trim();
}
