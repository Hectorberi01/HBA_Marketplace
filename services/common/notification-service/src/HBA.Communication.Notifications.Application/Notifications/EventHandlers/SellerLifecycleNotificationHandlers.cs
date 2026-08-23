using Microsoft.Extensions.Logging;
using HBA.Shared.IntegrationEvents;
using HBA.Inventory.Contracts.IntegrationEvents;
using HBA.Products.Contracts;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.Ordering.Contracts;
using HBA.Engagement.Reviews.Contracts.IntegrationEvents;
using HBA.Merchants.Contracts;
using HBA.Merchants.Contracts.IntegrationEvents;

namespace HBA.Communication.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// Prévient l'ADMIN qu'une boutique vient d'être créée et attend sa validation KYB.
/// Sans cela, un dossier pouvait dormir des jours : rien ne signalait son arrivée.
/// </summary>
public sealed class SellerRegisteredAdminNotificationHandler : IIntegrationEventHandler<SellerRegisteredIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly AdminNotificationTarget _admin;

    public SellerRegisteredAdminNotificationHandler(NotificationDispatcher dispatcher, AdminNotificationTarget admin)
    {
        _dispatcher = dispatcher;
        _admin = admin;
    }

    public async Task HandleAsync(SellerRegisteredIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (await _admin.ResolveAsync(cancellationToken) is not { } adminUserId)
        {
            return;
        }

        await _dispatcher.NotifyAsync(
            adminUserId,
            "Nouvelle boutique à valider",
            $"La boutique « {e.ShopName} » a été créée et attend la vérification de son dossier KYB.",
            "Seller",
            e.SellerId,
            cancellationToken);
    }
}

/// <summary>
/// Confirme au vendeur la FERMETURE qu'il a lui-même demandée.
///
/// CE MESSAGE ANNONÇAIT UNE SUSPENSION. Il disait « Votre boutique a été
/// suspendue […] contactez le support pour en connaître le motif » à quelqu'un
/// qui venait de fermer son compte de son plein gré. Le vendeur était accusé
/// d'une sanction inexistante, et invité à appeler pour un motif qui n'existait
/// pas. La vraie suspension, elle, ne prévenait personne — voir
/// SellerSuspendedNotificationHandler, ajouté depuis.
/// </summary>
public sealed class SellerClosedNotificationHandler : IIntegrationEventHandler<SellerClosedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public SellerClosedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(SellerClosedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.UserId,
            "Boutique fermée",
            "Votre boutique est fermée à votre demande : vos produits ne sont plus en vente. "
            + "Votre compte et votre historique sont conservés, et vous pouvez demander sa réouverture à tout moment.",
            "Seller",
            e.SellerId,
            cancellationToken,
            alsoEmail: true);
}

/// <summary>
/// Prévient le vendeur que sa boutique a été SUSPENDUE par la plateforme.
///
/// CETTE NOTIFICATION N'EXISTAIT PAS. La suspension retire tout son catalogue
/// de la vente : la découvrir par la chute de ses commandes est le pire cas — il
/// perd des jours à chercher une panne qui n'existe pas.
///
/// LE MOTIF EST REPRIS S'IL EXISTE. « Contactez le support » sans rien d'autre
/// fait payer au vendeur un appel pour une information qu'on avait déjà.
///
/// Doublée par e-mail : il n'ouvrira pas forcément l'application ce jour-là, et
/// c'est justement le jour où il doit savoir.
/// </summary>
public sealed class SellerSuspendedNotificationHandler : IIntegrationEventHandler<SellerSuspendedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public SellerSuspendedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(SellerSuspendedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.UserId,
            "Boutique suspendue",
            string.IsNullOrWhiteSpace(e.Reason)
                ? "Votre boutique a été suspendue : vos produits ne sont plus en vente. Contactez le support pour en connaître le motif."
                : $"Votre boutique a été suspendue : vos produits ne sont plus en vente. Motif : {e.Reason}",
            "Seller",
            e.SellerId,
            cancellationToken,
            alsoEmail: true);
}

/// <summary>Prévient le vendeur que sa suspension est levée.</summary>
public sealed class SellerSuspensionLiftedNotificationHandler : IIntegrationEventHandler<SellerSuspensionLiftedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public SellerSuspensionLiftedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(SellerSuspensionLiftedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.UserId,
            "Suspension levée",
            "Votre boutique est de nouveau active. Les fiches retirées pour cette suspension sont revenues en vente ; "
            + "celles retirées pour un autre motif restent à corriger.",
            "Seller",
            e.SellerId,
            cancellationToken,
            alsoEmail: true);
}

