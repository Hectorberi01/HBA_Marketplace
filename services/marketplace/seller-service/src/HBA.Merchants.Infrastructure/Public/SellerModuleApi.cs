using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Merchants.Application;
using HBA.Merchants.Contracts;
using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Domain.Stores;
using HBA.Merchants.Infrastructure.Persistence;

namespace HBA.Merchants.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Sellers. Lecture seule.
/// Permet par exemple à Catalog de vérifier qu'un vendeur est actif.
///
/// Toutes les lectures sont en cache-aside : cette API est appelée EN BOUCLE par la
/// fiche produit mobile (un appel par vendeur du produit). Voir SellersCacheKeys.
/// </summary>
internal sealed class SellerModuleApi : ISellerModuleApi
{
    private readonly SellersDbContext _dbContext;
    private readonly ICacheService _cache;
    private readonly IPlatformPricing _pricing;

    public SellerModuleApi(SellersDbContext dbContext, ICacheService cache, IPlatformPricing pricing)
    {
        _dbContext = dbContext;
        _cache = cache;
        _pricing = pricing;
    }

    /// <summary>
    /// PAS DE CACHE ICI, CONTRAIREMENT AUX LECTURES DE VENDEUR.
    ///
    /// Cette lecture sert à AUTORISER : Products s'en servira pour refuser une
    /// offre posée sur la boutique d'autrui, ou sur une boutique fermée. Servir
    /// une réponse périmée de dix minutes reviendrait à accepter des offres sur
    /// une boutique qui vient d'être suspendue.
    ///
    /// Les lectures de vendeur, elles, alimentent des écrans : un nom de boutique
    /// vieux de dix minutes n'a jamais fait de mal.
    /// </summary>
    public async Task<StoreSummary?> GetStoreAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var id = new StoreId(storeId);
        var store = await _dbContext.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        return store is null ? null : MapStore(store);
    }

    public async Task<IReadOnlyList<StoreSummary>> ListStoresBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
    {
        var stores = await _dbContext.Stores
            .AsNoTracking()
            .Where(s => s.SellerId == sellerId)
            .OrderBy(s => s.CreatedOnUtc)
            .ToListAsync(cancellationToken);

        return stores.Select(MapStore).ToList();
    }

    private static StoreSummary MapStore(Store store)
        => new(
            store.Id.Value,
            store.SellerId,
            store.Name,
            store.LogoUrl,
            store.Description,
            store.Contact.Phone,
            store.Contact.Email,
            store.Status.ToString(),
            store.IsSelling,
            store.FulfillmentLocationId,
            store.StatusReason,
            store.OpeningHours
                // Lundi en tête : DayOfWeek vaut Sunday = 0. Voir StoreMapper,
                // qui applique la même règle côté écrans.
                .OrderBy(h => ((int)h.Day + 6) % 7)
                .ThenBy(h => h.OpensAt)
                .Select(h => new StoreOpeningHourSummary(
                    h.Day.ToString(),
                    h.OpensAt.ToString("HH\\:mm", System.Globalization.CultureInfo.InvariantCulture),
                    h.ClosesAt.ToString("HH\\:mm", System.Globalization.CultureInfo.InvariantCulture)))
                .ToList(),
            store.CreatedOnUtc);

    public Task<SellerSummary?> GetSellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            SellersCacheKeys.Seller(sellerId),
            async ct =>
            {
                var id = new SellerId(sellerId);

                // PLUS DE `.Include(KybDocuments)`, ET C'EST UN GAIN, PAS UN OUBLI.
                //
                // Ce résumé ne porte plus les pièces : elles vivent sur
                // `SellerDetail`, côté HTTP. Cette lecture-ci est appelée EN BOUCLE
                // par la fiche produit mobile — elle chargeait donc les références
                // des pièces d'identité de chaque vendeur, à chaque affichage, pour
                // les jeter aussitôt.
                var seller = await _dbContext.Sellers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == id, ct);

                return seller is null ? null : Map(seller);
            },
            SellersCacheKeys.SellerTtl,
            SellersCacheKeys.MissTtl,
            cancellationToken);

    public Task<SellerSummary?> GetSellerByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            SellersCacheKeys.SellerByUser(userId),
            async ct =>
            {
                var seller = await _dbContext.Sellers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.UserId == userId, ct);

                return seller is null ? null : Map(seller);
            },
            SellersCacheKeys.SellerTtl,
            SellersCacheKeys.MissTtl,
            cancellationToken);

    /// <summary>
    /// Vendeur actif ?
    ///
    /// Cette méthode faisait sa PROPRE requête (un AnyAsync). Elle se déduit
    /// désormais du résumé déjà en cache : dans le cas courant, elle ne coûte plus
    /// rien du tout — pas même un aller-retour Redis supplémentaire, puisque la
    /// fiche produit vient justement de charger ce même vendeur.
    ///
    /// Le statut est comparé sous forme de chaîne parce que c'est ainsi que
    /// SellerSummary le porte (Status.ToString()). Le nom de l'énumération est la
    /// source de vérité des deux côtés — pas une constante recopiée.
    /// </summary>
    public async Task<bool> IsActiveSellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        var seller = await GetSellerAsync(sellerId, cancellationToken);
        return seller is not null
            && string.Equals(seller.Status, nameof(SellerStatus.Active), StringComparison.Ordinal);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE COMPTE DE REVERSEMENT — LU DIRECTEMENT, SANS CACHE ET SANS LES PIÈCES.
    ///
    /// PAS DE CACHE, ET C'EST LA MOITIÉ DE LA RAISON D'ÊTRE DE CETTE MÉTHODE.
    ///
    /// `GetSellerAsync` sert des écrans et met sa réponse en cache dix minutes ;
    /// un nom de boutique périmé n'a jamais fait de mal. Un NUMÉRO MOBILE MONEY
    /// périmé, si : c'est l'argent envoyé à l'ancien numéro d'un vendeur qui vient
    /// de corriger une faute de frappe. Un retrait lit la valeur du moment.
    ///
    /// ET PAS DE `.Include(KybDocuments)` : un retrait n'a aucune raison de
    /// faire remonter les références des pièces d'identité du vendeur.
    ///
    /// ON CHARGE LA LIGNE, ON NE PROJETTE PAS LE SEUL CHAMP. `PayoutAccount`
    /// passe par un convertisseur jsonb ; le projeter seul rendrait `null` aussi
    /// bien pour un vendeur absent que pour un vendeur sans compte, et il faudrait
    /// une seconde requête pour départager. Une ligne sans ses pièces coûte moins
    /// que deux allers-retours, et distingue les deux cas d'elle-même.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<SellerPayout> GetSellerPayoutAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
    {
        var id = new SellerId(sellerId);

        var seller = await _dbContext.Sellers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (seller is null)
        {
            return SellerPayout.Unknown;
        }

        return seller.PayoutAccount is { } compte
            ? SellerPayout.Of(new PayoutAccountSummary(
                compte.Provider.ToString(), compte.AccountNumber, compte.AccountName))
            : SellerPayout.NotConfigured;
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES HUIT CHAMPS QUI VOYAGENT, ET RIEN D'AUTRE.
    ///
    /// CE MAPPEUR EN REMPLISSAIT TREIZE — ET IL EN OUBLIAIT UN.
    ///
    /// Positionnel, il s'arrêtait à `Metadata` et laissait `KybRejectionReason`
    /// tomber sur son défaut. Le même vendeur lu par `ISellerModuleApi` n'avait
    /// donc pas de motif de refus, là où le même vendeur lu par `SellerMapper` en
    /// avait un. Deux mappeurs pour un type, et une divergence que rien ne
    /// signalait.
    ///
    /// Elle disparaît par construction : `SellerSummary` ne porte plus que ce que
    /// le proto transporte, et il n'y a plus de champ à oublier. La fiche riche
    /// vit dans `SellerDetail`, côté Application, hors de portée d'ici.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private SellerSummary Map(Seller seller) => new(
        seller.Id.Value,
        seller.UserId,
        seller.ShopName,
        seller.LogoUrl,
        seller.Description,
        seller.Status.ToString(),
        seller.KybStatus.ToString(),
        _pricing.CommissionRate);   // le taux APPLIQUÉ, pas la colonne morte

}
