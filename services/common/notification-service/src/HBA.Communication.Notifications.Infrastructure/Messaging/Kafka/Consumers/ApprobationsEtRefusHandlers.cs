using HBA.Catalog.Contracts.IntegrationEvents;
using HBA.Communication.Notifications.Application.Notifications;
using HBA.Engagement.Reviews.Contracts.IntegrationEvents;
using HBA.Merchants.Contracts;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Products.Contracts;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>LES DECISIONS FAVORABLES NE PREVENAIENT PERSONNE.</summary>

/// <summary>Previent le vendeur que son dossier KYB est ACCEPTE.</summary>
[NomDeConsommateur("HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers.SellerKybApprovedNotificationHandler")]
public sealed class SellerKybApprovedNotificationHandler : IIntegrationEventHandler<SellerKybApprovedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;

    public SellerKybApprovedNotificationHandler(NotificationDispatcher dispatcher) => _dispatcher = dispatcher;

    public Task HandleAsync(SellerKybApprovedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _dispatcher.NotifyAsync(
            e.UserId,
            "Dossier de vérification accepté",
            // ON NE PROMET PAS L'OUVERTURE DE LA BOUTIQUE.
            "Votre dossier de vérification a été accepté. L'ouverture de votre boutique fait l'objet "
            + "d'une dernière étape : vous serez prévenu dès qu'elle est active.",
            "Seller",
            e.SellerId,
            cancellationToken,
            alsoEmail: true);
}

/// <summary>Prévient le vendeur que sa fiche produit est acceptée.</summary>
[NomDeConsommateur("HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers.ProductApprovedNotificationHandler")]
public sealed class ProductApprovedNotificationHandler : IIntegrationEventHandler<ProductApprovedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IProductsModuleApi _catalogue;
    private readonly ISellerModuleApi _vendeurs;
    private readonly ILogger<ProductApprovedNotificationHandler> _logger;

    public ProductApprovedNotificationHandler(
        NotificationDispatcher dispatcher,
        IProductsModuleApi catalogue,
        ISellerModuleApi vendeurs,
        ILogger<ProductApprovedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _catalogue = catalogue;
        _vendeurs = vendeurs;
        _logger = logger;
    }

    public async Task HandleAsync(ProductApprovedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var vendeur = await _vendeurs.GetSellerAsync(e.SellerId, cancellationToken);
        if (vendeur is null)
        {
            _logger.LogWarning(
                "Produit {ProductId} accepté : vendeur {SellerId} introuvable — non prévenu.",
                e.ProductId, e.SellerId);

            return;
        }

        // LE NOM DU PRODUIT EST UN CONFORT, PAS UNE CONDITION. Une fiche
        // introuvable ne doit pas priver le vendeur de la nouvelle.
        var produit = await _catalogue.GetProductAsync(e.ProductId, cancellationToken);
        var designation = produit is null ? "Votre fiche produit" : $"« {produit.Name} »";

        await _dispatcher.NotifyAsync(
            vendeur.UserId,
            "Fiche produit acceptée",
            $"{designation} a été acceptée par la modération et peut être publiée depuis votre espace vendeur.",
            "Product",
            e.ProductId,
            cancellationToken);
    }
}

/// <summary>Prévient le vendeur que sa fiche produit est REFUSÉE.</summary>
[NomDeConsommateur("HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers.ProductRejectedNotificationHandler")]
public sealed class ProductRejectedNotificationHandler : IIntegrationEventHandler<ProductRejectedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IProductsModuleApi _catalogue;
    private readonly ISellerModuleApi _vendeurs;
    private readonly ILogger<ProductRejectedNotificationHandler> _logger;

    public ProductRejectedNotificationHandler(
        NotificationDispatcher dispatcher,
        IProductsModuleApi catalogue,
        ISellerModuleApi vendeurs,
        ILogger<ProductRejectedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _catalogue = catalogue;
        _vendeurs = vendeurs;
        _logger = logger;
    }

    public async Task HandleAsync(ProductRejectedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var vendeur = await _vendeurs.GetSellerAsync(e.SellerId, cancellationToken);
        if (vendeur is null)
        {
            _logger.LogWarning(
                "Produit {ProductId} refusé : vendeur {SellerId} introuvable — non prévenu.",
                e.ProductId, e.SellerId);

            return;
        }

        var produit = await _catalogue.GetProductAsync(e.ProductId, cancellationToken);
        var designation = produit is null ? "Votre fiche produit" : $"« {produit.Name} »";

        await _dispatcher.NotifyAsync(
            vendeur.UserId,
            "Fiche produit refusée",
            $"{designation} a été refusée par la modération. Le détail des points à corriger est affiché "
            + "sur la fiche, dans votre espace vendeur : corrigez-les et soumettez-la à nouveau.",
            "Product",
            e.ProductId,
            cancellationToken,
            alsoEmail: true);
    }
}

/// <summary>
/// Prévient le vendeur qu'un avis sur l'un de ses produits a été REFUSÉ par la
/// modération.
/// </summary>
[NomDeConsommateur("HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers.ReviewRejectedNotificationHandler")]
public sealed class ReviewRejectedNotificationHandler : IIntegrationEventHandler<ReviewRejectedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly IProductsModuleApi _catalogue;
    private readonly ISellerModuleApi _vendeurs;
    private readonly ILogger<ReviewRejectedNotificationHandler> _logger;

    public ReviewRejectedNotificationHandler(
        NotificationDispatcher dispatcher,
        IProductsModuleApi catalogue,
        ISellerModuleApi vendeurs,
        ILogger<ReviewRejectedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _catalogue = catalogue;
        _vendeurs = vendeurs;
        _logger = logger;
    }

    public async Task HandleAsync(ReviewRejectedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var vendeur = await _vendeurs.GetSellerAsync(e.SellerId, cancellationToken);
        if (vendeur is null)
        {
            _logger.LogWarning(
                "Avis {ReviewId} refusé : vendeur {SellerId} introuvable — non prévenu.",
                e.ReviewId, e.SellerId);

            return;
        }

        var produit = await _catalogue.GetProductAsync(e.ProductId, cancellationToken);
        var designation = produit is null ? "l'un de vos produits" : $"« {produit.Name} »";

        // PAS D'ENVOI D'E-MAIL POUR CELUI-CI.
        await _dispatcher.NotifyAsync(
            vendeur.UserId,
            "Un avis a été retiré",
            $"Un avis publié sur {designation} a été retiré par la modération. Votre note a été recalculée "
            + "en conséquence.",
            "Review",
            e.ProductId,
            cancellationToken);
    }
}
