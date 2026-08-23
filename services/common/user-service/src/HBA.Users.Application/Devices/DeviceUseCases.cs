using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Shared.IntegrationEvents;
using HBA.Users.Application.Abstractions;
using HBA.Users.Contracts.IntegrationEvents;
using HBA.Users.Domain.Devices;

namespace HBA.Users.Application.Devices;

/// <summary>
/// Appareil tel que rendu par l'API.
///
/// LE JETON PUSH N'EST PAS DANS LE DTO — voir l'encadré de <see cref="UserDevice"/>.
/// Le rendre permettrait à quiconque lit la liste des appareils d'envoyer des
/// notifications au nom de la plateforme.
/// </summary>
public sealed record DeviceDto(Guid Id, string Platform, DateTime LastSeenAtUtc);

/// <summary>Enregistre ou rafraîchit un appareil pour les notifications push.</summary>
public sealed record RegisterDeviceCommand(Guid UserId, string? Platform, string? PushToken)
    : ICommand<DeviceDto>;

internal sealed class RegisterDeviceCommandHandler : ICommandHandler<RegisterDeviceCommand, DeviceDto>
{
    private readonly IUserDeviceRepository _repository;
    private readonly IUsersUnitOfWork _unitOfWork;
    private readonly IIntegrationEventPublisher _publisher;

    public RegisterDeviceCommandHandler(
        IUserDeviceRepository repository,
        IUsersUnitOfWork unitOfWork,
        IIntegrationEventPublisher publisher)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<Result<DeviceDto>> Handle(
        RegisterDeviceCommand command, CancellationToken cancellationToken)
    {
        var created = UserDevice.Register(command.UserId, command.Platform, command.PushToken);

        if (created.IsFailure)
        {
            return Result.Failure<DeviceDto>(created.Error);
        }

        // Réenregistrement du même appareil : on rafraîchit la ligne existante. Sans
        // cela, chaque ouverture de l'application ajouterait un destinataire et
        // l'utilisateur recevrait la même notification autant de fois qu'il a
        // réinstallé.
        var existing = await _repository.FindAsync(
            command.UserId, created.Value.PushToken, cancellationToken);

        if (existing is not null)
        {
            existing.Touch(created.Value.Platform);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(existing));
        }

        await _repository.AddAsync(created.Value, cancellationToken);

        // PUBLICATION AVANT SaveChanges, PAS APRÈS.
        //
        // `IIntegrationEventPublisher` écrit dans l'outbox du même DbContext : la
        // ligne d'événement et la ligne d'appareil partent donc dans LA MÊME
        // transaction. Publier après le SaveChanges les séparerait en deux, et un
        // arrêt entre les deux perdrait l'événement sans laisser de trace — le
        // problème exact que l'outbox du §19.6 existe pour supprimer.
        await _publisher.PublishAsync(
            new UserDeviceRegisteredIntegrationEvent
            {
                UserId = command.UserId,
                DeviceId = created.Value.Id,
                Platform = created.Value.Platform
            },
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(Map(created.Value));
    }

    private static DeviceDto Map(UserDevice d) => new(d.Id, d.Platform, d.LastSeenAtUtc);
}

/// <summary>Liste les appareils enregistrés d'un utilisateur.</summary>
public sealed record ListDevicesQuery(Guid UserId) : IQuery<IReadOnlyList<DeviceDto>>;

internal sealed class ListDevicesQueryHandler : IQueryHandler<ListDevicesQuery, IReadOnlyList<DeviceDto>>
{
    private readonly IUserDeviceRepository _repository;

    public ListDevicesQueryHandler(IUserDeviceRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<DeviceDto>>> Handle(
        ListDevicesQuery query, CancellationToken cancellationToken)
    {
        var devices = await _repository.ListByUserAsync(query.UserId, cancellationToken);

        IReadOnlyList<DeviceDto> result = devices
            .Select(d => new DeviceDto(d.Id, d.Platform, d.LastSeenAtUtc))
            .ToList();

        return Result.Success(result);
    }
}
