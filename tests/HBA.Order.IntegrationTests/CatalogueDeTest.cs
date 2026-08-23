using System.Collections.Concurrent;
using HBA.Products.Contracts;

namespace HBA.Order.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CATALOGUE EN MÉMOIRE — LA REVALIDATION DU PRIX AU CHECKOUT (ISSUE-048).
///
/// Depuis le lot 7.4, `PlaceOrderCommandHandler` NE FAIT PLUS CONFIANCE AU PANIER
/// pour le prix : avant de construire la commande, il redemande au catalogue
/// chaque offre de marchandise et refuse si elle a disparu, n'est plus achetable,
/// ou n'a plus le même prix. C'est ce qui empêche un panier vieux d'une semaine de
/// faire payer un prix qui n'existe plus.
///
/// CE DOUBLE A ÉTÉ AJOUTÉ APRÈS COUP, ET TROIS TESTS SONT TOMBÉS D'ABORD.
///
/// `AddProductsGrpcClient` a été branché dans `HBA.Order.Api/Program.cs` sans que
/// cette fabrique reçoive `Services__Catalog` : l'hôte refusait de se construire,
/// avec « Services:Catalog est absent ». C'est la CINQUIÈME fois qu'une liste
/// d'adresses tenue à la main dans une fabrique de test prend un lot de retard sur
/// le `Program.cs` qu'elle démarre. `check-service-addresses.py` couvre désormais
/// les cinq fabriques du dépôt, pas seulement `AuthorizationTestFactory`.
///
/// ET L'ADRESSE SEULE N'AURAIT PAS SUFFI. Le catalogue n'est plus seulement
/// exigé au démarrage, il est APPELÉ à chaque commande. Un port fermé aurait
/// transformé trois erreurs de construction en une erreur par test qui commande —
/// plus lente, plus bruyante, et pointant vers le réseau au lieu du manque.
///
/// LE PRIX PAR DÉFAUT EST CELUI DU PANIER, ET C'EST TOUT L'ENJEU.
/// Les deux doubles lisent <see cref="PanierDeTest.PrixUnitaire"/>. Les laisser
/// diverger ferait refuser CHAQUE checkout de la suite avec
/// `ordering.price_changed` — un échec parfaitement légitime du code de
/// production, pour une raison qui n'existe que dans les tests.
///
/// SINGLETON : le test pose ses exceptions, la requête HTTP les lit.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class CatalogueDeTest : IProductsModuleApi
{
    private readonly ConcurrentDictionary<Guid, byte> _absentes = new();
    private readonly ConcurrentDictionary<Guid, byte> _nonAchetables = new();
    private readonly ConcurrentDictionary<Guid, decimal> _prixForces = new();

    /// <summary>L'offre a disparu du catalogue → `ordering.offer_unavailable`.</summary>
    public void Retirer(Guid offreId) => _absentes[offreId] = 0;

    /// <summary>L'offre existe mais n'est plus vendable → `ordering.offer_not_purchasable`.</summary>
    public void RendreNonAchetable(Guid offreId) => _nonAchetables[offreId] = 0;

    /// <summary>Le prix a bougé depuis l'ajout au panier → `ordering.price_changed`.</summary>
    public void PoserLePrix(Guid offreId, decimal prix) => _prixForces[offreId] = prix;

    /// <summary>Remet le catalogue à son état nominal : tout achetable, au prix du panier.</summary>
    public void Reinitialiser()
    {
        _absentes.Clear();
        _nonAchetables.Clear();
        _prixForces.Clear();
    }

    /// <summary>
    /// LES OFFRES INCONNUES SONT RENDUES ACHETABLES, PAS ABSENTES.
    ///
    /// `PanierDeTest` tire ses identifiants d'offre au hasard à chaque dépôt : le
    /// double ne peut pas les connaître d'avance. Répondre « inconnue » ferait
    /// refuser tout checkout de la suite avec `ordering.offer_unavailable`.
    ///
    /// Ce que ce choix NE masque PAS : les trois refus restent éprouvables, en les
    /// demandant explicitement sur un identifiant que le test a lu dans
    /// <see cref="PanierDeTest.Offres"/>. Ce qu'il masque, et il faut le savoir :
    /// une offre que le code de production irait chercher SANS passer par le
    /// panier trouverait ici une réponse complaisante. Aucun chemin d'order-service
    /// ne fait cela aujourd'hui — `GetOffersAsync` n'est appelé que sur les lignes
    /// du panier.
    /// </summary>
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
            //
            // `PlaceOrderCommandHandler` ne lit de cette offre que trois choses :
            // sa PRÉSENCE, `IsPurchasable` et `EffectivePrice`. Poser des
            // identifiants aléatoires sur les autres suggérerait qu'ils veulent
            // dire quelque chose — et le jour où un chemin les lirait, il
            // travaillerait sur du bruit sans que rien ne le signale. `Guid.Empty`
            // dit ce qui est vrai : ce double ne modélise pas ces champs.
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

    // ═════════════════════════════════════════════════════════════════════════
    // LES CINQ AUTRES MÉTHODES LÈVENT, ET C'EST DÉLIBÉRÉ.
    //
    // order-service n'appelle QUE `GetOffersAsync`. Rendre une valeur neutre
    // ferait passer en silence le jour où quelqu'un branche une autre lecture —
    // et le test continuerait de « réussir » sur une réponse inventée. Même
    // raisonnement que `PanierDeTest.GetCartAsync`.
    // ═════════════════════════════════════════════════════════════════════════
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
