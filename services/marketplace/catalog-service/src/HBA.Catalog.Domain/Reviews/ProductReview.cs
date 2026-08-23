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

/// <summary>
/// Motifs de rejet standards.
///
/// CE N'EST PAS UNE LISTE FERMÉE, ET C'EST DÉLIBÉRÉ.
///
/// Les cinq ci-dessous couvrent l'essentiel et donnent au client mobile de quoi
/// afficher un message traduit sans lire le texte libre. Mais un administrateur
/// rencontrera des cas qu'aucune énumération n'aura prévus, et lui imposer un code
/// existant le ferait choisir le moins faux — l'information serait perdue pour de
/// bon. Le code est donc une chaîne libre, normalisée, avec ces valeurs comme
/// vocabulaire commun.
/// </summary>
public static class MotifsDeRejet
{
    public const string ImagesInvalides = "INVALID_IMAGES";
    public const string DescriptionInsuffisante = "INSUFFICIENT_DESCRIPTION";
    public const string CategorieIncorrecte = "WRONG_CATEGORY";
    public const string PrixSuspect = "SUSPICIOUS_PRICE";
    public const string ContenuInterdit = "PROHIBITED_CONTENT";
}

/// <summary>
/// Un motif attaché à une décision. Table <c>product_review_reasons</c> (§20).
/// </summary>
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
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE DÉCISION D'ADMINISTRATION SUR UNE RÉVISION — TABLE <c>product_reviews</c>.
///
/// CETTE TABLE ÉTAIT CITÉE PAR TROIS COMMENTAIRES DU CODE ET N'EXISTAIT PAS.
///
/// `ProductLifecycleIntegrationEvents`, `Product.Reject` et `ProductStatus`
/// renvoyaient tous vers « ProductReview, où vivent les motifs ». Conséquence : un
/// rejet ne conservait AUCUN motif. Le vendeur apprenait que sa fiche était
/// refusée, jamais pourquoi — et l'administrateur qui reprenait le dossier une
/// semaine plus tard ne le savait pas davantage.
///
/// AGRÉGAT À PART, PAS UNE COLLECTION DANS `Product`.
///
/// Une fiche très retravaillée accumule des dizaines de décisions. Les porter dans
/// l'agrégat produit ferait grossir sans borne ce que chaque chargement lit — or
/// `Product` est déjà chargé avec ses révisions, ses variantes et ses médias, à
/// chaque écriture. Et la file de validation du §16 se lit PAR DÉCISION, pas par
/// produit : elle n'a aucune raison de traverser l'agrégat.
///
/// ELLE EST IMMUABLE APRÈS CRÉATION.
///
/// Une décision rendue ne se modifie pas : on en rend une nouvelle. C'est ce qui
/// fait de cette table un journal exploitable — sans quoi « qui a approuvé cette
/// fiche, et sur quel contenu » deviendrait une question sans réponse.
/// ═════════════════════════════════════════════════════════════════════════════
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

    /// <summary>
    /// La révision jugée.
    ///
    /// C'EST CE CHAMP QUI DONNE SA VALEUR À LA TABLE.
    ///
    /// Sans lui, on saurait qu'une fiche a été approuvée, pas SUR QUEL CONTENU.
    /// Une fiche modifiée trois fois après une approbation rendrait la décision
    /// illisible — et un litige sur ce qu'un administrateur avait réellement validé
    /// deviendrait inarbitrable.
    /// </summary>
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

    /// <summary>
    /// UN REJET SANS MOTIF EST REFUSÉ. C'EST L'INVARIANT DE CETTE CLASSE.
    ///
    /// Le §16 montre un rejet avec un tableau `reasons`, sans dire qu'il est
    /// obligatoire. Le rendre facultatif produirait exactement ce que ce lot
    /// corrige : un vendeur qui apprend que sa fiche est refusée sans savoir quoi
    /// changer, resoumet à l'identique, et occupe la file une seconde fois.
    ///
    /// Le commentaire libre, lui, reste facultatif : il ajoute du contexte, il ne
    /// remplace pas un motif qu'un client puisse traduire.
    /// </summary>
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
