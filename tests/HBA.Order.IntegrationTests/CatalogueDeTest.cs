using System.Collections.Concurrent;
using HBA.Products.Contracts;

namespace HBA.Order.IntegrationTests;

/// <summary>LE CATALOGUE EN MÉMOIRE — LA REVALIDATION DU PRIX AU CHECKOUT (ISSUE-048).</summary>
internal sealed class CatalogueDeTest : IProductsModuleApi
{
    private readonly ConcurrentDictionary<Guid, byte> _absentes = new();
    private readonly ConcurrentDictionary<Guid, byte> _nonAchetables = new();
    private readonly ConcurrentDictionary<Guid, decimal> _prixForces = new();

    /// <summary>L'offre a disparu du catalogue → `ordering.offer_unavailable`.</summary>
    public void Retirer(Guid offreId) => _absentes[offreId] = 0;

    /// <summary>
    /// L'offre existe mais n'est plus vendable → `ordering.offer_not_purchasable`.
    /// </summary>
    public void RendreNonAchetable(Guid offreId) => _nonAchetables[offreId] = 0;

    /// <summary>Le prix a bougé depuis l'ajout au panier → `ordering.price_changed`.</summary>
    public void PoserLePrix(Guid offreId, decimal prix) => _prixForces[offreId] = prix;

    /// <summary>
    /// Remet le catalogue à son état nominal : tout achetable, au prix du panier.
    /// </summary>
    public void Reinitialiser()
    {
        _absentes.Clear();
        _nonAchetables.Clear();
        _prixForces.Clear();
    }

    /// <summary>LES OFFRES INCONNUES SONT RENDUES ACHETABLES, PAS ABSENTES.</summary>
    public Task<IReadOnlyDictionary<Guid, OfferSummary>> GetOffersAsync(
        IReadOnlyCollection<Guid> offerIds, CancellationToken cancellationToken = default)
    {
        var resultat = new Dictionary<Guid, OfferSummary>();

        foreach (var id in offerIds)
        {
            if (_absentes.ContainsKey(id))
            {
                continue;
            }

            var prix = _prixForces.TryGetValue(id, out var force) ? force : PanierDeTest.PrixUnitaire;

            // LES CHAMPS NON LUS VALENT `Guid.Empty`, PAS UN GUID AU HASARD.
            resultat[id] = new OfferSummary(
                Id: id,
                ProductId: Guid.Empty,
                VariantId: Guid.Empty,
                StoreId: Guid.Empty,
                SellerId: Guid.Empty,
                Sku: null,
                BuyerPrice: prix,
                PromotionalPrice: null,
                EffectivePrice: prix,
                PromotionEndsOnUtc: null,
                Currency: "XOF",
                Status: "Active",
                IsPurchasable: !_nonAchetables.ContainsKey(id),
                Condition: "New",
                HandlingTimeDays: 1,
                ShipFromLocationId: Guid.Empty);
        }

        return Task.FromResult<IReadOnlyDictionary<Guid, OfferSummary>>(resultat);
    }

    // LES CINQ AUTRES MÉTHODES LÈVENT, ET C'EST DÉLIBÉRÉ.
    private static Task<T> NonAppelee<T>(string methode)
        => throw new NotSupportedException(
            $"order-service n'appelle pas `{methode}` : il ne revalide que les offres de son panier. "
            + "Si un chemin nouveau l'appelle, c'est ce double qu'il faut compléter, pas cette exception "
            + "qu'il faut retirer.");

    public Task<ProductSummary?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default)
        => NonAppelee<ProductSummary?>(nameof(GetProductAsync));

    public Task<OfferSummary?> GetOfferAsync(Guid offerId, CancellationToken cancellationToken = default)
        => NonAppelee<OfferSummary?>(nameof(GetOfferAsync));

    public Task<IReadOnlyList<OfferSummary>> ListPurchasableOffersAsync(
        Guid productId, CancellationToken cancellationToken = default)
        => NonAppelee<IReadOnlyList<OfferSummary>>(nameof(ListPurchasableOffersAsync));

    public Task<IReadOnlyList<OfferSummary>> ListOffersBySkuAsync(
        string sku, CancellationToken cancellationToken = default)
        => NonAppelee<IReadOnlyList<OfferSummary>>(nameof(ListOffersBySkuAsync));
}
