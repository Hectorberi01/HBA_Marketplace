using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Context;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.IntegrationEvents;
using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Domain.Products;
using HBA.Merchants.Contracts.IntegrationEvents;

// ═════════════════════════════════════════════════════════════════════════════
// CE FICHIER A DÉMÉNAGÉ DE `Application` VERS `Infrastructure/Integration`.
//
// Il n'a pas changé de rôle ; il a acquis une dépendance qui ne pouvait pas vivre
// où il était. La garde d'idempotence du §19.5 passe par `IConsumerInbox`, qui
// vit dans `HBA.Shared.Infrastructure` — et `HBA.Catalog.Application` ne
// référence pas l'infrastructure, délibérément : c'est le sens de la flèche de
// dépendance de l'architecture en couches.
//
// Deux façons de s'en sortir : faire remonter le contrat dans la couche
// Application du socle, ou déplacer le consommateur. On déplace, pour deux
// raisons. La première est que le contrat décrit un mécanisme de PERSISTANCE —
// une table, une transaction — et non une règle métier. La seconde est que le
// dépôt a déjà tranché ailleurs : `CreateUserProfileOnUserRegisteredHandler` vit
// dans `HBA.Users.Api/Integration` avec l'encadré qui l'explique, « la
// composition root a le droit de tout connaître ».
//
// Ce que ce déplacement change pour le lecteur : les gestionnaires d'événements
// d'INTÉGRATION (venus d'un autre service) ne sont plus mélangés aux
// gestionnaires d'événements de DOMAINE (nés dans cet agrégat), qui restent dans
// `Application/Products/EventHandlers`. Les deux portaient le même suffixe et
// n'ont ni la même portée ni les mêmes droits.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Catalog.Infrastructure.Integration;

/// <summary>
/// Fermeture d'un compte vendeur (suppression partielle) : on RETIRE ses produits de
/// la vente. On les dépublie (Active -> Draft) plutôt que de les archiver : le retrait
/// doit être RÉVERSIBLE, puisque le vendeur peut demander une réactivation et
/// republier ses fiches d'un geste.
/// </summary>
// ═════════════════════════════════════════════════════════════════════════════
// CES HANDLERS NE SONT PLUS LES SEULS, ET NE SONT PLUS LES PRINCIPAUX.
//
// Depuis la bascule vers le module Products, la vitrine, les BFF et le panier
// lisent `products`. Ce qui se joue ici ne concerne plus que les lignes
// `catalog.products` — encore lues par onze appels à `GetProductAsync` (fiche
// admin, panier mobile, lien profond, avis vendeur), et par eux seuls.
//
// Le retrait réel de la vente se fait désormais dans
// `Marketplace.Api.Integration.SellerLifecycleProductHandlers`. Les deux
// coexistent sans se contredire : ils écrivent dans des tables différentes.
//
// À SUPPRIMER AVEC catalog.products, PAS AVANT. Les retirer maintenant
// laisserait ces onze lectures rendre des fiches d'un vendeur écarté.
// ═════════════════════════════════════════════════════════════════════════════
public sealed class SellerClosedProductInvalidationHandler : IIntegrationEventHandler<SellerClosedIntegrationEvent>
{
    /// <summary>Nom de ce consumer dans `consumer_inbox` (§19.5). Stable : il est en base.</summary>
    private const string ConsumerName = "catalog-service.merchants-seller-closed";

    private readonly IProductRepository _products;
    private readonly IConsumerInbox _inbox;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILogger<SellerClosedProductInvalidationHandler> _logger;

