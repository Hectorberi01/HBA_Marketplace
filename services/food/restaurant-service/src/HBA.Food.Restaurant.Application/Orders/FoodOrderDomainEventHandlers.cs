using HBA.Food.Contracts.IntegrationEvents;
using HBA.Food.Domain.Orders;
using HBA.Food.Domain.Orders.Events;
using static HBA.Food.Application.Orders.FoodOrderOriginTranslation;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.Food.Application.Orders;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES HUIT PUBLICATEURS DE LA COMMANDE FOOD (cahier §19).
///
/// L'OUTBOX DU SCHÉMA « food » TOURNAIT À VIDE POUR LES COMMANDES.
///
/// Les événements de domaine étaient levés correctement par l'agrégat, et aucun
/// ne franchissait la frontière du module. C'est le défaut que ce dépôt a déjà
/// corrigé pour le cycle de vie des établissements — reproduit ici, un cran plus
/// bas, sur des messages autrement plus urgents : celui du refus laisse un client
/// débité sans repas, celui de la mise à disposition n'appelle aucun livreur.
///
/// UN FICHIER, HUIT CLASSES, ET C'EST DÉLIBÉRÉ. Les séparer aurait dispersé une
/// correspondance qui se lit d'un coup d'œil : à chaque événement de domaine, son
/// jumeau d'intégration. Un manquant se voit ici ; réparti sur huit fichiers, non.
///
/// CHACUN TRADUIT L'ORIGINE, ET C'EST LA FRONTIÈRE QUI L'IMPOSE.
///
/// L'agrégat porte `FoodOrderOrigin`, une énumération du DOMAINE. Le contrat, lui,
/// porte une chaîne : `HBA.Food.Restaurant.Contracts` ne référence que le socle,
/// et un consommateur qui devrait tirer le domaine de restaurant-service pour lire
/// un événement ferait la dépendance que la frontière du module interdit. La
/// traduction est donc ici, en un seul endroit — `Traduire`, en bas de fichier.
///
/// ET IL EN MANQUAIT UN — LA REMISE AU CLIENT.
///
/// Ils étaient sept pour huit transitions : « livré » n'avait ni événement de
/// domaine, ni jumeau d'intégration, ni publicateur. La démonstration exacte de
/// l'argument ci-dessus, sauf que le trou n'a pas sauté aux yeux parce que
/// l'agrégat ne levait rien à traduire. Conséquence : le repas était remis au
/// client et le restaurateur n'était jamais payé.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class FoodOrderReceivedDomainEventHandler : IDomainEventHandler<FoodOrderReceivedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FoodOrderReceivedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(FoodOrderReceivedDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new FoodOrderReceivedIntegrationEvent
            {
                FoodOrderId = e.FoodOrderId,
                OrderId = e.OrderId,
                OrderOrigin = Traduire(e.Origin),
                RestaurantId = e.RestaurantId,
                Total = e.Total,
                ItemCount = e.ItemCount
            },
            cancellationToken);
}

public sealed class FoodOrderAcceptedDomainEventHandler : IDomainEventHandler<FoodOrderAcceptedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FoodOrderAcceptedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(FoodOrderAcceptedDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new FoodOrderAcceptedIntegrationEvent
            {
                FoodOrderId = e.FoodOrderId,
                OrderId = e.OrderId,
                OrderOrigin = Traduire(e.Origin),
                RestaurantId = e.RestaurantId,
                EstimatedPreparationMinutes = e.EstimatedPreparationMinutes,
                AcceptedByUserId = e.AcceptedByUserId
            },
            cancellationToken);
}

/// <summary>
/// LE PLUS URGENT DES SEPT : LE CLIENT A PAYÉ.
///
/// Un refus qui ne remonte pas laisse un débit sans contrepartie. Le client
/// découvrirait tout seul, au bout d'une heure d'attente, qu'il n'aura pas de
/// repas — et personne chez HBA ne le saurait avant lui.
/// </summary>
public sealed class FoodOrderRejectedDomainEventHandler : IDomainEventHandler<FoodOrderRejectedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FoodOrderRejectedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(FoodOrderRejectedDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new FoodOrderRejectedIntegrationEvent
            {
                FoodOrderId = e.FoodOrderId,
                OrderId = e.OrderId,
                OrderOrigin = Traduire(e.Origin),
                RestaurantId = e.RestaurantId,
                Reason = e.Reason,
                Comment = e.Comment
            },
            cancellationToken);
}

public sealed class FoodOrderPreparationStartedDomainEventHandler
    : IDomainEventHandler<FoodOrderPreparationStartedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FoodOrderPreparationStartedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        FoodOrderPreparationStartedDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new FoodOrderPreparingIntegrationEvent
            {
                FoodOrderId = e.FoodOrderId,
                OrderId = e.OrderId,
                OrderOrigin = Traduire(e.Origin),
                RestaurantId = e.RestaurantId
            },
            cancellationToken);
}