/// <summary>
/// Prévient le vendeur que son dossier KYB a été REFUSÉ, et lui dit POURQUOI.
///
/// AUCUN MOTIF NE LUI PARVENAIT — ni même le refus. Il voyait un statut
/// « Rejeté » sur son écran, sans savoir quelle pièce corriger : il redéposait la
/// même, la modération la refusait à nouveau, et les deux s'épuisaient.
///
/// Un refus sans motif n'est pas une décision de modération, c'est une impasse.
/// </summary>
public sealed class SellerKybRejectedNotificationHandler : IIntegrationEventHandler<SellerKybRejectedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public SellerKybRejectedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(SellerKybRejectedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.UserId,
            "Dossier de vérification refusé",
            string.IsNullOrWhiteSpace(e.Reason)
                ? "Votre dossier de vérification a été refusé. Déposez de nouvelles pièces depuis votre espace vendeur ; "
                  + "contactez le support si vous ne voyez pas ce qui doit être corrigé."
                : $"Votre dossier de vérification a été refusé. Motif : {e.Reason}. "
                  + "Déposez les pièces corrigées depuis votre espace vendeur : votre dossier repassera automatiquement en revue.",
            "Seller",
            e.SellerId,
            cancellationToken,
            alsoEmail: true);
}

/// <summary>Prévient le vendeur que sa boutique est rouverte.</summary>
public sealed class SellerReactivatedNotificationHandler : IIntegrationEventHandler<SellerReactivatedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public SellerReactivatedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(SellerReactivatedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.UserId,
            "Boutique réactivée",
            "Votre boutique est de nouveau active : vos produits sont visibles et vous pouvez recevoir des commandes.",
            "Seller",
            e.SellerId,
            cancellationToken,
            alsoEmail: true);
}

/// <summary>
/// Prévient le vendeur qu'un avis vient d'être publié sur l'un de ses produits.
/// L'événement ne porte que le produit : on remonte au vendeur via le catalogue.
/// </summary>
/// <remarks>
/// `IProductsModuleApi` ET NON `ICatalogModuleApi` — SUBSTITUTION DÉLIBÉRÉE.
///
/// Dans le monolithe, ce gestionnaire appelait `ICatalogModuleApi` : les deux
/// modules vivaient dans le même processus, n'importe quelle interface faisait
/// l'affaire. Ici l'appel doit traverser le réseau, et `ICatalogModuleApi`
/// n'existe QUE côté serveur — `HBA.Catalog.Contracts.Grpc` expose le service,
/// pas de client. Aucun moyen de joindre catalog-service par cette interface.
///
/// `IProductsModuleApi` a, lui, un client gRPC (`AddProductsGrpcClient`) qui
/// pointe vers `Services:Catalog`. Son `ProductSummary` porte `SellerId` et
/// `Name`, les deux seuls champs utilisés ici : la substitution ne change rien
/// au message envoyé.
///
/// Elle va d'ailleurs dans le sens déjà acté de la dualité Catalog/Products,
/// Products étant le successeur.
/// </remarks>
public sealed class ReviewPublishedNotificationHandler : IIntegrationEventHandler<ReviewPublishedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IProductsModuleApi _catalog;
    private readonly ISellerModuleApi _sellers;
    private readonly ILogger<ReviewPublishedNotificationHandler> _logger;

    public ReviewPublishedNotificationHandler(
        NotificationDispatcher dispatcher,
        IProductsModuleApi catalog,
        ISellerModuleApi sellers,
        ILogger<ReviewPublishedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _catalog = catalog;
        _sellers = sellers;
        _logger = logger;
    }

    public async Task HandleAsync(ReviewPublishedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var product = await _catalog.GetProductAsync(e.ProductId, cancellationToken);
        if (product is null)
        {
            _logger.LogWarning("Avis {ReviewId} : produit {ProductId} introuvable — vendeur non prévenu.", e.ReviewId, e.ProductId);
            return;
        }

        // SellerId → UserId : le jeton d'appareil est porté par le compte.
        var seller = await _sellers.GetSellerAsync(product.SellerId, cancellationToken);
        if (seller is null)
        {
            _logger.LogWarning("Avis {ReviewId} : vendeur {SellerId} introuvable — non prévenu.", e.ReviewId, product.SellerId);
            return;
        }

        var stars = new string('★', Math.Clamp(e.Rating, 0, 5));

        await _dispatcher.NotifyAsync(
            seller.UserId,
            "Nouvel avis client",
            $"« {product.Name} » a reçu un avis {stars} ({e.Rating}/5).",
            "Review",
            e.ProductId,
            cancellationToken);
    }
}

