using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Shared.Application.Context;
using HBA.Shared.IntegrationEvents;
using HBA.Users.Application.Abstractions;
using HBA.Users.Application.Profiles;
using MediatR;
using Microsoft.Extensions.Logging;

using HBA.Users.Infrastructure.Persistence.Outbox;
using HBA.Users.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Users.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>UN COMPTE EST CRÉÉ → UN PROFIL EST CRÉÉ.</summary>
public sealed class CreateUserProfileOnUserRegisteredHandler: IIntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    /// <summary>Nom de ce consumer dans `consumer_inbox` (§19.5).</summary>
    private const string ConsumerName = "user-service.identity-user-registered";

    private readonly ISender _sender;
    private readonly IConsumerInbox _inbox;
    private readonly IUsersUnitOfWork _unitOfWork;
    private readonly ILogger<CreateUserProfileOnUserRegisteredHandler> _logger;

    public CreateUserProfileOnUserRegisteredHandler(
        ISender sender,
        IConsumerInbox inbox,
        IUsersUnitOfWork unitOfWork,
        ILogger<CreateUserProfileOnUserRegisteredHandler> logger)
    {
        _sender = sender;
        _inbox = inbox;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(UserRegisteredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // GARDE D'IDEMPOTENCE DU §19.5 — ET CE QU'ELLE APPORTE VRAIMENT ICI.
        if (await _inbox.HasProcessedAsync(integrationEvent.Id, ConsumerName, cancellationToken))
        {
            _logger.LogDebug("Événement {EventId} déjà traité par {Consumer} : ignoré.",integrationEvent.Id, ConsumerName);
            return;
        }

        // La commande est IDEMPOTENTE : Kafka livre au moins une fois, et un rejeu
        // après redémarrage rappellerait ce gestionnaire.
        var result = await _sender.Send(
            new CreateUserProfileCommand(
                integrationEvent.UserId,
                integrationEvent.FirstName,
                integrationEvent.LastName),
            cancellationToken);

        if (result.IsFailure)
        {
            // ON LÈVE, CONTRAIREMENT AUX GESTIONNAIRES DE RÔLES.
            _logger.LogError(
                "Profil NON créé pour le compte {UserId} — {Code} : {Message}",
                integrationEvent.UserId, result.Error.Code, result.Error.Message);

            throw new InvalidOperationException(
                $"Création du profil impossible pour le compte {integrationEvent.UserId} : "
                + $"{result.Error.Code} — {result.Error.Message}");
        }

        // Trace de consommation. Écrite APRÈS le succès seulement : la marquer
        // avant ferait considérer comme traité un événement dont l'effet métier a
        // échoué, et le rejeu — la seule chance de réparation — n'aurait jamais
        // lieu.
        await _inbox.MarkProcessedAsync(
            integrationEvent.Id,
            ConsumerName,
            "identity.user.registered",
            HbaRequestContext.Current.CorrelationId,
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
