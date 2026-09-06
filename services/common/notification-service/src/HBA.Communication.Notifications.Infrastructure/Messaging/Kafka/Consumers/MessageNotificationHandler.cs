using Microsoft.Extensions.Logging;
using HBA.Shared.IntegrationEvents;
using HBA.Communication.Contracts;
using HBA.Communication.Contracts.IntegrationEvents;
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
/// Prévient le destinataire d'un message de conversation (acheteur ↔ vendeur).
///
/// ─────────────────────────────────────────────────────────────────────────────
/// SANS CE HANDLER, LA MESSAGERIE ÉTAIT MUETTE.
///
/// Un acheteur posait une question ; le vendeur ne l'apprenait qu'en rouvrant son
/// application, par hasard. Et réciproquement. Une messagerie dont personne n'est
/// averti n'est pas une messagerie — c'est une boîte aux lettres qu'il faut penser
/// à aller relever.
/// ─────────────────────────────────────────────────────────────────────────────
///
/// L'événement ne porte que l'EXPÉDITEUR : on remonte donc les participants de la
/// conversation pour notifier tous les autres (le fil est à deux aujourd'hui, mais
/// rien n'oblige à le rester).
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
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.MessageSentNotificationHandler")]
public sealed class MessageSentNotificationHandler : IIntegrationEventHandler<MessageSentIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IMessagingModuleApi _messaging;
    private readonly ILogger<MessageSentNotificationHandler> _logger;

    public MessageSentNotificationHandler(
        NotificationDispatcher dispatcher,
        IMessagingModuleApi messaging,
        ILogger<MessageSentNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _messaging = messaging;
        _logger = logger;
    }

    public async Task HandleAsync(MessageSentIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var participants = await _messaging.ListParticipantsAsync(e.ConversationId, cancellationToken);
        if (participants.Count == 0)
        {
            _logger.LogWarning(
                "Message {MessageId} : conversation {ConversationId} introuvable — personne n'est prévenu.",
                e.MessageId, e.ConversationId);
            return;
        }

        // Tout le monde SAUF l'expéditeur : se notifier soi-même de son propre message
        // est le grand classique des messageries mal branchées.
        foreach (var userId in participants.Where(p => p != e.SenderId))
        {
            try
            {
                // Le CONTENU n'est pas repris dans la notification : l'événement ne le
                // porte pas, et un aperçu de message privé sur un écran verrouillé n'est
                // pas anodin. On invite à ouvrir le fil.
                await _dispatcher.NotifyAsync(
                    userId,
                    "Nouveau message",
                    "Vous avez reçu un message. Ouvrez la conversation pour le lire.",
                    "Message",
                    e.ConversationId,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Un destinataire injoignable ne doit pas priver les autres.
                _logger.LogError(
                    ex, "Message {MessageId} : échec de la notification de {UserId}.", e.MessageId, userId);
            }
        }
    }
}