/// <summary>
/// Prévient le vendeur qu'une de ses références est en RUPTURE. Un produit en rupture
/// ne se vend plus : chaque heure sans le savoir est une vente perdue.
/// </summary>
public sealed class StockDepletedNotificationHandler : IIntegrationEventHandler<StockDepletedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IProductsModuleApi _products;
    private readonly ISellerModuleApi _sellers;
    private readonly ILogger<StockDepletedNotificationHandler> _logger;

    public StockDepletedNotificationHandler(
        NotificationDispatcher dispatcher,
        IProductsModuleApi products,
        ISellerModuleApi sellers,
        ILogger<StockDepletedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _products = products;
        _sellers = sellers;
        _logger = logger;
    }

    public async Task HandleAsync(StockDepletedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // L'inventaire raisonne en SKU ; c'est l'offre qui connaît le vendeur.
        var offres = await _products.ListOffersBySkuAsync(e.Sku, cancellationToken);

        if (offres.Count == 0)
        {
            _logger.LogWarning("Rupture {Sku} : aucune offre correspondante — vendeur non prévenu.", e.Sku);
            return;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ON PRÉVIENT TOUS LES VENDEURS CONCERNÉS, PLUS UN SEUL.
        //
        // L'ancien code prenait `.FirstOrDefault()` sur la liste des offres
        // portant ce SKU. Or le SKU n'est unique QU'AU SEIN D'UN PRODUIT : deux
        // produits distincts peuvent porter la même référence, et l'inventaire
        // les indexe ensemble.
        //
        // Un seul vendeur — celui que la base rendait en premier — était donc
        // prévenu, et les autres découvraient la rupture en constatant l'absence
        // de commandes. Le tirage était en plus instable : rien ne garantit
        // l'ordre d'une requête sans tri.
        //
        // On déduplique par vendeur : un même vendeur ayant plusieurs offres sur
        // cette référence ne doit pas recevoir trois fois le même message.
        // ═════════════════════════════════════════════════════════════════════
        var vendeurs = offres.Select(o => o.SellerId).Distinct().ToList();

        foreach (var sellerId in vendeurs)
        {
            var seller = await _sellers.GetSellerAsync(sellerId, cancellationToken);
            if (seller is null)
            {
                _logger.LogWarning(
                    "Rupture {Sku} : vendeur {SellerId} introuvable — non prévenu.", e.Sku, sellerId);
                continue;
            }

            await _dispatcher.NotifyAsync(
                seller.UserId,
                "Rupture de stock",
                $"La référence {e.Sku} n'a plus de stock : elle n'est plus proposée à la vente. "
                + "Réapprovisionnez-la pour la remettre en ligne.",
                "Order",
                e.InventoryItemId,
                cancellationToken);
        }
    }
}

/// <summary>
/// Prévient l'acheteur que son PAIEMENT A ÉCHOUÉ. Sans ce message, il croit sa commande
/// passée et attend un colis qui ne partira jamais.
/// </summary>
public sealed class PaymentFailedNotificationHandler : IIntegrationEventHandler<PaymentFailedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IOrderingModuleApi _orders;
    private readonly ILogger<PaymentFailedNotificationHandler> _logger;

    public PaymentFailedNotificationHandler(
        NotificationDispatcher dispatcher, IOrderingModuleApi orders, ILogger<PaymentFailedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _orders = orders;
        _logger = logger;
    }

    public async Task HandleAsync(PaymentFailedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetOrderAsync(e.OrderId, cancellationToken);
        if (order is null)
        {
            _logger.LogWarning("Paiement {PaymentId} échoué : commande {OrderId} introuvable.", e.PaymentId, e.OrderId);
            return;
        }

        // Le MOTIF technique du PSP n'est pas repris : il est rarement compréhensible et
        // parfois révélateur (numéro, solde). On invite à réessayer.
        await _dispatcher.NotifyAsync(
            order.BuyerId,
            "Paiement échoué",
            "Votre paiement n'a pas abouti et votre commande n'a pas été validée. Vous pouvez réessayer depuis votre panier.",
            "Payment",
            e.OrderId,
            cancellationToken,
            alsoEmail: true);
    }
}
