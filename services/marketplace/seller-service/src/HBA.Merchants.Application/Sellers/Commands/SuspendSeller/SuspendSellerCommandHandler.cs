using HBA.Shared.Application.Abstractions;
using HBA.Merchants.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Domain.Sellers;

namespace HBA.Merchants.Application.Sellers.Commands.SuspendSeller;

internal sealed class SuspendSellerCommandHandler : ICommandHandler<SuspendSellerCommand>
{
    private readonly ISellerRepository _sellerRepository;
    private readonly ISellerUnitOfWork _unitOfWork;

    public SuspendSellerCommandHandler(ISellerRepository sellerRepository, ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SuspendSellerCommand command, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (seller is null)
        {
            return Result.Failure(Error.NotFound("sellers.seller.not_found", $"Vendeur {command.SellerId} introuvable."));
        }

        // Le résultat est désormais examiné : Suspend() peut refuser (compte
        // fermé). L'ignorer aurait rendu la garde de statut décorative — elle
        // aurait protégé l'agrégat, et l'appelant aurait reçu un succès.
        var result = seller.Suspend(command.Reason);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>
/// Lève une suspension. Le catalogue retiré POUR CE MOTIF revient en vente —
/// voir SellerCatalogSuspension côté Products : ce qu'un modérateur avait
/// suspendu pour une autre raison reste retiré.
/// </summary>
internal sealed class LiftSellerSuspensionCommandHandler : ICommandHandler<LiftSellerSuspensionCommand>
{
    private readonly ISellerRepository _sellerRepository;
    private readonly ISellerUnitOfWork _unitOfWork;

    public LiftSellerSuspensionCommandHandler(ISellerRepository sellerRepository, ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(LiftSellerSuspensionCommand command, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (seller is null)
        {
            return Result.Failure(Error.NotFound("sellers.seller.not_found", $"Vendeur {command.SellerId} introuvable."));
        }

        var result = seller.LiftSuspension();
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
