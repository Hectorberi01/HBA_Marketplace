using System.Collections.Concurrent;
using HBA.Commerce.Contracts;

namespace HBA.Order.IntegrationTests;

/// <summary>LE PANIER VALORISÉ EN MÉMOIRE — PILOTABLE, ET SEULEMENT PILOTABLE.</summary>
internal sealed class PanierDeTest : ICartModuleApi
{
    /// <summary>Le prix unitaire de toute ligne déposée par ce double.</summary>
    public const decimal PrixUnitaire = 5000m;

    private readonly ConcurrentDictionary<Guid, CartSummary> _parAcheteur = new();

    /// <summary>
    /// Les identifiants d'offre du panier actif de cet acheteur, dans l'ordre des
    /// SKU.
    /// </summary>
    public IReadOnlyList<Guid> Offres(Guid acheteurId)
        => _parAcheteur.TryGetValue(acheteurId, out var panier)
            ? [.. panier.Lines.Select(l => l.OfferId)]
            : [];

    /// <summary>
    /// Dépose un panier de MARCHANDISE pour cet acheteur et rend son identifiant.
    /// </summary>
    /// <param name="lieuExpedition">
    /// Le MÊME lieu pour toutes les lignes, et c'est indispensable.
    /// </param>
    /// <param name="skus">
    /// Une ligne par SKU. Plusieurs lignes rendent vérifiable « une libération PAR
    /// LIGNE » plutôt que « au moins une libération » — voir
    /// <see cref="InventaireDeTest"/> .
    /// </param>
    public Guid Deposer(Guid acheteurId, Guid lieuExpedition, params string[] skus)
    {
        var cartId = Guid.NewGuid();

        var lignes = skus.Select(sku => new CartLineSummary(
            LineId: Guid.NewGuid(),

            // « Goods » ÉCRIT EN TOUTES LETTRES, ET LA CASSE COMPTE.
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
    /// order-service N'APPELLE PAS CETTE MÉTHODE, ET ELLE LÈVE POUR QUE CELA RESTE
    /// VRAI.
    /// </summary>
    public Task<CartSummary?> GetCartAsync(Guid cartId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "order-service ne lit qu'un panier ACTIF, jamais un panier désigné par son identifiant.");
}
