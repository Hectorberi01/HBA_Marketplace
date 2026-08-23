using HBA.Shared.Application.Abstractions;
using HBA.Merchants.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Domain.Sellers;

namespace HBA.Merchants.Application.Sellers.Commands.UpdateSellerProfile;

internal sealed class UpdateSellerProfileCommandHandler : ICommandHandler<UpdateSellerProfileCommand>
{
    private readonly ISellerRepository _sellerRepository;
    private readonly ISellerUnitOfWork _unitOfWork;

    public UpdateSellerProfileCommandHandler(ISellerRepository sellerRepository, ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateSellerProfileCommand command, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (seller is null)
        {
            return Result.Failure(Error.NotFound("sellers.seller.not_found", $"Vendeur {command.SellerId} introuvable."));
        }

        var result = seller.UpdateProfile(command.ShopName, command.LogoUrl, command.Description);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
