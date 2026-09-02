using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Application.Abstractions;
using HBA.Orders.Application.Orders.Commands.PlaceOrder;
using HBA.Orders.Application.Orders.EventHandlers;
using HBA.Orders.Contracts;
using HBA.Orders.Domain.Orders;
using HBA.Orders.Domain.Orders.Events;
using HBA.Orders.Domain.Orders.SellerOrders;
using HBA.Orders.Domain.Orders.SellerOrders.Events;
using HBA.Orders.Infrastructure.Persistence;
using HBA.Orders.Infrastructure.Public;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Returns.Contracts.IntegrationEvents;

namespace HBA.Orders.Infrastructure;

/// <summary>Enregistre le module Ordering : DbContext, repository, API publique, Saga handlers, validators, outbox.</summary>
public sealed class OrderingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Ordering";

    public Assembly ApplicationAssembly => typeof(PlaceOrderCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<OrderingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", OrderingDbContext.SchemaName)));

        services.AddScoped<IOrderingUnitOfWork>(sp => sp.GetRequiredService<OrderingDbContext>());

        services.AddScoped<IOrderRepository, OrderRepository>();

        // SANS CETTE LIGNE, LES CINQ ROUTES VENDEUR NE DÉMARRENT PAS.
        //
        // `SellerOrderCommandHandler`, `ListOrdersBySellerQueryHandler`,
        // `ConfirmOrderPaymentCommandHandler` et le gestionnaire de cascade
        // l'injectent tous. Contrairement à un gestionnaire d'événement oublié —
        // qui laisse le service démarrer et échoue en silence au premier message —
        // celle-ci fait échouer `ValidateOnBuild`, donc le démarrage. C'est le
        // bon sens du défaut, et c'est aussi ce que le contrôle `(supprimé le 28 août 2026)` attrape.
        services.AddScoped<ISellerOrderRepository, SellerOrderRepository>();

        services.AddScoped<IOrderingModuleApi, OrderingModuleApi>();

        // SANS CETTE LIGNE, LES NEUF CONSOMMATEURS DU SERVICE SONT REJOUABLES.
        //
        // Six sont enregistrés plus bas ; les trois autres — course à créer, course
        // annulée, commande annulée — le sont dans `HBA.Order.Api/Program.cs`. Ils
        // dépendent tous de CETTE ligne, parce que la garde est portée par
        // `IntegrationEventDispatcher` et non par les gestionnaires.
        //
        // Le dispatcher résout `IConsumerInbox` en OPTIONNEL : sans enregistrement,
        // le service démarre et consomme sans garde, avec un simple avertissement
        // au premier message. La trace atterrit dans `ordering.consumer_inbox`.
        services.AddScoped<IConsumerInbox, EfConsumerInbox<OrderingDbContext>>();

        services.AddScoped<IDomainEventHandler<OrderPlacedDomainEvent>, OrderPlacedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<OrderConfirmedDomainEvent>, OrderConfirmedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<OrderCancelledDomainEvent>, OrderCancelledDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<OrderDeliveredDomainEvent>, OrderDeliveredDomainEventHandler>();

        // LA SORTIE DE SECOURS DOIT SE VOIR HORS DU MODULE.
        //
        // Sans ces deux lignes, la commande changerait d'état en silence : le
        // dossier d'arbitrage s'ouvrirait dans la base et l'acheteur continuerait
        // d'attendre un colis, sans le moindre message. C'est exactement le
        // silence que cette transition existe pour rompre.
        services.AddScoped<IDomainEventHandler<OrderUnderReviewDomainEvent>, OrderUnderReviewDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<OrderResumedAfterReviewDomainEvent>, OrderResumedAfterReviewDomainEventHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // LE REFUS D'UN VENDEUR DOIT SORTIR DU MODULE (ISSUE-027).
        //
        // ET IL N'ARRIVE ENCORE NULLE PART. `SellerOrderRefusedIntegrationEvent`
        // n'a AUCUN consommateur : un refus vendeur ne libère pas le stock, ne
        // rembourse pas la part et ne prévient pas le client. Les trois gestes
        // vivent dans inventory-service, financial-service et
        // communication-service, hors du périmètre de ce lot.
        //
        // Sans cette ligne, le refus ne laisserait qu'une ligne changée dans une
        // table : le fait n'existerait même pas sur le bus le jour où le premier
        // consommateur sera branché. Le gestionnaire journalise d'ailleurs en
        // `Warning` — c'est aujourd'hui la seule chose qui met un humain au
        // courant qu'un client a payé pour ce qui ne viendra pas.
        //
        // DEUX GESTIONNAIRES SUR `OrderCancelled`, ET C'EST VOULU.
        //
        // Le premier publie l'événement d'intégration ; le second ferme les parts
        // vendeur d'une commande annulée après confirmation — sans quoi le vendeur
        // prépare un colis pour une vente déjà remboursée. `DomainEventDispatcher`
        // résout par `GetServices`, donc les deux s'exécutent.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<IDomainEventHandler<SellerOrderRefusedDomainEvent>, SellerOrderRefusedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<OrderCancelledDomainEvent>, CancelSellerOrdersOnOrderCancelledHandler>();

        // Suite du Saga : réactions aux résultats de paiement.
        services.AddScoped<IIntegrationEventHandler<PaymentCapturedIntegrationEvent>, ConfirmOrderOnPaymentCapturedHandler>();
        services.AddScoped<IIntegrationEventHandler<PaymentFailedIntegrationEvent>, CancelOrderOnPaymentFailedHandler>();

        // Étape finale du Saga : la course terminée clôt la commande (déclenche
        // escrow + payout vendeur).
        //
        // C'EST DELIVERY QUI L'ANNONCE, PLUS SHIPPING.
        //
        // Le module Shipping n'a pas été extrait du monolithe. L'ancien
        // gestionnaire réclamait `IShippingModuleApi`, que personne ne fournit :
        // la validation du conteneur refusait de démarrer le service. Voir
        // `MarkOrderDeliveredOnDeliveryCompletedHandler` pour ce que la bascule
        // coûte — le multi-colis.
        services.AddScoped<IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>, MarkOrderDeliveredOnDeliveryCompletedHandler>();

        // LE RESTAURANT REFUSE → LA COMMANDE EST ANNULÉE.
        //
        // Sans ces deux lignes, le ticket passe « refusé » et la commande reste
        // « confirmée » : le client est débité pour un repas qui n'existera
        // jamais, et rien ne relie les deux faits.
        //
        // L'annulation publie `OrderCancelled` ; c'est financial-service qui
        // rembourse en la consommant. order-service annonce, il n'ordonne pas.
        services.AddScoped<IIntegrationEventHandler<FoodOrderRejectedIntegrationEvent>, CancelOrderOnFoodOrderRejectedHandler>();
        services.AddScoped<IIntegrationEventHandler<FoodOrderCancelledIntegrationEvent>, CancelOrderOnFoodOrderCancelledHandler>();

        // LE REPAS EST REMIS AU CLIENT → LA COMMANDE EST LIVRÉE.
        //
        // Sans cette ligne, une commande de repas ne se terminait JAMAIS : elle
        // restait « confirmée », `OrderDelivered` n'était jamais publié, l'escrow
        // n'était pas levé et le gain du restaurateur restait bloqué en « à
        // venir ». Le repas était remis au client et le restaurateur n'était
        // jamais payé.
        //
        // Le gestionnaire au-dessus, branché sur la fin de course, ne pouvait pas
        // s'en charger : il ne lit que « ORDER- », et le GUID d'une référence
        // « FOOD- » est celui du TICKET, inconnu de cette base. C'est food-service
        // qui traduit, en publiant `FoodOrderDelivered` avec l'`OrderId`.
        services.AddScoped<IIntegrationEventHandler<FoodOrderDeliveredIntegrationEvent>, MarkOrderDeliveredOnFoodOrderDeliveredHandler>();

        // SANS CETTE LIGNE, LA COMMANDE N'APPREND JAMAIS QU'UN ARTICLE EST REVENU.
        //
        // `GetOrderReturnContextAsync` répondait `AlreadyReturnedQuantity: 0` et
        // `AlreadyRefundedAmount: 0m` en dur (ISSUE-014). Ce gestionnaire est la
        // seule source d'order-service sur les retours : non enregistré, il ne
        // manque rien au démarrage, aucune erreur n'apparaît, et le même
        // exemplaire se rembourse autant de fois qu'on ouvre de dossiers.
        services.AddScoped<IIntegrationEventHandler<ReturnRefundedIntegrationEvent>, RecordReturnSettlementOnRefundHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<OrderingDbContext>();
    }
}
