using HBA.Users.Application.Abstractions;
using HBA.Users.Domain.Addresses;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Users.Application.Addresses;

/// <summary>Définit l'adresse par défaut (et retire le défaut des autres).</summary>
public sealed record SetDefaultAddressCommand(Guid UserId, Guid AddressId) : ICommand;

internal sealed class SetDefaultAddressCommandHandler : ICommandHandler<SetDefaultAddressCommand>
{
    private readonly IAddressRepository _repository;
    private readonly IUsersUnitOfWork _unitOfWork;

    public SetDefaultAddressCommandHandler(IAddressRepository repository, IUsersUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetDefaultAddressCommand command, CancellationToken cancellationToken)
    {
        var addresses = await _repository.ListByUserAsync(command.UserId, cancellationToken);
        var target = addresses.FirstOrDefault(a => a.Id.Value == command.AddressId);
        if (target is null)
        {
            return Result.Failure(Error.NotFound("users.address.not_found", "Adresse introuvable."));
        }

        foreach (var address in addresses)
        {
            if (address.IsDefault)
            {
                address.ClearDefault();
            }
        }

        target.MarkDefault();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
