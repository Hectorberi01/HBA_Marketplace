using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Domain.Products;
using HBA.Catalog.Domain.Reviews;

namespace HBA.Catalog.Application.Reviews;

// LES QUATRE DÉCISIONS D'ADMINISTRATION (§16).

/// <summary>Approuve la révision courante (§16 : POST /products/{id}/approve).</summary>
public sealed record ApproveProductCommand(
    Guid ProductId,
    Guid ReviewedBy,
    string? Comment = null) : ICommand;

/// <summary>Un motif de rejet reçu du client, en chaînes.</summary>
public sealed record MotifSaisi(string Code, string? Field, string Message);

/// <summary>
/// Rejette la révision courante avec ses motifs (§16 : POST /products/{id}/reject).
/// </summary>
public sealed record RejectProductCommand(
    Guid ProductId,
    Guid ReviewedBy,
    string? Comment,
    IReadOnlyList<MotifSaisi> Reasons) : ICommand;

/// <summary>Retire la fiche de la vente par décision de la plateforme (§16).</summary>
public sealed record SuspendProductCommand(Guid ProductId, string? Reason) : ICommand;

/// <summary>Lève une suspension (§16).</summary>
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

        // LA TRACE EST CONSTRUITE AVANT LA TRANSITION, ICI, ET C'EST L'INVERSE DE
        // L'APPROBATION.
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
