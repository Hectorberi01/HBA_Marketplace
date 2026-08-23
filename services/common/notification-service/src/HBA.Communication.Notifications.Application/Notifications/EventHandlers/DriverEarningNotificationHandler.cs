using Microsoft.Extensions.Logging;
using HBA.Deliveries.Contracts;
using HBA.Financial.Wallet.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.Communication.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// Le gain d'une course vient d'être crédité → le livreur l'apprend.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// MÊME SILENCE QUE LES REVERSEMENTS VENDEUR, UN ÉTAGE PLUS BAS.
///
/// Le livreur n'était pas payé du tout — le fil entre la fin de course et le
/// portefeuille n'existait pas. Une fois ce fil posé, il l'aurait été SANS UN
/// MOT : rien dans sa boîte de réception, rien par courriel. Il aurait dû ouvrir
/// l'écran « Revenus » et comparer deux chiffres de mémoire pour deviner qu'une
/// course lui avait été réglée.
///
/// C'est exactement ce que `PayoutPaidNotificationHandler` a corrigé pour le
/// vendeur, et pour la même raison : le message qu'un travailleur attend le plus
/// est celui qui lui dit qu'il a été payé.
///
/// ON ÉCOUTE LE CRÉDIT, PAS LA FIN DE COURSE.
///
/// `DeliveryCompletedIntegrationEvent` porte déjà le montant, et il serait plus
/// court de partir de lui. Mais ce montant n'est qu'un calcul : une course sans
/// prix ou un portefeuille en devise incompatible n'aboutit à aucune écriture, et
/// le livreur aurait reçu l'annonce d'un gain absent de son solde. Un message qui
/// ment envoie l'intéressé au support avec raison.
///
/// PAS D'E-MAIL.
///
/// Le livreur enchaîne les courses ; une adresse encombrée d'un courriel par
/// remise deviendrait un dossier de spam, et le jour où un vrai message
/// arriverait, personne ne le lirait. Le récapitulatif de revenus est un autre
/// besoin, et il se traite par période, pas par course.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class DriverEarningCreditedNotificationHandler
    : IIntegrationEventHandler<DriverEarningCreditedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IDeliveryModuleApi _deliveries;
    private readonly ILogger<DriverEarningCreditedNotificationHandler> _logger;

    public DriverEarningCreditedNotificationHandler(
        NotificationDispatcher dispatcher,
        IDeliveryModuleApi deliveries,
        ILogger<DriverEarningCreditedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _deliveries = deliveries;
        _logger = logger;
    }

    public async Task HandleAsync(
        DriverEarningCreditedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // Traduction DriverId → UserId : le portefeuille est indexé sur le livreur,
        // le jeton d'appareil sur le COMPTE. L'événement ne porte pas le second,
        // comme les événements de course — voir `IDeliveryModuleApi`.
        var livreur = await _deliveries.GetDriverAccountAsync(e.DriverId, cancellationToken);

        if (livreur is null)
        {
            // ON N'ÉCHOUE PAS : L'ARGENT EST DÉJÀ AU PORTEFEUILLE.
            //
            // Le crédit est écrit et commité ; rejouer ce message ne le referait
            // pas — le contrôle d'idempotence le rejetterait — et ne ferait
            // qu'empiler des tentatives. Un livreur crédité mais introuvable au
            // référentiel signale en revanche une incohérence qui se corrige à la
            // main : elle doit sortir dans les alertes.
            _logger.LogError(
                "Gain crédité sur la course {DeliveryId} : livreur {DriverId} introuvable — "
                + "il ne sera PAS prévenu de son paiement.",
                e.DeliveryId, e.DriverId);

            return;
        }

        await _dispatcher.NotifyAsync(
            livreur.UserId,
            "Course payée",
            $"Votre gain de {e.Amount:0.00} {e.Currency} a été ajouté à votre solde.",
            "Delivery",
            e.DeliveryId,
            cancellationToken,
            alsoEmail: false);
    }
}
