using HBA.Shared.Application.Abstractions;
using HBA.Media.Contracts;
using HBA.Merchants.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Application.Members;

namespace HBA.Merchants.Application.Sellers.Commands.AddKybDocument;

/// <summary>RATTACHE UNE PIÈCE KYB DÉJÀ TÉLÉVERSÉE — APRÈS AVOIR VÉRIFIÉ À QUI ELLE EST.</summary>
internal sealed class AddKybDocumentCommandHandler : ICommandHandler<AddKybDocumentCommand, Guid>
{
    /// <summary>La nature imposée par le §12 pour une pièce légale de vendeur.</summary>
    private const string NatureAttendue = "SellerDocument";

    /// <summary>
    /// Le propriétaire attendu, côté media-service (<c>MediaOwnerType.Seller</c>).
    /// </summary>
    private const string ProprietaireAttendu = "Seller";

    private readonly ISellerRepository _sellerRepository;
    private readonly IMediaModuleApi _media;
    private readonly MemberAccessResolver _acces;
    private readonly ISellerUnitOfWork _unitOfWork;

    public AddKybDocumentCommandHandler(
        ISellerRepository sellerRepository,
        IMediaModuleApi media,
        MemberAccessResolver acces,
        ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _media = media;
        _acces = acces;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(AddKybDocumentCommand command, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (seller is null)
        {
            return Error.NotFound("sellers.seller.not_found", $"Vendeur {command.SellerId} introuvable.");
        }

        if (!Enum.TryParse<KybDocumentType>(command.Type, ignoreCase: true, out var type))
        {
            return Error.Validation("sellers.kyb.type_invalid", "Type de pièce KYB invalide.");
        }

        var media = await _media.GetAsync(command.MediaId, cancellationToken);

        if (media is null)
        {
            return Error.NotFound(
                "sellers.kyb.media_not_found",
                "Ce fichier n'existe pas. Téléversez la pièce avant de la rattacher au dossier.");
        }

        // LE CONTRÔLE QUI FERME LES DEUX EXPLOITATIONS.
        if (!string.Equals(media.OwnerType, ProprietaireAttendu, StringComparison.OrdinalIgnoreCase)
            || media.OwnerId != command.SellerId)
        {
            return Error.Forbidden(
                "sellers.kyb.media_not_owned",
                "Ce fichier n'appartient pas à ce dossier vendeur.");
        }

        // ET LE DÉPOSANT DOIT ÊTRE DE CETTE ÉQUIPE — CE QUE LE CONTRÔLE CI-DESSUS
        // NE DIT PAS.
        if (command.RequestedByUserId == Guid.Empty)
        {
            return Error.Forbidden(
                "sellers.kyb.media_uploader_unknown",
                "Appelant inconnu : impossible de vérifier qui a déposé ce fichier.");
        }

        if (media.CreatedByUserId != command.RequestedByUserId)
        {
            var deposant = await _acces.ResolveAsync(
                command.SellerId, media.CreatedByUserId, cancellationToken);

            if (deposant.IsFailure)
            {
                return Error.Forbidden(
                    "sellers.kyb.media_not_uploader",
                    "Ce fichier a été déposé par un compte étranger à ce dossier vendeur.");
            }
        }

        // LA NATURE, EN PLUS DE LA PROPRIÉTÉ — ET LES DEUX SERVENT.
        if (!string.Equals(media.MediaType, NatureAttendue, StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation(
                "sellers.kyb.media_wrong_kind",
                $"Ce fichier est de nature « {media.MediaType} » ; un dossier KYB attend une pièce légale.");
        }

        // UN MÉDIA PAS ENCORE PRÊT N'EST PAS UN MÉDIA ABSENT.
        if (!string.Equals(media.Status, "Ready", StringComparison.OrdinalIgnoreCase))
        {
            return Error.BusinessRule(
                "sellers.kyb.media_not_ready",
                "Ce fichier est encore en cours de traitement. Réessayez dans quelques instants.");
        }

        var result = seller.AddKybDocument(type, command.MediaId);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result.Value.Id;
    }
}
