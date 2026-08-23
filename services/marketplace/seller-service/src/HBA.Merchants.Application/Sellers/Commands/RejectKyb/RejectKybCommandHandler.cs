using HBA.Shared.Application.Abstractions;
using HBA.Merchants.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Domain.Sellers;

namespace HBA.Merchants.Application.Sellers.Commands.RejectKyb;

internal sealed class RejectKybCommandHandler : ICommandHandler<RejectKybCommand>
{
    private readonly ISellerRepository _sellerRepository;
    private readonly ISellerUnitOfWork _unitOfWork;

    public RejectKybCommandHandler(ISellerRepository sellerRepository, ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RejectKybCommand command, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (seller is null)
        {
            return Result.Failure(Error.NotFound("sellers.seller.not_found", $"Vendeur {command.SellerId} introuvable."));
        }

        // Le résultat est examiné : RejectKyb peut refuser (dossier jamais
        // déposé). L'ignorer aurait rendu la garde décorative — l'appelant aurait
        // reçu un succès sur une décision non appliquée.
        var result = seller.RejectKyb(command.Reason);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