/// <summary>
/// CELUI QUI APPELLE UN LIVREUR (§24).
///
/// Le raccordement à HBA Delivery reste à écrire, au composition root — Food ne
/// connaît aucun autre module. Mais l'événement DOIT sortir dès maintenant :
/// sinon le jour du raccordement, il faudrait aussi se souvenir de le publier, et
/// c'est exactement l'oubli qui produit des sacs qui refroidissent sur un passe.
/// </summary>
public sealed class FoodOrderReadyForPickupDomainEventHandler
    : IDomainEventHandler<FoodOrderReadyForPickupDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FoodOrderReadyForPickupDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        FoodOrderReadyForPickupDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new FoodOrderReadyForPickupIntegrationEvent
            {
                FoodOrderId = e.FoodOrderId,
                OrderId = e.OrderId,
                OrderOrigin = Traduire(e.Origin),
                RestaurantId = e.RestaurantId,
                ReadyAtUtc = e.ReadyAtUtc
            },
            cancellationToken);
}

public sealed class FoodOrderPickedUpDomainEventHandler : IDomainEventHandler<FoodOrderPickedUpDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FoodOrderPickedUpDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(FoodOrderPickedUpDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new FoodOrderPickedUpIntegrationEvent
            {
                FoodOrderId = e.FoodOrderId,
                OrderId = e.OrderId,
                OrderOrigin = Traduire(e.Origin),
                RestaurantId = e.RestaurantId
            },
            cancellationToken);
}

/// <summary>
/// CELUI QUI FAIT PAYER LE RESTAURATEUR.
///
/// C'est le dernier maillon avant l'argent : order-service le consomme pour
/// clore la commande commerciale, ce qui publie <c>OrderDelivered</c>, ce qui
/// lève l'escrow et fait passer le gain du restaurateur de « à venir » à
/// « disponible ». Sans ce publicateur, la remise au client restait un fait
/// privé du module Food et le solde ne bougeait jamais.
/// </summary>
public sealed class FoodOrderDeliveredDomainEventHandler : IDomainEventHandler<FoodOrderDeliveredDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FoodOrderDeliveredDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(FoodOrderDeliveredDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new FoodOrderDeliveredIntegrationEvent
            {
                FoodOrderId = e.FoodOrderId,
                OrderId = e.OrderId,
                OrderOrigin = Traduire(e.Origin),
                RestaurantId = e.RestaurantId
            },
            cancellationToken);
}

public sealed class FoodOrderCancelledDomainEventHandler : IDomainEventHandler<FoodOrderCancelledDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FoodOrderCancelledDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(FoodOrderCancelledDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new FoodOrderCancelledIntegrationEvent
            {
                FoodOrderId = e.FoodOrderId,
                OrderId = e.OrderId,
                OrderOrigin = Traduire(e.Origin),
                RestaurantId = e.RestaurantId,
                Reason = e.Reason,
                WasInKitchen = e.WasInKitchen
            },
            cancellationToken);
}

/// <summary>
/// La traduction domaine → contrat de l'univers de la commande.
/// </summary>
/// <remarks>
/// `ToString()` AURAIT MARCHÉ, ET C'EST EXACTEMENT POUR ÇA QU'ON NE L'UTILISE
/// PAS.
///
/// Les noms coïncident aujourd'hui (`Marketplace`, `Food`). S'appuyer là-dessus
/// ferait dépendre un CONTRAT PUBLIÉ SUR KAFKA du nom d'un membre d'énumération
/// interne : renommer `FoodOrderOrigin.Food` — geste banal, purement local —
/// changerait silencieusement la charge utile, et tous les filtres des
/// consommateurs cesseraient de passer sans qu'une seule compilation échoue.
///
/// Le `switch` exhaustif fait l'inverse : ajouter une valeur au domaine sans la
/// déclarer au contrat ne compile pas.
/// </remarks>
// PUBLIQUE : l'infrastructure s'en sert aussi, pour `FoodModuleApi.GetOrderAsync`.
// Une seconde traduction, même de trois lignes, finirait par diverger de
// celle-ci — et une divergence sur ce champ précis renvoie un consommateur vers
// la mauvaise base.
public static class FoodOrderOriginTranslation
{
    public static string Traduire(FoodOrderOrigin origine) => origine switch
    {
        FoodOrderOrigin.Marketplace => FoodOrderOrigins.Marketplace,
        FoodOrderOrigin.Food => FoodOrderOrigins.Food,
        _ => throw new ArgumentOutOfRangeException(
            nameof(origine), origine,
            "Univers de commande inconnu : aucune valeur de contrat ne lui correspond.")
    };
}
