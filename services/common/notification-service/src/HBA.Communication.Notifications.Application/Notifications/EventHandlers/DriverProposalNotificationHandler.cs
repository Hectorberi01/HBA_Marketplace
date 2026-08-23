using Microsoft.Extensions.Logging;
using HBA.Deliveries.Contracts;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.Communication.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// Une course est proposée à un livreur → il en est averti.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE LIVREUR AVAIT QUARANTE-CINQ SECONDES POUR ACCEPTER UNE COURSE DONT RIEN
///    NE L'AVERTISSAIT.
///
/// `DeliveryAssignedIntegrationEvent` n'avait aucun consommateur. Le dispatch
/// choisissait un livreur, démarrait son chronomètre, et l'expiration tombait
/// sans que l'intéressé ait jamais su qu'on lui proposait quelque chose. La
/// course repartait au suivant, puis finissait en « aucun livreur disponible ».
///
/// Le contrat de l'événement documente pourtant cette notification comme sa
/// seule raison d'être. Le consommateur vivait dans la composition root du
/// monolithe et n'a pas suivi l'extraction.
///
/// L'ÉVÉNEMENT NE PORTE PAS LE COMPTE UTILISATEUR, ET C'EST VOULU.
///
/// Il ne transporte que le `DriverId`. Son contrat l'explique : il part AUSSI
/// vers l'API partenaires, à qui le compte HBA d'un livreur ne regarde pas.
/// On relit donc le livreur par gRPC — une lecture de plus sur un événement
/// rare, contre un champ exposé à des tiers pour toujours.
///
/// ON NE LÈVE PAS, MAIS ON LE DIT FORT.
///
/// Rejouer trois fois une proposition dont la fenêtre de quarante-cinq secondes
/// est déjà close n'a aucun sens : la course est repartie ailleurs. En revanche
/// un livreur introuvable au moment de le prévenir signale une incohérence
/// entre le dispatch et le référentiel — ça se journalise en avertissement.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class NotifyDriverOnDeliveryAssignedHandler
    : IIntegrationEventHandler<DeliveryAssignedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IDeliveryModuleApi _deliveries;
    private readonly ILogger<NotifyDriverOnDeliveryAssignedHandler> _logger;

    public NotifyDriverOnDeliveryAssignedHandler(
        NotificationDispatcher dispatcher,
        IDeliveryModuleApi deliveries,
        ILogger<NotifyDriverOnDeliveryAssignedHandler> logger)
    {
        _dispatcher = dispatcher;
        _deliveries = deliveries;
        _logger = logger;
    }

    public async Task HandleAsync(
        DeliveryAssignedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var livreur = await _deliveries.GetDriverAccountAsync(e.DriverId, cancellationToken);

        if (livreur is null)
        {
            _logger.LogWarning(
                "Proposition non notifiée : livreur {DriverId} introuvable pour la course {DeliveryId}.",
                e.DriverId, e.DeliveryId);

            return;
        }

        // PAS D'E-MAIL. Une proposition dure quarante-cinq secondes ; un
        // courriel arrive après la bataille et encombre une boîte pour rien.
        // C'est le push qui compte, et lui seul.
        await _dispatcher.NotifyAsync(
            livreur.UserId,
            "Nouvelle course",
            "Une course vous est proposée. Vous avez 45 secondes pour l'accepter.",
            "Delivery",
            e.DeliveryId,
            cancellationToken,
            alsoEmail: false);
    }
}
