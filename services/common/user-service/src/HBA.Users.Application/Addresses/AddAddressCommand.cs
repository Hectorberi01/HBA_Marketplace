using HBA.Users.Application.Abstractions;
using HBA.Users.Domain.Addresses;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Shared.IntegrationEvents;
using HBA.Users.Contracts.IntegrationEvents;

namespace HBA.Users.Application.Addresses;

/// <summary>Ajoute une adresse au carnet de l'utilisateur.</summary>
public sealed record AddAddressCommand(
    Guid UserId,
    string? Label,
    string? Recipient,
    string? Phone,
    string? CommuneCode,
    string? Quartier,
    string? Landmark,
    string? Line1,
    double? Latitude,
    double? Longitude,
    bool MakeDefault) : ICommand<Guid>;

internal sealed class AddAddressCommandHandler : ICommandHandler<AddAddressCommand, Guid>
{
    private readonly IAddressRepository _repository;
    private readonly IUsersUnitOfWork _unitOfWork;
    private readonly IIntegrationEventPublisher _publisher;

    public AddAddressCommandHandler(
        IAddressRepository repository,
        IUsersUnitOfWork unitOfWork,
        IIntegrationEventPublisher publisher)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<Result<Guid>> Handle(AddAddressCommand command, CancellationToken cancellationToken)
    {
        var existing = await _repository.ListByUserAsync(command.UserId, cancellationToken);
        // La première adresse est forcément le défaut.
        var makeDefault = command.MakeDefault || existing.Count == 0;

        var created = Address.Create(
            command.UserId, command.Label, command.Recipient, command.Phone,
            command.CommuneCode, command.Quartier, command.Landmark, command.Line1,
            command.Latitude, command.Longitude, makeDefault);
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        if (makeDefault)
        {
            foreach (var other in existing.Where(a => a.IsDefault))
            {
                other.ClearDefault();
            }
        }

        await _repository.AddAsync(created.Value, cancellationToken);

        // §10.2 : `user.address.created`. Ni rue ni point GPS dans la charge utile —
        // voir l'encadré de l'événement : un topic conservé plusieurs jours n'est pas
        // un endroit où poser une adresse postale.
        await _publisher.PublishAsync(
            new UserAddressCreatedIntegrationEvent
            {
                UserId = command.UserId,
                AddressId = created.Value.Id.Value,
                CommuneCode = command.CommuneCode,
                IsDefault = makeDefault
            },
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(created.Value.Id.Value);
    }
}
