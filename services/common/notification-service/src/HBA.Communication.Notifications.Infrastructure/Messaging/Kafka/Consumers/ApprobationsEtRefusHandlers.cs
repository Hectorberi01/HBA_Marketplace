using HBA.Catalog.Contracts.IntegrationEvents;
using HBA.Communication.Notifications.Application.Notifications;
using HBA.Engagement.Reviews.Contracts.IntegrationEvents;
using HBA.Merchants.Contracts;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Products.Contracts;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;

/// ═════════════════════════════════════════════════════════════════════════════
/// LES DECISIONS FAVORABLES NE PREVENAIENT PERSONNE.
///
/// L'audit des evenements sans consommateur a fait apparaître un motif repete
/// quatre fois : la decision NEGATIVE est notifiee, la POSITIVE ne l'est pas.
///
///   `SellerKybRejected` -> notifie     |  `SellerKybApproved` -> personne
///   `ReviewPublished`   -> notifie     |  `ReviewRejected`    -> personne
///   `ProductPublished`  -> personne    |  `ProductApproved`   -> personne
///
/// Ce n'est pas un choix de conception : c'est la trace de la façon dont le code
/// a grandi. On branche ce qui fait du bruit — le refus, l'annulation — et le
/// symetrique reste sur le quai. Le vendeur voyait donc son dossier refuse et
/// apprenait qu'il etait accepte en rafraîchissant son ecran, ou pas du tout.
///
/// L'APPROBATION KYB N'EST PAS L'ACTIVATION. `Seller` leve
/// `SellerKybVerifiedDomainEvent` et `SellerActivatedDomainEvent` depuis deux
/// methodes distinctes : un dossier valide n'ouvre pas la boutique. Le message
/// ci-dessous le dit, pour ne pas promettre une mise en vente qui n'a pas eu lieu.
///
/// CES QUATRE GESTIONNAIRES APPELLENT seller-service EN SYNCHRONE.
///
/// Trois des quatre evenements portent `SellerId` et non `UserId` — la
/// notification, elle, s'adresse a un COMPTE. Il faut donc traduire, et
/// `ISellerModuleApi` est le seul chemin. C'est la meme dependance que
/// `ReviewPublishedNotificationHandler` assume deja depuis l'extraction ; on la
/// reprend plutôt que d'inventer un troisieme motif dans le meme fichier.
///
/// CE QUE ÇA COUTE, ET IL FAUT LE SAVOIR. Si seller-service est indisponible, le
/// gestionnaire leve, le message est rejoue puis mis en lettre morte : la
/// notification est PERDUE, silencieusement. Le fermer demande que les
/// producteurs portent `UserId` dans l'evenement — ce que `SellerKybApproved`
/// fait deja, et ce que catalog et review ne peuvent pas faire aujourd'hui,
/// faute de connaître le compte derriere le vendeur.
/// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Previent le vendeur que son dossier KYB est ACCEPTE.
///
/// AUCUN APPEL RESEAU ICI : l'evenement porte deja `UserId`. C'est la forme que
/// les trois autres devraient avoir.
/// </summary>
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
            //
            // La vérification et l'activation sont deux décisions distinctes dans
            // l'agrégat `Seller`. Écrire « votre boutique est ouverte » ici ferait
            // attendre au vendeur des commandes qui ne peuvent pas arriver.
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

        // LE NOM DU PRODUIT EST UN CONFORT, PAS UNE CONDITION.
        // Une fiche introuvable ne doit pas priver le vendeur de la nouvelle.
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

/// <summary>
/// Prévient le vendeur que sa fiche produit est REFUSÉE.
///
/// LE MOTIF N'EST PAS DANS L'ÉVÉNEMENT, ET C'EST DÉLIBÉRÉ COTÉ CONTRAT.
///
/// `ProductRejectedIntegrationEvent` ne porte pas les motifs : un refus en compte
/// plusieurs, chacun visant un champ, et ils vivent dans `ProductReview`. Les
/// recopier dans l'événement ferait deux vérités à tenir d'accord — c'est écrit
/// dans le contrat lui-même.
///
/// Le message renvoie donc le vendeur là où les motifs sont exacts, plutôt que
/// d'en inventer un résumé. Un refus sans indication de l'endroit où regarder
/// serait la même impasse que le KYB muet.
/// </summary>
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
///
/// POURQUOI PRÉVENIR D'UN REFUS D'AVIS. Le vendeur voit sa note évoluer sans
/// comprendre pourquoi un avis visible hier a disparu. Le dire évite le soupçon
/// — et évite le ticket de support qui va avec.
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
        //
        // C'est une information de contexte, pas une action a mener : la
        // notification dans l'espace vendeur suffit. Un e-mail par avis modere
        // ferait du bruit, et le bruit fait desactiver les alertes qui comptent.
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
