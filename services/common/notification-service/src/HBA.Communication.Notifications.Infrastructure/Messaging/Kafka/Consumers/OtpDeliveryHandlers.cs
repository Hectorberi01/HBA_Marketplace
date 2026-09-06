using Microsoft.Extensions.Logging;
using HBA.Shared.IntegrationEvents;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Communication.Notifications.Application.Abstractions;
using HBA.Shared.Application.Abstractions;
using HBA.Communication.Notifications.Application.Emails;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
//
// Il vivait dans `HBA.Communication.Notifications.Application.Notifications.EventHandlers` et y resolvait ses voisins SANS `using` : le
// compilateur cherche d'abord dans les espaces de noms englobants. Descendu
// dans `Messaging/Kafka/Consumers`, il a perdu ce voisinage — d'ou les lignes
// ci-dessous, qui rendent explicite ce qui etait implicite.
using HBA.Communication.Notifications.Application.Notifications;
using HBA.Communication.Notifications.Application.Notifications.EventHandlers;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// Remet le code à usage unique à son destinataire, par SMS ou par e-mail.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE HANDLER N'EXISTAIT PAS, ET IL N'Y AVAIT MÊME PAS D'ÉVÉNEMENT À CONSOMMER.
///
/// `IssueOtpChallengeCommandHandler` générait le code, le hachait, le stockait,
/// appliquait le plafond de tentatives — puis écrivait `_ = code;`. Le clair
/// partait avec la pile. Aucun envoi, aucun événement, aucune erreur : la route
/// rendait un `challengeId` parfaitement valide, et l'utilisateur attendait un
/// message qui ne pouvait pas venir.
///
/// C'est le troisième cas du même motif dans ce service : l'e-mail de
/// vérification et le jeton de réinitialisation étaient déjà partis dans le vide,
/// faute de consommateur. Voir `AccountEmailHandlers`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
//
// `IntegrationEventDispatcher` la derivait du nom complet du type. Descendre ce
// fichier dans `Messaging/Kafka/Consumers` a change son espace de noms, donc sa
// cle, donc a orpheline ses traces dans `consumer_inbox` : au premier rejeu,
// chaque evenement deja traite serait repasse pour neuf.
//
// Les valeurs ci-dessous reproduisent le nom complet d'AVANT le deplacement.
// Ce sont des cles de base de donnees : elles ne se refactorisent pas.
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.SendOtpCodeHandler")]
public sealed class SendOtpCodeHandler : IIntegrationEventHandler<OtpChallengeIssuedIntegrationEvent>
{
    /// <summary>Les valeurs de `MfaChannels`, recopiées côté consommateur.</summary>
    /// <remarks>
    /// RECOPIÉES DÉLIBÉRÉMENT, contrairement à la règle habituelle du dépôt.
    /// `MfaChannels` vit dans `HBA.Identity.Domain` : le référencer d'ici ferait
    /// dépendre notification-service du DOMAINE d'identity, pas de ses contrats.
    /// C'est exactement le couplage que la séparation en `*.Contracts` existe pour
    /// empêcher. Le prix est ces deux constantes ; le contrôle qui l'empêche de
    /// diverger est `check-event-contracts.py`, qui refuse une rupture du champ
    /// `Channel`.
    /// </remarks>
    private const string CanalSms = "SMS";
    private const string CanalEmail = "EMAIL";

    private readonly IEmailSender _email;
    private readonly ISmsSender _sms;
    private readonly ISecretProtector _protecteur;
    private readonly INotificationsUnitOfWork _unitOfWork;
    private readonly ILogger<SendOtpCodeHandler> _logger;

