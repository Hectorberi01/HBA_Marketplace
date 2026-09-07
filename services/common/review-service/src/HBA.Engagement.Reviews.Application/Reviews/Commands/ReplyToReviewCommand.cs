using HBA.Merchants.Contracts;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Engagement.Reviews.Application.Abstractions;
using HBA.Engagement.Reviews.Domain.Reviews;

namespace HBA.Engagement.Reviews.Application.Reviews.Commands;

/// <summary>Réponse publique du vendeur à un avis.</summary>
public sealed record ReplyToReviewCommand(Guid ReviewId, Guid CallerUserId, string Body) : ICommand;

internal sealed class ReplyToReviewCommandHandler : ICommandHandler<ReplyToReviewCommand>
{
    private readonly IReviewRepository _repository;
    private readonly IReviewsUnitOfWork _unitOfWork;
    private readonly IMerchantAccessApi _access;

    public ReplyToReviewCommandHandler(
        IReviewRepository repository,
        IReviewsUnitOfWork unitOfWork,
        IMerchantAccessApi access)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _access = access;
    }

    public async Task<Result> Handle(ReplyToReviewCommand command, CancellationToken cancellationToken)
    {
        var review = await _repository.GetByIdAsync(new ReviewId(command.ReviewId), cancellationToken);
        if (review is null)
        {
            return Result.Failure(Error.NotFound("reviews.not_found", "Avis introuvable."));
        }

        // LA GARDE QUI MANQUAIT — ET CE QU'ELLE FERMAIT.
        var autorise = await _access.HasCapabilityAsync(
            command.CallerUserId,
            review.SellerId,
            storeId: null,
            MerchantCapabilities.ReviewReply,
            cancellationToken);

        if (!autorise)
        {
            return Result.Failure(Error.Forbidden(
                "reviews.reply.not_seller",
                "Seul le vendeur concerné, ou un membre habilité de son équipe, peut répondre à cet avis."));
        }

        var reply = review.Reply(command.Body);
        if (reply.IsFailure)
        {
            return reply;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
