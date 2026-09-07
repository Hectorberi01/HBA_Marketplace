using HBA.Users.Application.Abstractions;
using HBA.Users.Domain.Addresses;
using HBA.Users.Domain.Profiles;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Users.Application.Profiles;

/// <summary>EFFACE TOUT CE QUE CE MODULE SAIT D'UNE PERSONNE.</summary>
public sealed record PurgeUserDataCommand(Guid UserId) : ICommand;

internal sealed class PurgeUserDataCommandHandler : ICommandHandler<PurgeUserDataCommand>
{
    private readonly IUserProfileRepository _profiles;
    private readonly IAddressRepository _addresses;
    private readonly IUsersUnitOfWork _unitOfWork;

    public PurgeUserDataCommandHandler(
        IUserProfileRepository profiles,
        IAddressRepository addresses,
        IUsersUnitOfWork unitOfWork)
    {
        _profiles = profiles;
        _addresses = addresses;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(PurgeUserDataCommand command, CancellationToken ct)
    {
        if (command.UserId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "users.purge.user_required", "L'identifiant du compte est obligatoire."));
        }

        var carnet = await _addresses.ListByUserAsync(command.UserId, ct);
        foreach (var adresse in carnet)
        {
            _addresses.Remove(adresse);
        }

        var profile = await _profiles.GetByUserIdAsync(command.UserId, ct);
        if (profile is not null)
        {
            _profiles.Remove(profile);
        }

        // UN SEUL SaveChanges pour les deux.
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
