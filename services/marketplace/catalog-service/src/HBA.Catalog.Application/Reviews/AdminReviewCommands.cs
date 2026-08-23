using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Domain.Products;
using HBA.Catalog.Domain.Reviews;

namespace HBA.Catalog.Application.Reviews;

// ═════════════════════════════════════════════════════════════════════════════
// LES QUATRE DÉCISIONS D'ADMINISTRATION (§16).
//
// CES COMMANDES DÉBLOQUENT TOUT LE RESTE.
//
// `Product.Approve`, `Reject`, `Suspend` et `Restore` existaient depuis le lot 1,
// testés, et n'étaient appelés par PERSONNE. Conséquence : une fiche soumise ne
// pouvait jamais être approuvée, et le parcours du §28 s'arrêtait à l'étape 4.
// `ChangeProductStatusCommandHandler` renvoyait d'ailleurs le vendeur vers « l'API
// admin » — qui n'existait pas.
//
// APPROBATION ET REJET ÉCRIVENT DANS DEUX AGRÉGATS, EN UNE TRANSACTION.
//
// Le produit avance, et une décision est journalisée. Les deux passent par le même
// `SaveChangesAsync` : une approbation qui n'écrirait pas sa trace, ou une trace
// sans changement de statut, laisserait la file de validation et la fiche en
// désaccord — et c'est la file que l'administrateur suivant regardera.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Approuve la révision courante (§16 : POST /products/{id}/approve).</summary>
public sealed record ApproveProductCommand(
    Guid ProductId,
    Guid ReviewedBy,
    string? Comment = null) : ICommand;

/// <summary>Un motif de rejet reçu du client, en chaînes.</summary>
public sealed record MotifSaisi(string Code, string? Field, string Message);

/// <summary>Rejette la révision courante avec ses motifs (§16 : POST /products/{id}/reject).</summary>
public sealed record RejectProductCommand(
    Guid ProductId,
    Guid ReviewedBy,
    string? Comment,
    IReadOnlyList<MotifSaisi> Reasons) : ICommand;

/// <summary>Retire la fiche de la vente par décision de la plateforme (§16).</summary>
public sealed record SuspendProductCommand(Guid ProductId, string? Reason) : ICommand;

/// <summary>Lève une suspension (§16). La fiche revient à APPROVED, pas à PUBLISHED.</summary>
public sealed record RestoreProductCommand(Guid ProductId) : ICommand;

internal sealed class AdminReviewCommandHandler
    : ICommandHandler<ApproveProductCommand>,
      ICommandHandler<RejectProductCommand>,
      ICommandHandler<SuspendProductCommand>,
      ICommandHandler<RestoreProductCommand>
{
    private readonly IProductRepository _products;
    private readonly IProductReviewRepository _reviews;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public AdminReviewCommandHandler(
        IProductRepository products,
        IProductReviewRepository reviews,
        ICatalogUnitOfWork unitOfWork)
    {
        _products = products;
        _reviews = reviews;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ApproveProductCommand command, CancellationToken cancellationToken)
    {
        var product = await Charger(command.ProductId, cancellationToken);
        if (product is null)
        {
            return Introuvable(command.ProductId);
        }

        var maintenant = DateTimeOffset.UtcNow;
        var revision = product.CurrentRevision;

        // L'AGRÉGAT D'ABORD, LA TRACE ENSUITE.
        //
        // `Approve` refuse si la révision n'attend pas de décision. Journaliser
        // avant produirait une décision sur une fiche qui ne l'a jamais reçue —
        // une ligne d'audit qui ment, ce qui est pire qu'une ligne absente.
        var transition = product.Approve(command.ReviewedBy, maintenant);
        if (transition.IsFailure)
        {
            return transition;
        }

        var review = ProductReview.Approbation(
            product.Id.Value, revision.Id, revision.Version,
            product.SellerId, command.ReviewedBy, command.Comment, maintenant);

        if (review.IsFailure)
        {
            return Result.Failure(review.Error);
        }

        await _reviews.AddAsync(review.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> Handle(RejectProductCommand command, CancellationToken cancellationToken)
    {
        var product = await Charger(command.ProductId, cancellationToken);
        if (product is null)
        {
            return Introuvable(command.ProductId);
        }

        var maintenant = DateTimeOffset.UtcNow;
        var revision = product.CurrentRevision;

        // LA TRACE EST CONSTRUITE AVANT LA TRANSITION, ICI, ET C'EST L'INVERSE
        //    DE L'APPROBATION.
        //
        // Un rejet sans motif est refusé par `ProductReview.Rejet`. Faire avancer
        // l'agrégat d'abord mettrait la fiche en REJECTED puis échouerait sur les
        // motifs : le vendeur verrait sa fiche refusée sans qu'aucun motif ne soit
        // enregistré — exactement le défaut que ce lot corrige. On valide donc la
        // décision AVANT de toucher au produit, et on ne l'enregistre qu'après.
        var review = ProductReview.Rejet(
            product.Id.Value, revision.Id, revision.Version,
            product.SellerId, command.ReviewedBy, command.Comment,
            (command.Reasons ?? Array.Empty<MotifSaisi>())
                .Select(m => new MotifDeRejet(m.Code, m.Field, m.Message)),
            maintenant);

        if (review.IsFailure)
        {
            return Result.Failure(review.Error);
        }

        var transition = product.Reject(command.ReviewedBy, maintenant);
        if (transition.IsFailure)
        {
            return transition;
        }

        await _reviews.AddAsync(review.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> Handle(SuspendProductCommand command, CancellationToken cancellationToken)
    {
        var product = await Charger(command.ProductId, cancellationToken);
        if (product is null)
        {
            return Introuvable(command.ProductId);
        }

        var transition = product.Suspend(command.Reason);
        if (transition.IsFailure)
        {
            return transition;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> Handle(RestoreProductCommand command, CancellationToken cancellationToken)
    {
        var product = await Charger(command.ProductId, cancellationToken);
        if (product is null)
        {
            return Introuvable(command.ProductId);
        }

        var transition = product.Restore();
        if (transition.IsFailure)
        {
            return transition;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private Task<Product?> Charger(Guid productId, CancellationToken cancellationToken)
        => _products.GetByIdAsync(new ProductId(productId), cancellationToken);

    private static Result Introuvable(Guid productId)
        => Result.Failure(Error.NotFound("catalog.product.not_found", $"Produit {productId} introuvable."));
}
