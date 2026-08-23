using HBA.Catalog.Application.Products;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.UnitTests;

/// <summary>Prix de référence (§8, §21, §23) et variantes / SKU (§11, §28).</summary>
public sealed class ProductPricingAndVariantTests
{
    // ═════════════════════════════════════════════════════════════════════════
    // PRIX
    // ═════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_prix_de_base_non_positif_est_refuse(long montant)
    {
        var resultat = ProductPricing.Create(montant);

        // Zéro n'est pas un produit gratuit, c'est un formulaire à moitié rempli —
        // et une commande à 0 F traverserait paiement et livraison sans que rien
        // ne s'en étonne.
        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.pricing.base_price_invalid");
    }

    /// <summary>
    /// UN PRIX BARRÉ INFÉRIEUR AU PRIX COURANT AFFICHE UNE REMISE NÉGATIVE.
    ///
    /// Défaut de saisie fréquent — les deux champs se ressemblent — et invisible
    /// partout sauf à l'écran de l'acheteur.
    /// </summary>
    [Theory]
    [InlineData(850_000, 800_000)]
    [InlineData(850_000, 850_000)]
    public void Un_prix_barre_non_superieur_est_refuse(long basePrice, long compareAt)
    {
        var resultat = ProductPricing.Create(basePrice, compareAt);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.pricing.compare_at_not_higher");
    }

    [Fact]
    public void Le_prix_du_cahier_est_accepte()
    {
        // L'exemple du §8, à l'unité près.
        var resultat = ProductPricing.Create(850_000, 900_000, 760_000, "XOF", taxIncluded: true, taxRate: 18);

        resultat.IsSuccess.Should().BeTrue();
        resultat.Value.BasePrice.Should().Be(850_000);
        resultat.Value.Currency.Should().Be("XOF");
        resultat.Value.TaxRate.Should().Be(18);
    }

    [Fact]
    public void La_devise_par_defaut_est_le_franc_CFA()
        => ProductPricing.Create(1_000).Value.Currency.Should().Be("XOF");

    [Fact]
    public void Un_taux_de_TVA_hors_bornes_est_refuse()
        => ProductPricing.Create(1_000, taxRate: 180).Error.Code
            .Should().Be("catalog.pricing.tax_rate_invalid");

    /// <summary>
    /// LE COÛT D'ACHAT N'EST PAS UNE MODIFICATION CRITIQUE.
    ///
    /// Il n'est jamais montré à l'acheteur : le corriger ne change rien de ce que
    /// l'administrateur avait validé. L'y inclure enverrait en file d'attente des
    /// fiches en vente pour une correction de comptabilité interne.
    /// </summary>
    [Fact]
    public void Changer_le_cout_dachat_ne_declenche_pas_de_nouvelle_validation()
    {
        var produit = UnProduit.Publie();
        var avecCout = ProductPricing.Create(850_000, costPrice: 700_000).Value;

        produit.UpdateContenu(UnProduit.Contenu(pricing: avecCout)).IsSuccess.Should().BeTrue();

        produit.Revisions.Should().HaveCount(1);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // VARIANTES ET SKU
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Deux_variantes_ne_peuvent_pas_partager_un_SKU()
    {
        var produit = UnProduit.Brouillon();
        produit.AddVariant("IP16-N256", null, null, 200).IsSuccess.Should().BeTrue();

        var doublon = produit.AddVariant("IP16-N256", null, null, 210);

        // Le SKU est la clé partagée avec Inventory : un doublon rattache le stock
        // à la mauvaise variante.
        doublon.IsFailure.Should().BeTrue();
        doublon.Error.Code.Should().Be("catalog.variant.sku_duplicate");
    }

    [Fact]
    public void Le_SKU_est_normalise_en_majuscules()
    {
        var produit = UnProduit.Brouillon();

        var variante = produit.AddVariant("ip16-n256", null, null, 200);

        variante.IsSuccess.Should().BeTrue();
        variante.Value.Sku.Value.Should().Be("IP16-N256");
    }

    [Fact]
    public void Un_poids_negatif_est_refuse()
        => UnProduit.Brouillon().AddVariant("IP16-N256", null, null, -1)
            .Error.Code.Should().Be("catalog.variant.weight_negative");

    // ═════════════════════════════════════════════════════════════════════════
    // IMAGES — « exactement une image principale » (§12, §23)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Zéro image principale laisse la vitrine choisir au hasard ; deux font
    /// diverger la vignette du panier et celle de la fiche. Aucun des deux cas ne
    /// lève d'erreur nulle part — d'où la garde à la soumission.
    /// </summary>
    [Fact]
    public void La_premiere_image_devient_principale_doffice()
    {
        var produit = UnProduit.Brouillon();

        produit.AddMedia(Guid.NewGuid(), "https://cdn.hba.test/a.webp", ProductMediaType.Image, "a", isPrimary: false);

        produit.Media.Count(m => m.IsPrimary).Should().Be(1);
    }

    [Fact]
    public void Designer_une_nouvelle_principale_retire_lancienne()
    {
        var produit = UnProduit.Brouillon();
        produit.AddMedia(Guid.NewGuid(), "https://cdn.hba.test/a.webp", ProductMediaType.Image, "a", isPrimary: true);
        var seconde = produit.AddMedia(Guid.NewGuid(), "https://cdn.hba.test/b.webp", ProductMediaType.Image, "b", isPrimary: false).Value;

        produit.SetPrimaryMedia(seconde.Id).IsSuccess.Should().BeTrue();

        produit.Media.Count(m => m.IsPrimary).Should().Be(1);
        produit.Media.Single(m => m.IsPrimary).Id.Should().Be(seconde.Id);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TRADUCTION DES VALEURS DU CAHIER
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LE CAHIER ÉCRIT « VERY_GOOD », LE C# ÉCRIT « VeryGood ».
    ///
    /// Sans le retrait des soulignés, l'API refuserait exactement les valeurs que
    /// sa propre documentation donne en exemple (§9, §11) — et le message d'erreur
    /// désignerait une valeur que le lecteur vient de recopier du cahier.
    /// </summary>
    [Theory]
    [InlineData("VERY_GOOD")]
    [InlineData("VeryGood")]
    [InlineData("very_good")]
    public void Les_deux_ecritures_du_cahier_sont_acceptees(string saisie)
    {
        var contenu = ContenuProduitFactory.Construire(
            "iPhone", "Description", UnProduit.Categorie,
            new TarificationSaisie(850_000),
            new ConditionSaisie(Type: saisie, Grade: "A"));

        contenu.IsSuccess.Should().BeTrue();
        contenu.Value.Condition.Type.Should().Be(ProductConditionType.VeryGood);
    }

    /// <summary>
    /// UNE VALEUR INCONNUE EST REFUSÉE, PAS RAMENÉE À UN DÉFAUT.
    ///
    /// Retomber sur « New » transformerait une faute de frappe du client en
    /// promesse commerciale : un vendeur qui envoie « REFURBISHD » verrait sa fiche
    /// publiée en NEUF.
    /// </summary>
    [Fact]
    public void Un_etat_commercial_inconnu_est_refuse()
    {
        var contenu = ContenuProduitFactory.Construire(
            "iPhone", "Description", UnProduit.Categorie,
            new TarificationSaisie(850_000),
            new ConditionSaisie(Type: "REFURBISHD"));

        contenu.IsFailure.Should().BeTrue();
        contenu.Error.Code.Should().Be("catalog.condition.type_invalid");
    }
}
