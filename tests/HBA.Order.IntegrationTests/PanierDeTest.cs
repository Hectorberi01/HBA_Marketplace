using System.Collections.Concurrent;
using HBA.Commerce.Contracts;

namespace HBA.Order.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE PANIER VALORISÉ EN MÉMOIRE — PILOTABLE, ET SEULEMENT PILOTABLE.
///
/// `PlaceOrderCommandHandler` commence par lire le panier de l'acheteur pour en
/// FIGER les prix dans la commande. Sans lui, aucune commande n'existe, et il n'y
/// a rien à confirmer ni à annuler : c'est la condition d'entrée de tout ce que
/// cette suite éprouve.
///
/// POURQUOI UN FAUX PLUTÔT QUE cart-service EN CONTENEUR.
///
/// Faire tourner le voisin ferait de chaque test de cette suite un test de DEUX
/// services. Une migration cassée chez cart-service ferait échouer « un paiement
/// capturé confirme la commande », et l'on chercherait ici une panne qui n'y est
/// pas. Le panier n'est pas ce qu'on éprouve — il est ce dont on part.
///
/// CE QU'IL NE MASQUE PAS : la valorisation elle-même. Les prix qu'il rend
/// sont ceux que le test a posés, et le test n'assertit RIEN dessus. Il n'y a donc
/// pas de règle de calcul qui passerait pour vérifiée alors qu'elle ne l'est pas.
///
/// SINGLETON : le test dépose le panier, la requête HTTP le lit.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class PanierDeTest : ICartModuleApi
{
    /// <summary>
    /// Le prix unitaire de toute ligne déposée par ce double.
    /// </summary>
    /// <remarks>
    /// <see cref="CatalogueDeTest"/> LIT CETTE MÊME CONSTANTE, ET C'EST OBLIGATOIRE.
    ///
    /// Depuis le lot 7.4, `PlaceOrderCommandHandler` redemande au catalogue le prix de
    /// chaque offre et REFUSE le checkout s'il diffère de celui figé au panier. Deux
    /// valeurs recopiées séparément feraient donc refuser CHAQUE commande de cette suite
    /// avec `ordering.price_changed` — un refus parfaitement correct du code de
    /// production, pour un désaccord qui n'existe que dans les tests. Une seule source.
    /// </remarks>
    public const decimal PrixUnitaire = 5000m;

    private readonly ConcurrentDictionary<Guid, CartSummary> _parAcheteur = new();

    /// <summary>
    /// Les identifiants d'offre du panier actif de cet acheteur, dans l'ordre des SKU.
    /// </summary>
    /// <remarks>
    /// Ils sont tirés au hasard à chaque dépôt : sans cette lecture, un test ne peut
    /// pas désigner l'offre qu'il veut voir disparaître, devenir invendable ou changer
    /// de prix dans <see cref="CatalogueDeTest"/>. C'est ce qui rend les trois refus du
    /// lot 7.4 éprouvables au lieu d'être seulement écrits.
    /// </remarks>
    public IReadOnlyList<Guid> Offres(Guid acheteurId)
        => _parAcheteur.TryGetValue(acheteurId, out var panier)
            ? [.. panier.Lines.Select(l => l.OfferId)]
            : [];

    /// <summary>
    /// Dépose un panier de MARCHANDISE pour cet acheteur et rend son identifiant.
    /// </summary>
    /// <param name="lieuExpedition">
    /// Le MÊME lieu pour toutes les lignes, et c'est indispensable.
    ///
    /// DEUX LIEUX METTRAIENT LA COMMANDE EN ARBITRAGE, PAS EN LIVRAISON.
    ///
    /// `CreateDeliveryOnOrderConfirmedHandler` refuse le multi-colis — à juste
    /// titre : une course par lieu ferait clore la commande à la première remise.
    /// Il bascule alors la commande en `UnderReview`, et l'assertion « la commande
    /// est confirmée » tomberait sur un état parfaitement légitime, pour une
    /// raison qui n'a rien à voir avec le paiement.
    /// </param>
    /// <param name="skus">
    /// Une ligne par SKU. Plusieurs lignes rendent vérifiable « une libération PAR
    /// LIGNE » plutôt que « au moins une libération » — voir
    /// <see cref="InventaireDeTest"/>.
    /// </param>
    public Guid Deposer(Guid acheteurId, Guid lieuExpedition, params string[] skus)
    {
        var cartId = Guid.NewGuid();

        var lignes = skus.Select(sku => new CartLineSummary(
            LineId: Guid.NewGuid(),

            // « Goods » ÉCRIT EN TOUTES LETTRES, ET LA CASSE COMPTE.
            //
            // `PlaceOrderCommandHandler` fait un `Enum.TryParse<OrderLineKind>`
            // SANS ignorer la casse, et REFUSE le checkout sur une nature
            // inconnue au lieu de se replier sur « Goods ». « goods » ferait donc
            // échouer la commande avec `ordering.unknown_line_kind`.
            Kind: "Goods",
            OfferId: Guid.NewGuid(),
            ProductId: Guid.NewGuid(),
            CategoryId: Guid.NewGuid(),
            SellerId: Guid.NewGuid(),
            Sku: sku,
            ShipFromLocationId: lieuExpedition,
            Quantity: 2,
            UnitBaseAmount: PrixUnitaire,
            SellerDiscount: 0m,
            PlatformDiscount: 0m,
            FinalUnitPrice: PrixUnitaire,
            LineTotal: PrixUnitaire * 2,
            Currency: "XOF")).ToList();

        _parAcheteur[acheteurId] = new CartSummary(
            CartId: cartId,
            BuyerId: acheteurId,
            Currency: "XOF",
            Status: "Active",
            Kind: "Goods",
            Lines: lignes,
            Subtotal: lignes.Sum(l => l.LineTotal),
            TotalSellerDiscount: 0m,
            TotalPlatformDiscount: 0m,
            GrandTotal: lignes.Sum(l => l.LineTotal));

        return cartId;
    }

    public Task<CartSummary?> GetActiveCartAsync(
        Guid buyerId, CancellationToken cancellationToken = default)
        => Task.FromResult(_parAcheteur.TryGetValue(buyerId, out var panier) ? panier : null);

    /// <summary>
    /// order-service N'APPELLE PAS CETTE MÉTHODE, ET ELLE LÈVE POUR QUE CELA
    /// RESTE VRAI.
    ///
    /// Le checkout part TOUJOURS du panier ACTIF de l'acheteur : lire un panier
    /// par identifiant permettrait de commander celui d'un tiers. Rendre une
    /// valeur neutre ferait passer en silence le jour où quelqu'un branche cette
    /// lecture-là.
    /// </summary>
    public Task<CartSummary?> GetCartAsync(Guid cartId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "order-service ne lit qu'un panier ACTIF, jamais un panier désigné par son identifiant.");
}
