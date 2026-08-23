using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using HBA.Financial.Wallet.Application.Wallets;

namespace HBA.Financial.Wallet.Application.Earnings;

/// <summary>
/// La course est remise : le livreur est payé.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE GESTIONNAIRE MANQUAIT, ET LE LIVREUR N'ÉTAIT JAMAIS PAYÉ.
///
/// Tout le reste existait. `Delivery.MarkDelivered` calcule le gain, le
/// gestionnaire de domaine le pose sur `DeliveryCompletedIntegrationEvent`,
/// `CreditDriverEarningCommand` sait créditer, `DriverWallet` et sa table
/// existent, et l'écran « Revenus » de l'application livreur lit ce solde.
///
/// Il manquait le fil entre les deux bouts : `CreditDriverEarningCommand`
/// n'apparaissait NULLE PART ailleurs que dans sa propre définition. Le gain
/// était calculé, publié, et tombait dans le vide. Le portefeuille du livreur
/// restait à zéro à vie, et l'écran « Revenus » affichait un solde que rien ne
/// pouvait faire bouger.
///
/// ON NE FILTRE PAS SUR LA SOURCE NI SUR LE PRÉFIXE DE RÉFÉRENCE.
///
/// Les autres consommateurs de cet événement commencent par relire la référence
/// (« ORDER-… », « FOOD-… ») et sortent si elle n'est pas la leur. La tentation
/// est de faire pareil ici et d'écarter les courses de partenaire externe.
///
/// Ce serait un défaut de paiement. Vérification faite dans `Delivery` : une
/// course `ExternalPartner` désigne le donneur d'ORDRE — un site marchand tiers
/// qui passe par l'API partenaire — et elle est dispatchée aux MÊMES livreurs
/// HBA que les autres. Aucune notion de flotte appartenant au partenaire
/// n'existe dans le modèle : `AssignedDriverId` est toujours un `Driver` de
/// notre référentiel. Le tiers paie la course à HBA ; HBA paie son coursier.
/// Filtrer sur le préfixe ferait rouler des livreurs gratuitement.
///
/// CE QUI RESTE OUVERT, ET QUI N'EST PAS DE NOTRE RESSORT ICI : la part du
/// livreur sort du solde « livraison » de la plateforme, alimenté par les frais
/// encaissés auprès de l'ACHETEUR. Une course de partenaire n'alimente ce solde
/// nulle part — la facturation partenaire n'est pas branchée. Le solde part donc
/// en négatif, ce que `PlatformWallet.DebitShipping` autorise DÉLIBÉRÉMENT pour
/// rendre la perte visible. C'est un manque de recette à facturer, pas une raison
/// de ne pas payer l'homme qui a roulé.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class CreditDriverOnDeliveryCompletedHandler
    : IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<CreditDriverOnDeliveryCompletedHandler> _logger;

    public CreditDriverOnDeliveryCompletedHandler(
        ISender sender, ILogger<CreditDriverOnDeliveryCompletedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        DeliveryCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (integrationEvent.DriverId == Guid.Empty)
        {
            // `MarkDelivered` déréférence `AssignedDriverId!` : une course sans
            // livreur ne peut pas atteindre cet état. Si le cas arrive quand même,
            // c'est une donnée d'un autre âge ou une reprise manuelle en base — et
            // il n'y a personne à créditer. Rejouer n'y changerait rien.
            _logger.LogError(
                "Course {DeliveryId} remise SANS livreur affecté : aucun gain ne peut être versé.",
                integrationEvent.DeliveryId);

            return;
        }

        if (integrationEvent.DriverEarning is not { } gain)
        {
            // NUL N'EST PAS ZÉRO, ET C'EST TOUT L'INTÉRÊT DE LA DISTINCTION.
            //
            // Le domaine laisse le gain NUL quand la course n'avait aucun prix,
            // précisément pour que « aucun gain calculé » se cherche au lieu de se
            // confondre avec « zéro franc ». Le livreur a bien roulé : quelqu'un
            // doit regarder pourquoi cette course n'a pas été tarifée.
            _logger.LogError(
                "Course {DeliveryId} remise par le livreur {DriverId} SANS gain calculé — "
                + "la course n'avait aucun prix. Le livreur a roulé et n'est pas payé.",
                integrationEvent.DeliveryId, integrationEvent.DriverId);

            return;
        }

        if (gain <= 0m)
        {
            // Une part à zéro ou négative vient d'un taux ou d'un prix aberrant.
            // `DriverWallet.CreditEarning` la refuserait de toute façon ; l'arrêter
            // ici donne un journal qui nomme la cause au lieu d'un échec de commande.
            _logger.LogError(
                "Course {DeliveryId} : gain de {Gain} pour le livreur {DriverId} — "
                + "montant non versable. Vérifiez le prix de la course et la part livreur.",
                integrationEvent.DeliveryId, gain, integrationEvent.DriverId);

            return;
        }

        var result = await _sender.Send(
            new CreditDriverEarningCommand(
                integrationEvent.DriverId,
                integrationEvent.DeliveryId,
                gain,
                integrationEvent.Currency),
            cancellationToken);

        // NE JAMAIS ÉCRIRE `=> _sender.Send(...)` ICI.
        //
        // C'est le défaut corrigé sur les deux gestionnaires de dénouement de
        // paiement : un `Result` ni inspecté, ni journalisé, ni levé, et le message
        // acquitté quand même. Ici il coûterait le salaire d'un livreur, en silence
        // — le genre de panne que personne ne découvre avant la réclamation.
        //
        // `SagaOutcome` fait le tri : une devise incompatible ne redeviendra pas
        // compatible en rejouant (on journalise et on acquitte), une base
        // indisponible passera au prochain essai (on lève, l'outbox rejoue).
        SagaOutcome.Exiger(
            result, _logger,
            "créditer le gain du livreur — SANS ELLE, LE COURSIER A ROULÉ POUR RIEN",
            integrationEvent.DriverId, integrationEvent.DeliveryId, gain);
    }
}
