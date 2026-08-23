using HBA.Users.Application.Abstractions;
using HBA.Users.Domain.Addresses;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Users.Application.Addresses;

/// <summary>Supprime une adresse du carnet (vérifie la propriété).</summary>
public sealed record DeleteAddressCommand(Guid UserId, Guid AddressId) : ICommand;

internal sealed class DeleteAddressCommandHandler : ICommandHandler<DeleteAddressCommand>
{
    private readonly IAddressRepository _repository;
    private readonly IUsersUnitOfWork _unitOfWork;

    public DeleteAddressCommandHandler(IAddressRepository repository, IUsersUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteAddressCommand command, CancellationToken cancellationToken)
    {
        var address = await _repository.GetByIdAsync(new AddressId(command.AddressId), cancellationToken);
        if (address is null || address.UserId != command.UserId)
        {
            return Result.Failure(Error.NotFound("users.address.not_found", "Adresse introuvable."));
        }

        _repository.Remove(address);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
