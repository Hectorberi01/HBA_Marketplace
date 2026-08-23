using Microsoft.Extensions.Logging;
using HBA.Shared.IntegrationEvents;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Communication.Notifications.Application.Abstractions;
using HBA.Shared.Application.Abstractions;
using HBA.Communication.Notifications.Application.Emails;

namespace HBA.Communication.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'E-MAIL D'INVITATION — LE SEUL CHEMIN PAR LEQUEL UN EMPLOYÉ PEUT ENTRER.
///
/// SANS CE HANDLER, L'INVITATION EXISTE ET N'ATTEINT PERSONNE.
///
/// MediatR et le répartiteur d'événements d'intégration résolvent paresseusement :
/// un événement sans consommateur ne provoque aucune erreur, aucun avertissement.
/// Il part dans l'outbox, est marqué traité, et disparaît. C'est exactement ce qui
/// est arrivé pendant des mois à `EmailVerificationRequestedIntegrationEvent` —
/// publié consciencieusement depuis le premier jour, consommé par personne, et
/// aucun compte n'a jamais reçu son lien.
///
/// LE DESTINATAIRE N'A PEUT-ÊTRE PAS ENCORE DE COMPTE, ET C'EST LE CAS NORMAL.
///
/// On invite une ADRESSE, pas un utilisateur : c'est tout l'objet du §9, où l'invité
/// « ouvre le lien, se connecte si le compte existe, le crée sinon ». Ce handler
/// écrit donc directement à l'adresse portée par l'événement, sans chercher de
/// `UserId` — contrairement aux notifications de commande, qui poussent vers un
/// compte connu.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SendSellerInvitationEmailHandler
    : IIntegrationEventHandler<SellerMemberInvitedIntegrationEvent>
{
    private readonly IEmailSender _email;
    private readonly IAccountLinkBuilder _liens;
    private readonly ISecretProtector _protecteur;
    private readonly ILogger<SendSellerInvitationEmailHandler> _logger;

    public SendSellerInvitationEmailHandler(
        IEmailSender email,
        IAccountLinkBuilder liens,
        ISecretProtector protecteur,
        ILogger<SendSellerInvitationEmailHandler> logger)
    {
        _email = email;
        _liens = liens;
        _protecteur = protecteur;
        _logger = logger;
    }

    public async Task HandleAsync(
        SellerMemberInvitedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // LE JETON ARRIVE CHIFFRÉ et n'est rendu lisible qu'ici, au dernier
        // moment. On NE capture PAS l'échec : une charge illisible signifie que
        // seller-service et ce service n'ont pas la même
        // `Security:SecretProtection:Key`. Composer un lien avec une valeur de
        // remplacement enverrait à l'invité une porte qui ne s'ouvre pas, sans que
        // rien ne le signale ; l'exception, elle, se voit.
        var jeton = _protecteur.Unprotect(integrationEvent.ProtectedInvitationToken);

        var message = MemberEmailTemplates.SellerInvitation(
            integrationEvent.Email,
            integrationEvent.DisplayName,
            integrationEvent.ShopName,
            _liens.SellerInvitation(jeton),
            integrationEvent.ExpiresOnUtc);

        // On NE capture PAS l'exception. Un échec laisse le message d'outbox non
        // traité, donc rejoué au tour suivant. L'avaler perdrait l'invitation
        // définitivement et en silence — et le commerçant attendrait un employé
        // qui n'a jamais rien reçu, sans que rien ne l'explique.
        await _email.SendAsync(message, cancellationToken);

        // NI LE JETON, NI L'URL (qui le contient), NI L'ADRESSE.
        //
        // Un jeton dans les journaux est un jeton lisible par quiconque y a accès :
        // ce serait recréer, en plus discret, la fuite que le hachage en base
        // vient de fermer. L'identifiant de l'invitation suffit à retrouver la
        // ligne correspondante.
        _logger.LogInformation(
            "E-mail d'invitation envoyé pour l'invitation {InvitationId} du vendeur {SellerId}.",
            integrationEvent.InvitationId, integrationEvent.SellerId);
    }
}
