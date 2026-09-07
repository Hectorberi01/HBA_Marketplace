using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Application.Abstractions;

namespace HBA.Merchants.Application.Sellers.Commands.DeleteSeller;

internal sealed class DeleteSellerCommandHandler : ICommandHandler<DeleteSellerCommand>
{
    private readonly ISellerRepository _sellerRepository;
    private readonly ISellerUnitOfWork _unitOfWork;

    public DeleteSellerCommandHandler(ISellerRepository sellerRepository, ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteSellerCommand command, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (seller is null)
        {
            return Result.Failure(Error.NotFound("sellers.seller.not_found", $"Vendeur {command.SellerId} introuvable."));
        }

        // LES PIÈCES D'IDENTITÉ SONT TOUJOURS EFFACÉES — PAR ÉVÉNEMENT DÉSORMAIS.

        // Émet l'événement de purge AVANT le retrait : le dispatch des domain
        // events lit le ChangeTracker avant le SaveChanges, l'agrégat (encore
        // tracké, à l'état Deleted) porte donc bien son événement.
        seller.MarkForDeletion();
        _sellerRepository.Remove(seller);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
