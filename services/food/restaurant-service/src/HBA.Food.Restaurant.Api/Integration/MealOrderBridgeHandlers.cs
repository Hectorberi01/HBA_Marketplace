using HBA.Food.Application.Orders;
using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using MediatR;

using HBA.Food.Domain.Orders;

namespace HBA.Food.Api.Integration;

/// <summary>
/// Commande de repas confirmée chez food-order-service → ticket de cuisine ouvert.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// PERSONNE NE CONSOMMAIT `MealOrderConfirmed` : LE CLIENT ÉTAIT DÉBITÉ ET
///    AUCUNE CUISINE N'ÉTAIT SERVIE.
///
/// food-order-service publie ce fait depuis son extraction. Le seul ouvreur de
/// ticket du dépôt — <see cref="ReceiveFoodOrderOnOrderConfirmedHandler"/> —
/// écoute `OrderConfirmed`, l'événement de la MARKETPLACE. Une commande passée
/// par le parcours food traversait donc le paiement sans que rien ne s'ouvre en
/// cuisine : pas de ticket sur l'écran du restaurateur, pas de préparation, et
/// aucun des `FoodOrder*` de la suite du parcours — donc pas de course, pas de
/// livraison, pas de reversement. Le client payait un repas que personne ne
/// commençait.
///
/// Rien ne le signalait : un événement sans consommateur se consomme
/// silencieusement. Le dispatcher rend la main dès que la liste des
/// gestionnaires est vide.
///
/// L'ANCIEN GESTIONNAIRE RESTE EN PLACE — C'EST VOULU, ET C'EST TEMPORAIRE.
///
/// Les deux ouvrent le ticket par le MÊME chemin métier
/// (<see cref="ReceiveFoodOrderCommand"/>), depuis deux amonts différents :
/// celui-ci depuis food-order-service, l'autre depuis la marketplace pour les
/// commandes marquées `Kind == "Food"`. Tant que le chemin marketplace→food peut
/// encore porter une commande de repas, le retirer rouvrirait la panne d'en
/// face. Il s'enlèvera quand le contrat de confirmation commun annoncé dans
/// `MealOrderIntegrationEvents` aura été DÉPLACÉ — le lot suivant.
///
/// LES DEUX NE PEUVENT PAS DOUBLER UN TICKET, MÊME S'ILS PARTAIENT ENSEMBLE.
///
/// `ReceiveFoodOrderCommand` est idempotente sur `OrderId` : elle rend le ticket
/// existant au lieu d'en créer un second. Et les deux `OrderId` viennent
/// d'agrégats distincts — la commande marketplace et la commande de repas — donc
/// même un doublon fonctionnel resterait deux tickets distincts, pas un ticket
/// écrasé. Le seul vrai garde-fou contre ce cas-là est qu'un parcours donné ne
/// publie qu'un seul des deux faits.
///
/// ON NE RAPPELLE PERSONNE : L'ÉVÉNEMENT PORTE SES LIGNES.
///
/// C'est la différence de fond avec l'ancien chemin, et la raison pour laquelle
/// ce contrat existe. `OrderConfirmed` servait deux univers, ne pouvait rien
/// porter de spécifique à l'un, et obligeait le pont à un aller-retour gRPC vers
/// order-service puis à un filtre sur `Kind`. Ici il n'y a qu'un univers :
/// l'événement dit tout, et le ticket s'ouvre sans dépendre de la disponibilité
/// d'un autre service au moment du dispatch.
///
/// ON LÈVE PLUTÔT QUE D'IGNORER, POUR DE L'ARGENT DÉJÀ ENCAISSÉ.
///
/// La confirmation vient APRÈS le paiement. Une charge utile inexploitable
/// n'est pas un cas nominal à journaliser en passant : c'est une vente payée
/// sans contrepartie. Lever donne la fenêtre de reprise bornée du consommateur
/// Kafka, puis un journal Critical que quelqu'un voit — un avertissement, non.
///
/// LA TRACE D'INBOX EST BIEN COMMITTÉE — VÉRIFIÉ, PAS SUPPOSÉ.
///
/// `IntegrationEventDispatcher` ajoute la trace au `DbContext` avant l'appel et
/// compte sur l'effet métier pour la committer. Le chemin nominal d'ici écrit :
/// `ReceiveFoodOrderCommand` fait `AddAsync` puis `SaveChangesAsync` sur
/// `IFoodUnitOfWork`, qui EST le `FoodDbContext` auquel `EfConsumerInbox` est
/// lié (`FoodModuleInstaller`). Trace et ticket partent donc dans la même
/// transaction, et ce gestionnaire n'a rien à écrire pour cela.
///
/// Le seul chemin sans écriture est le rejeu déjà couvert par l'idempotence de
/// la commande : elle rend le ticket existant sans sauvegarder, la trace tombe
/// avec la portée, et un rejeu ultérieur repassera par le même court-circuit.
/// Aucun effet dupliqué n'en découle.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class ReceiveFoodOrderOnMealOrderConfirmedHandler
    : IIntegrationEventHandler<MealOrderConfirmedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<ReceiveFoodOrderOnMealOrderConfirmedHandler> _logger;

    public ReceiveFoodOrderOnMealOrderConfirmedHandler(
        ISender sender,
        ILogger<ReceiveFoodOrderOnMealOrderConfirmedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        MealOrderConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // AUCUN FILTRE SUR UN DISCRIMINANT, CONTRAIREMENT À L'ANCIEN CHEMIN.
        //
        // `OrderConfirmed` part pour TOUTES les commandes, d'où le test
        // `Kind == "Food"` d'en face. Ce contrat-ci n'existe que dans l'univers
        // food : tout ce qui arrive ici est un repas. Y ajouter un filtre
        // « par symétrie » ferait taire le gestionnaire le jour où le champ
        // recopié cesserait d'être renseigné.

        // Le restaurant est `required` au contrat, donc jamais absent — mais
        // « présent » et « renseigné » sont deux choses différentes, et un Guid
        // vide passe la contrainte du compilateur. Le laisser filer ouvrirait un
        // ticket sur un établissement nul, c'est-à-dire un client débité pour une
        // cuisine qui n'existe pas.
        if (integrationEvent.RestaurantId == Guid.Empty)
        {
            _logger.LogError(
                "Commande de repas {OrderId} confirmée SANS restaurant. Le client est débité et "
                + "aucune cuisine ne peut être servie.",
                integrationEvent.OrderId);

            throw new InvalidOperationException(
                $"Commande de repas {integrationEvent.OrderId} : confirmation sans restaurant.");
        }

        // LES OPTIONS SE RÉDUISENT À LEURS IDENTIFIANTS, ET LES PRIX SE PERDENT
        //    ICI VOLONTAIREMENT.
        //
        // La charge utile porte `UnitPrice` et le groupe de chaque option ;
        // `FoodOrderLineInput` n'accepte que des identifiants, parce que le prix
        // du ticket est RECALCULÉ par `MenuItem.PriceSelection` à la réception.
        // Un prix qui voyagerait depuis l'appelant serait un prix réécrivable, et
        // la validation de la sélection (options d'un autre plat, minimum et
        // maximum de groupe, article épuisé) ne se ferait plus.
        var lignes = integrationEvent.Lines
            .Select(l => new FoodOrderLineInput(
                l.MenuItemId,
                l.Quantity,
                l.Options.Select(o => o.OptionId).ToList(),
                l.Notes))
            .ToList();

        if (lignes.Count == 0)
        {
            _logger.LogError(
                "Commande de repas {OrderId} confirmée sans aucune ligne. Aucun ticket ouvert.",
                integrationEvent.OrderId);

            throw new InvalidOperationException(
                $"Commande de repas {integrationEvent.OrderId} : aucune ligne à préparer.");
        }

        var resultat = await _sender.Send(
            new ReceiveFoodOrderCommand(
                // L'IDENTIFIANT EST CELUI D'UNE `MealOrder`, PAS D'UNE COMMANDE
                // order-service.
                //
                // Sans ce discriminant, le ticket était indistinguable de celui de
                // l'autre pont — et la création de course allait demander l'adresse
                // de livraison à order-service, qui ne connaît pas cet identifiant.
                // Le repas était prêt, aucun livreur n'était cherché, et les
                // reprises Kafka s'épuisaient en silence.
                FoodOrderOrigin.Food,
                integrationEvent.OrderId,
                integrationEvent.RestaurantId,
                lignes,

                // LA NOTE DU CLIENT ARRIVE ENFIN JUSQU'À LA CUISINE.
                //
                // L'ancien chemin passait `null` faute de pouvoir la transporter :
                // `OrderConfirmed` ne la portait pas. « Sans piment », « allergie
                // arachide » restaient dans la commande et n'atteignaient jamais
                // le passe.
                integrationEvent.CustomerNote),
            cancellationToken);

        if (resultat.IsFailure)
        {
            _logger.LogError(
                "Ticket de cuisine NON ouvert pour la commande de repas {OrderId} — {Code} : {Message}.",
                integrationEvent.OrderId, resultat.Error.Code, resultat.Error.Message);

            throw new InvalidOperationException(
                $"Ticket impossible pour la commande de repas {integrationEvent.OrderId} : "
                + $"{resultat.Error.Code} — {resultat.Error.Message}");
        }

        _logger.LogInformation(
            "Ticket de cuisine {FoodOrderId} ouvert pour la commande de repas {OrderId} "
            + "({Lignes} ligne(s)).",
            resultat.Value, integrationEvent.OrderId, lignes.Count);
    }
}