    public SellerClosedProductInvalidationHandler(
        IProductRepository products,
        IConsumerInbox inbox,
        ICatalogUnitOfWork unitOfWork,
        ILogger<SellerClosedProductInvalidationHandler> logger)
    {
        _products = products;
        _inbox = inbox;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(SellerClosedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // ═════════════════════════════════════════════════════════════════════
        // GARDE D'IDEMPOTENCE DU §19.5 — ET CE QU'ELLE APPORTE VRAIMENT ICI.
        //
        // Ce gestionnaire est DÉJÀ inoffensif au rejeu : la garde
        // `Status == Published` fait que la seconde passe ne dépublie rien. Ce que
        // l'inbox change est plus discret et plus utile : sans elle, chaque rejeu
        // recharge TOUS les produits du vendeur pour n'en modifier aucun, et
        // surtout le journal réannonce « 0 produit dépublié » sur un événement déjà
        // traité — on lit alors la trace d'une fermeture qui n'a rien fait, et l'on
        // se demande pourquoi.
        //
        // Le vrai enjeu est le suivant : ce fichier est le gabarit du prochain
        // consommateur du catalogue. Un gestionnaire qui décrémenterait un compteur
        // ou publierait un événement sortant n'a AUCUNE idempotence naturelle, et
        // sans cette garde un simple rééquilibrage de partitions Kafka le
        // rejouerait.
        // ═════════════════════════════════════════════════════════════════════
        if (await _inbox.HasProcessedAsync(e.Id, ConsumerName, cancellationToken))
        {
            _logger.LogDebug(
                "Événement {EventId} déjà traité par {Consumer} : ignoré.", e.Id, ConsumerName);

            return;
        }

        var products = await _products.ListBySellerForUpdateAsync(e.SellerId, cancellationToken);

        var unpublished = 0;
        foreach (var product in products)
        {
            // « Active » S'APPELLE MAINTENANT « Published », ET LA GARDE COMPTE.
            //
            // Sans ce test, `Unpublish()` serait appelé sur des fiches en brouillon
            // ou déjà suspendues : la liste blanche des transitions le refuserait,
            // le Result serait ignoré ici, et le compteur annoncerait des
            // dépublications qui n'ont pas eu lieu. Le journal dirait « 40 produits
            // dépubliés » là où il n'y en a eu que trois.
            if (product.Status == ProductStatus.Published)
            {
                product.Unpublish();
                unpublished++;
            }
        }

        // LA TRACE EST ÉCRITE DANS LA MÊME UNITÉ DE TRAVAIL QUE L'EFFET.
        //
        // C'est tout l'intérêt du dispositif, et c'est pour cela que
        // `MarkProcessedAsync` n'appelle pas `SaveChanges` lui-même. Committer la
        // trace séparément rouvrirait exactement la fenêtre que l'inbox ferme : une
        // panne entre les deux laisserait l'événement marqué traité alors que rien
        // ne l'a été.
        //
        // ET LE `SaveChanges` N'EST PLUS CONDITIONNEL.
        //
        // Il ne s'exécutait que `if (unpublished > 0)`. Garder cette condition
        // perdrait la trace précisément quand il n'y a rien à dépublier — donc à
        // chaque rejeu, donc pour toujours : l'événement ne serait jamais marqué
        // traité et reviendrait indéfiniment.
        await _inbox.MarkProcessedAsync(
            e.Id, ConsumerName, "merchants.seller.closed",
            HbaRequestContext.Current.CorrelationId, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Vendeur {SellerId} fermé : {Count} produit(s) dépublié(s).", e.SellerId, unpublished);
    }
}

/// <summary>
/// Suppression DÉFINITIVE d'un vendeur (admin) : on ARCHIVE tous ses produits
/// (transition terminale) pour les retirer irrévocablement de la vente. On ne les
/// supprime pas physiquement : ils restent référencés par l'historique de commandes.
/// </summary>
public sealed class SellerDeletedProductPurgeHandler : IIntegrationEventHandler<SellerDeletedIntegrationEvent>
{
    private const string ConsumerName = "catalog-service.merchants-seller-deleted";

    private readonly IProductRepository _products;
    private readonly IConsumerInbox _inbox;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILogger<SellerDeletedProductPurgeHandler> _logger;

    public SellerDeletedProductPurgeHandler(
        IProductRepository products,
        IConsumerInbox inbox,
        ICatalogUnitOfWork unitOfWork,
        ILogger<SellerDeletedProductPurgeHandler> logger)
    {
        _products = products;
        _inbox = inbox;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(SellerDeletedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // DEUX NOMS DE CONSUMER DISTINCTS, PAS UN SEUL POUR LE FICHIER.
        //
        // La clé de l'inbox est le couple (événement, consumer). Partager un nom
        // entre ces deux gestionnaires ne poserait pas de problème tant que
        // fermeture et suppression portent des identifiants d'événement différents
        // — mais le jour où un même événement doit être traité par deux
        // gestionnaires, le second se croirait déjà passé et ne s'exécuterait
        // jamais. Silencieusement.
        if (await _inbox.HasProcessedAsync(e.Id, ConsumerName, cancellationToken))
        {
            _logger.LogDebug(
                "Événement {EventId} déjà traité par {Consumer} : ignoré.", e.Id, ConsumerName);

            return;
        }

        var products = await _products.ListBySellerForUpdateAsync(e.SellerId, cancellationToken);

        var archived = 0;
        foreach (var product in products)
        {
            if (product.Status != ProductStatus.Archived)
            {
                product.Archive();
                archived++;
            }
        }

        await _inbox.MarkProcessedAsync(
            e.Id, ConsumerName, "merchants.seller.deleted",
            HbaRequestContext.Current.CorrelationId, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Vendeur {SellerId} supprimé : {Count} produit(s) archivé(s).", e.SellerId, archived);
    }
}
