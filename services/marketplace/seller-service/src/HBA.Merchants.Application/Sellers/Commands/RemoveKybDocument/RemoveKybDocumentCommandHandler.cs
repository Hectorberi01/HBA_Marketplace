using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Application.Abstractions;

namespace HBA.Merchants.Application.Sellers.Commands.RemoveKybDocument;

internal sealed class RemoveKybDocumentCommandHandler : ICommandHandler<RemoveKybDocumentCommand>
{
    private readonly ISellerRepository _sellerRepository;
    private readonly ISellerUnitOfWork _unitOfWork;

    public RemoveKybDocumentCommandHandler(
        ISellerRepository sellerRepository, ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemoveKybDocumentCommand command, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (seller is null)
        {
            return Result.Failure(Error.NotFound("sellers.seller.not_found", $"Vendeur {command.SellerId} introuvable."));
        }

        var result = seller.RemoveKybDocument(command.DocumentId);
        if (result.IsFailure)
        {
            return Result.Failure(result.Error);
        }

        // LE FICHIER EST TOUJOURS EFFACÉ — MAIS PLUS ICI, ET LA GARANTIE A CHANGÉ.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
