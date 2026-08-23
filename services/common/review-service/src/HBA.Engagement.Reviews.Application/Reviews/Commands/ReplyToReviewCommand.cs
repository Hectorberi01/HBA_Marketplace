using HBA.Merchants.Contracts;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Engagement.Reviews.Application.Abstractions;
using HBA.Engagement.Reviews.Domain.Reviews;

namespace HBA.Engagement.Reviews.Application.Reviews.Commands;

/// <summary>Réponse publique du vendeur à un avis.</summary>
/// <remarks>
/// <c>CallerUserId</c> VIENT DU JETON, JAMAIS DU CORPS DE LA REQUÊTE.
///
/// C'est la même règle que pour <c>SubmitReviewCommand</c> : un identifiant
/// d'auteur accepté depuis le corps ne prouve rien, il se recopie.
/// </remarks>
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

        // ═════════════════════════════════════════════════════════════════════
        // LA GARDE QUI MANQUAIT — ET CE QU'ELLE FERMAIT.
        //
        // CETTE ROUTE ÉTAIT OUVERTE À TOUT COMPTE INSCRIT, ACHETEURS COMPRIS.
        //
        // La réponse s'affiche sous l'avis comme émanant du vendeur. Le handler
        // ne lisait aucune identité : le seul contrôle était la présence d'un
        // jeton, et les identifiants d'avis sont publics — la liste par produit
        // les rend à qui la demande. N'importe qui pouvait donc faire dire
        // n'importe quoi à n'importe quel vendeur, sous l'avis de son choix.
        //
        // 403 ET NON 404, contrairement aux gardes du catalogue.
        //
        // Là-bas le 404 évite de confirmer qu'une fiche existe. Ici l'avis est
        // public : `GET /reviews/{id}` le rend déjà à tout inscrit. Cacher son
        // existence ne protégerait rien et rendrait le refus incompréhensible
        // pour le vendeur légitime dont le dossier n'est pas encore résolu.
        //
        // AUCUN CONTOURNEMENT ADMINISTRATEUR ICI, ET C'EST DÉLIBÉRÉ.
        //
        // Les gardes de propriété du dépôt commencent toutes par
        // `if (IsAdmin(user)) return null` — un modérateur doit pouvoir corriger
        // le prix aberrant d'un vendeur injoignable. Écrire une réponse publique
        // n'est pas du même ordre : c'est une PAROLE ATTRIBUÉE à un commerçant.
        // La modération dispose de `flag`, `reject` et `restore` pour agir sur
        // les contenus ; elle n'a pas à en produire au nom d'autrui.
        //
        // CE N'EST PLUS « LE PROPRIÉTAIRE », C'EST « QUI A LA CAPACITÉ ».
        //
        // La première version comparait le vendeur résolu depuis le jeton au
        // `SellerId` de l'avis. Elle fermait le trou, et fermait aussi la route
        // aux MEMBRES — un chargé de clientèle porte `REVIEW_REPLY` par son rôle,
        // et c'est exactement son métier. La capacité répond aux deux questions à
        // la fois : est-ce bien ce vendeur, et ce compte a-t-il le droit.
        //
        // `storeId: null` — L'AVIS NE CONNAÎT PAS LA BOUTIQUE.
        //
        // `Review` porte un `ProductId` et un `SellerId`, jamais de boutique. Le
        // paramètre est transmis explicitement à `null` plutôt qu'omis : le jour
        // du cadrage par boutique, c'est ici qu'il faudra remonter l'information
        // depuis l'offre, et un `null` écrit se voit là où un défaut se devine.
        // ═════════════════════════════════════════════════════════════════════
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
