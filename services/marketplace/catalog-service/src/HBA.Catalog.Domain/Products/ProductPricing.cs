using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Products;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// TARIFICATION DE RÉFÉRENCE D'UNE RÉVISION (§8, §21, §23).
///
/// CE N'EST PAS LE PRIX QUE L'ACHETEUR PAIE.
///
/// Décision D12 : le prix transactionnel reste porté par <c>ProductOffer</c>, que
/// cart-service consomme en gRPC et qui seul connaît la commission et les frais
/// fournisseur. Ce qui vit ICI est le prix de RÉFÉRENCE saisi par le vendeur dans
/// son formulaire (§13, étape 4) : la base à partir de laquelle une offre est
/// créée, et la valeur qui, modifiée, exige une nouvelle validation (§6).
///
/// Confondre les deux est le piège de ce fichier. Un affichage public qui lirait
/// <see cref="BasePrice"/> montrerait un prix hors commission — donc INFÉRIEUR à
/// ce qui sera facturé au paiement, et l'écart ne se verrait qu'au panier.
///
/// DES ENTIERS, PAS DES DÉCIMAUX (§21, décision D13).
///
/// Les montants sont des <c>long</c> en francs CFA entiers. Le XOF n'a pas de
/// subdivision : 850000 est un montant exact, et il n'existe pas de « 850000,50 ».
/// Un decimal n'apporterait ici qu'un stockage plus large et la tentation d'une
/// division qui rendrait des centimes que personne ne peut payer.
///
/// Les tables antérieures — product_offers — restent en numeric(18,2) : les
/// convertir demanderait de toucher le VO Money partagé par tout le dépôt et les
/// RPC gRPC qui rendent les montants en chaîne. Deux conventions coexistent donc
/// dans le schéma catalog, et c'est le seul endroit où elles se rencontrent.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ProductPricing : ValueObject
{
    public const string DeviseParDefaut = "XOF";

    private ProductPricing(
        long basePrice,
        long? compareAtPrice,
        long? costPrice,
        string currency,
        bool taxIncluded,
        int taxRate)
    {
        BasePrice = basePrice;
        CompareAtPrice = compareAtPrice;
        CostPrice = costPrice;
        Currency = currency;
        TaxIncluded = taxIncluded;
        TaxRate = taxRate;
    }

    // Requis par EF (matérialisation d'un type possédé).
    private ProductPricing()
    {
        Currency = DeviseParDefaut;
    }

    /// <summary>Prix de référence vendeur, en unités entières de <see cref="Currency"/>.</summary>
    public long BasePrice { get; private set; }

    /// <summary>Prix barré affiché à côté du prix courant. Nul s'il n'y a pas de remise.</summary>
    public long? CompareAtPrice { get; private set; }

    /// <summary>
    /// Coût d'achat du vendeur.
    ///
    /// NE SORT JAMAIS DANS UNE API PUBLIQUE (§17, consigne 14).
    ///
    /// C'est une marge commerciale : exposée, elle dit à un concurrent — et à
    /// l'acheteur — exactement ce que gagne le vendeur. La garde ne peut pas être
    /// posée ici, ce champ n'a aucun moyen de savoir qui le lit ; elle vit dans
    /// les DTO publics, qui ne doivent tout simplement pas porter la propriété.
    /// </summary>
    public long? CostPrice { get; private set; }

    public string Currency { get; private set; } = DeviseParDefaut;

    public bool TaxIncluded { get; private set; }

    /// <summary>Taux de TVA en POURCENTS entiers (18 = 18 %), pas en fraction.</summary>
    public int TaxRate { get; private set; }

    public static Result<ProductPricing> Create(
        long basePrice,
        long? compareAtPrice = null,
        long? costPrice = null,
        string? currency = null,
        bool taxIncluded = true,
        int taxRate = 0)
    {
        // §23 : « basePrice > 0 ». Zéro n'est pas un produit gratuit, c'est un
        // formulaire à moitié rempli — et une commande à 0 F traverserait paiement
        // et livraison sans que rien ne s'en étonne.
        if (basePrice <= 0)
        {
            return Error.Validation(
                "catalog.pricing.base_price_invalid",
                "Le prix de base doit être strictement positif.");
        }

        // UN PRIX BARRÉ INFÉRIEUR AU PRIX COURANT EST UNE FAUSSE PROMOTION.
        //
        // Affiché tel quel, il montre « 800 000 F » barré au-dessus de
        // « 850 000 F » : l'acheteur lit une remise négative. C'est un défaut de
        // saisie fréquent (les deux champs se ressemblent), et il ne se voit qu'à
        // l'écran, jamais dans les journaux.
        if (compareAtPrice.HasValue && compareAtPrice.Value <= basePrice)
        {
            return Error.Validation(
                "catalog.pricing.compare_at_not_higher",
                "Le prix barré doit être supérieur au prix de base, sinon la remise affichée serait négative.");
        }

        if (costPrice.HasValue && costPrice.Value < 0)
        {
            return Error.Validation(
                "catalog.pricing.cost_price_negative",
                "Le coût d'achat ne peut pas être négatif.");
        }

        if (taxRate is < 0 or > 100)
        {
            return Error.Validation(
                "catalog.pricing.tax_rate_invalid",
                "Le taux de TVA s'exprime en pourcents entiers, entre 0 et 100.");
        }

        var devise = string.IsNullOrWhiteSpace(currency)
            ? DeviseParDefaut
            : currency.Trim().ToUpperInvariant();

        if (devise.Length != 3)
        {
            return Error.Validation(
                "catalog.pricing.currency_invalid",
                "La devise doit être un code ISO 4217 de trois lettres.");
        }

        return new ProductPricing(basePrice, compareAtPrice, costPrice, devise, taxIncluded, taxRate);
    }

    /// <summary>
    /// Vrai si le passage de cette tarification à l'autre est une modification
    /// CRITIQUE au sens du §6 — donc si elle exige une nouvelle validation.
    ///
    /// LE COÛT D'ACHAT N'EN FAIT PAS PARTIE.
    ///
    /// Il n'est jamais montré à l'acheteur : le corriger ne change rien de ce que
    /// l'administrateur avait validé. L'y inclure enverrait en file d'attente des
    /// fiches déjà en vente pour une correction de comptabilité interne.
    /// </summary>
    public bool DiffereCritiquementDe(ProductPricing autre)
        => autre is null
           || BasePrice != autre.BasePrice
           || CompareAtPrice != autre.CompareAtPrice
           || Currency != autre.Currency
           || TaxIncluded != autre.TaxIncluded
           || TaxRate != autre.TaxRate;

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return BasePrice;
        yield return CompareAtPrice;
        yield return CostPrice;
        yield return Currency;
        yield return TaxIncluded;
        yield return TaxRate;
    }
}