    public SendOtpCodeHandler(
        IEmailSender email,
        ISmsSender sms,
        ISecretProtector protecteur,
        INotificationsUnitOfWork unitOfWork,
        ILogger<SendOtpCodeHandler> logger)
    {
        _email = email;
        _sms = sms;
        _protecteur = protecteur;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        OtpChallengeIssuedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // LE CODE ARRIVE CHIFFRÉ (ISSUE-071). On le déchiffre ici, au dernier moment.
        // On NE capture PAS l'échec : une charge illisible signifie que les deux services
        // n'ont pas la même `Security:SecretProtection:Key`. Envoyer un code de
        // remplacement serait pire que ne rien envoyer — l'utilisateur saisirait un code
        // que la base ne reconnaîtra jamais, et chaque essai consommerait une de ses cinq
        // tentatives.
        var code = _protecteur.Unprotect(integrationEvent.ProtectedCode);

        // MINUTES CALCULÉES, PAS RECOPIÉES DE `MfaChallenge.Lifetime`.
        //
        // Ce qui compte pour l'utilisateur, ce n'est pas la durée de vie configurée mais
        // le temps qu'il lui RESTE — l'événement a pu attendre dans l'outbox, et un rejeu
        // après temporisation peut arriver plusieurs minutes après l'émission. Annoncer
        // « dix minutes » sur un code qui en a trois est un mensonge que l'utilisateur
        // découvre au pire moment.
        //
        // Plancher à 1 : annoncer « 0 minute » ou un négatif sur un code déjà expiré
        // serait absurde. Le code sera de toute façon refusé à la vérification — c'est
        // là que l'expiration se tranche, pas dans un gabarit de message.
        var restantes = Math.Max(1, (int)Math.Ceiling((integrationEvent.ExpiresAtUtc - DateTime.UtcNow).TotalMinutes));

        // On NE capture PAS l'exception d'envoi. Un échec laisse le message d'outbox non
        // traité, donc rejoué, puis mis en lettre morte — visible et rejouable à la main.
        // L'avaler perdrait le code DÉFINITIVEMENT et EN SILENCE.
        switch (integrationEvent.Channel)
        {
            case CanalSms:
                await _sms.SendAsync(
                    new SmsMessage(
                        integrationEvent.PhoneNumber,
                        AccountEmailTemplates.OneTimeCodeSms(code, restantes)),
                    cancellationToken);
                break;

            case CanalEmail:
                await _email.SendAsync(
                    AccountEmailTemplates.OneTimeCode(
                        integrationEvent.Email, integrationEvent.FirstName, code, restantes),
                    cancellationToken);
                break;

            default:
                // ON LÈVE, ON NE RETOMBE PAS SUR L'E-MAIL.
                //
                // Un canal inconnu veut dire que le producteur en a ajouté un que ce
                // consommateur ne sait pas servir. Un repli silencieux enverrait le code
                // par un canal que l'utilisateur n'a pas choisi — et masquerait un
                // déploiement incomplet derrière un comportement « qui marche ». La
                // lettre morte, elle, se voit.
                throw new InvalidOperationException(
                    $"Canal OTP inconnu « {integrationEvent.Channel} » pour l'utilisateur "
                    + $"{integrationEvent.UserId}. Aucun envoi n'a été tenté : un repli sur un "
                    + "autre canal remettrait le code ailleurs que là où il a été demandé.");
        }

        // On journalise l'utilisateur et le canal, JAMAIS le code ni les coordonnées.
        _logger.LogInformation(
            "Code à usage unique remis à l'utilisateur {UserId} par {Channel}.",
            integrationEvent.UserId, integrationEvent.Channel);

        // ═════════════════════════════════════════════════════════════════════
        // CE `SaveChanges` NE SAUVEGARDE RIEN A NOUS — IL COMMITTE LA TRACE.
        //
        // `IntegrationEventDispatcher` ajoute l'entree d'inbox au contexte AVANT
        // d'appeler ce gestionnaire, et ne la sauvegarde pas : elle doit partir
        // avec la transaction de l'effet metier. Ce gestionnaire n'ecrivait rien
        // en base — il envoie, c'est tout — donc la trace restait en attente et
        // n'etait JAMAIS committee.
        //
        // Consequence : l'evenement restait « jamais traite » pour l'inbox. Au
        // premier rejeu — remise a zero d'offsets, rebalancement de partition —
        // le code a usage unique repartait a son destinataire, deja perime
        // ou deja utilise.
        //
        // L'APPEL EST APRES L'ENVOI, ET C'EST DELIBERE. Sauvegarder avant
        // marquerait l'evenement traite pour un envoi qui peut encore echouer :
        // le message ne partirait jamais et rien ne le rejouerait. Dans l'autre
        // sens, un echec de sauvegarde apres un envoi reussi fait un doublon —
        // Kafka livre au moins une fois, c'est le cote acceptable de l'arbitrage.
        // ═════════════════════════════════════════════════════════════════════
        await _unitOfWork.SaveChangesAsync(cancellationToken);

    }
}
