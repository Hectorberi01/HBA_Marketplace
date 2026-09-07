using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Products;

/// <summary>TARIFICATION DE RÉFÉRENCE D'UNE RÉVISION (§8, §21, §23).</summary>
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

    /// <summary>Prix barré affiché à côté du prix courant.</summary>
    public long? CompareAtPrice { get; private set; }

    /// <summary>Coût d'achat du vendeur.</summary>
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
