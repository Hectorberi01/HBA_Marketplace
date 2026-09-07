using Microsoft.Extensions.Logging;
using HBA.Shared.IntegrationEvents;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Communication.Notifications.Application.Abstractions;
using HBA.Shared.Application.Abstractions;
using HBA.Communication.Notifications.Application.Emails;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Communication.Notifications.Application.Notifications;
using HBA.Communication.Notifications.Application.Notifications.EventHandlers;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>L'E-MAIL D'INVITATION — LE SEUL CHEMIN PAR LEQUEL UN EMPLOYÉ PEUT ENTRER.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.SendSellerInvitationEmailHandler")]
public sealed class SendSellerInvitationEmailHandler
    : IIntegrationEventHandler<SellerMemberInvitedIntegrationEvent>
{
    private readonly IEmailSender _email;
    private readonly IAccountLinkBuilder _liens;
    private readonly ISecretProtector _protecteur;
    private readonly INotificationsUnitOfWork _unitOfWork;
    private readonly ILogger<SendSellerInvitationEmailHandler> _logger;

    public SendSellerInvitationEmailHandler(
        IEmailSender email,
        IAccountLinkBuilder liens,
        ISecretProtector protecteur,
        INotificationsUnitOfWork unitOfWork,
        ILogger<SendSellerInvitationEmailHandler> logger)
    {
        _email = email;
        _liens = liens;
        _protecteur = protecteur;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        SellerMemberInvitedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // LE JETON ARRIVE CHIFFRÉ et n'est rendu lisible qu'ici, au dernier moment.
        var jeton = _protecteur.Unprotect(integrationEvent.ProtectedInvitationToken);

        var message = MemberEmailTemplates.SellerInvitation(
            integrationEvent.Email,
            integrationEvent.DisplayName,
            integrationEvent.ShopName,
            _liens.SellerInvitation(jeton),
            integrationEvent.ExpiresOnUtc);

        // On NE capture PAS l'exception.
        await _email.SendAsync(message, cancellationToken);

        // NI LE JETON, NI L'URL (qui le contient), NI L'ADRESSE.
        _logger.LogInformation(
            "E-mail d'invitation envoyé pour l'invitation {InvitationId} du vendeur {SellerId}.",
            integrationEvent.InvitationId, integrationEvent.SellerId);

        // CE `SaveChanges` NE SAUVEGARDE RIEN A NOUS — IL COMMITTE LA TRACE.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

    }
}
