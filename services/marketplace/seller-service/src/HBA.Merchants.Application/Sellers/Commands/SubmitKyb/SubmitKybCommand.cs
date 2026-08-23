using HBA.Shared.Application.Abstractions;
using HBA.Merchants.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Domain.Sellers;

namespace HBA.Merchants.Application.Sellers.Commands.SubmitKyb;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE VENDEUR DÉCLARE SON DOSSIER COMPLET (§10.3 : POST /kyc/submit).
///
/// CE GESTE N'EXISTAIT PAS, ET SON ABSENCE COÛTAIT DES DEUX CÔTÉS.
///
/// Le passage en validation était un EFFET DE BORD du dépôt de la première pièce.
/// Le vendeur qui téléverse sa carte d'identité un lundi et son registre de
/// commerce le jeudi occupait la file d'un administrateur pendant trois jours avec
/// un dossier incomplet — que celui-ci ne pouvait que refuser. Et rien ne disait
/// au vendeur quand il avait fini : il déposait, et espérait.
///
/// LE CAHIER PASSE UNE LISTE DE `documentIds`. ON NE LA PREND PAS.
///
/// Le §10.3 montre `{ "documentIds": ["doc_1", "doc_2"] }`. La reprendre telle
/// quelle poserait une question sans réponse : que faire des pièces DÉJÀ déposées
/// qui n'y figurent pas ? Les retirer serait destructeur et surprenant ; les
/// ignorer rendrait le champ décoratif.
///
/// Les pièces sont déjà rattachées au dossier par `POST /kyb/documents` — c'est
/// l'agrégat qui les possède. Soumettre, c'est déclarer complet ce qui est là. La
/// liste serait au mieux une redite, au pire une seconde source de vérité.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record SubmitKybCommand(Guid SellerId) : ICommand;

internal sealed class SubmitKybCommandHandler : ICommandHandler<SubmitKybCommand>
{
    private readonly ISellerRepository _sellerRepository;
    private readonly ISellerUnitOfWork _unitOfWork;

    public SubmitKybCommandHandler(ISellerRepository sellerRepository, ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SubmitKybCommand command, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (seller is null)
        {
            return Result.Failure(
                Error.NotFound("sellers.seller.not_found", $"Vendeur {command.SellerId} introuvable."));
        }

        var result = seller.SubmitKyb();
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
