using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using HBA.Users.Application.Profiles;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HBA.Users.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>LE COMPTE CHANGE → LE PROFIL SUIT.</summary>
public sealed class RenameUserProfileOnIdentityProfileUpdatedHandler
    : IIntegrationEventHandler<UserProfileUpdatedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<RenameUserProfileOnIdentityProfileUpdatedHandler> _logger;

    public RenameUserProfileOnIdentityProfileUpdatedHandler(
        ISender sender, ILogger<RenameUserProfileOnIdentityProfileUpdatedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        UserProfileUpdatedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new RenameUserProfileCommand(
                integrationEvent.UserId, integrationEvent.FirstName, integrationEvent.LastName),
            cancellationToken);

        if (result.IsFailure)
        {
            // DEUX TRAITEMENTS, ET LA DISTINCTION EST LE FOND DU SUJET.
            if (result.Error.Code == "users.profile.not_found")
            {
                _logger.LogWarning(
                    "Profil non renommé pour le compte {UserId} : aucun profil "
                    + "(compte antérieur au branchement du pont de création).",
                    integrationEvent.UserId);

                return;
            }

            _logger.LogError(
                "Profil NON renommé pour le compte {UserId} — {Code} : {Message}",
                integrationEvent.UserId, result.Error.Code, result.Error.Message);

            throw new InvalidOperationException(
                $"Renommage du profil impossible pour le compte {integrationEvent.UserId} : "
                + $"{result.Error.Code} — {result.Error.Message}");
        }
    }
}

/// <summary>LE COMPTE EST SUPPRIMÉ → LE PROFIL ET LES ADRESSES DISPARAISSENT.</summary>
public sealed class PurgeUserDataOnAccountAnonymizedHandler
    : IIntegrationEventHandler<UserAnonymizedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<PurgeUserDataOnAccountAnonymizedHandler> _logger;

    public PurgeUserDataOnAccountAnonymizedHandler(
        ISender sender, ILogger<PurgeUserDataOnAccountAnonymizedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        UserAnonymizedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new PurgeUserDataCommand(integrationEvent.UserId), cancellationToken);

        if (result.IsFailure)
        {
            // ON LÈVE, ET C'EST UNE CORRECTION D'UNE VERSION ANTÉRIEURE.
            _logger.LogError(
                "Données personnelles NON purgées pour le compte supprimé {UserId} — {Code} : {Message}",
                integrationEvent.UserId, result.Error.Code, result.Error.Message);

            throw new InvalidOperationException(
                $"Purge des données personnelles impossible pour le compte {integrationEvent.UserId} : "
                + $"{result.Error.Code} — {result.Error.Message}");
        }
    }
}
