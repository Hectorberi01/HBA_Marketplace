using HBA.Shared.Application.Messaging;

namespace HBA.Engagement.Reviews.Application.Reviews.Commands.SubmitReview;

/// <summary>Dépose un avis sur un produit d'une commande confirmée de l'acheteur.</summary>
public sealed record SubmitReviewCommand(
    Guid BuyerId, Guid ProductId, Guid OrderId, int Rating, string Title, string Body) : ICommand<Guid>;
