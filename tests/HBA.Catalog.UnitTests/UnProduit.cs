using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE MONTEUR DE PRODUITS DES TESTS.
///
/// IL S'APPELLE « UnProduit » ET NON « Product », DÉLIBÉRÉMENT.
///
/// Une classe d'aide nommée comme le type qu'elle construit MASQUE ce type dans
/// tout le fichier de test. Les appels statiques — `Product.Create(...)` — se
/// mettent alors à désigner l'aide, et il faut qualifier le vrai type partout.
/// Le piège a déjà coûté une passe de correction dans les tests de promotion ;
/// il est refermé ici par le nom.
///
/// Ce que ce monteur apporte : un produit SOUMETTABLE. Les préconditions du §23
/// — boutique, description, au moins une image, exactement une image principale —
/// sont satisfaites par défaut, et chaque test n'a plus qu'à retirer CELLE qu'il
/// veut voir refusée. Écrites à la main dans chaque test, elles seraient recopiées
/// vingt fois, et l'oubli de l'une ferait échouer un test pour la mauvaise raison.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal static class UnProduit
{
    public static readonly Guid Vendeur = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Boutique = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Categorie = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Administrateur = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static readonly DateTimeOffset Maintenant =
        new(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);

    public static ProductPricing Prix(long basePrice = 850_000, long? compareAt = null)
        => ProductPricing.Create(basePrice, compareAt).Value;

    public static ContenuProduit Contenu(
        string name = "iPhone 16 Pro",
        string description = "Smartphone Apple 256 Go, garanti un an.",
        Guid? categoryId = null,
        ProductPricing? pricing = null,
        ProductCondition? condition = null,
        ProductType type = ProductType.Physical,
        Guid? brandId = null,
        IEnumerable<string>? tags = null,
        IReadOnlyList<GroupeDeSpecifications>? specifications = null)
        => new(
            name,
            description,
            categoryId ?? Categorie,
            pricing ?? Prix(),
            condition ?? ProductCondition.Neuf(),
            ShortDescription: null,
            Type: type,
            BrandId: brandId,
            Attributes: null,
            Tags: tags,
            Slug: null,
            Specifications: specifications);

    /// <summary>
    /// Une fiche technique d'exemple (§12), reprise de celle du cahier.
    ///
    /// ELLE EST REDONNÉE À CHAQUE APPEL, PAS PARTAGÉE.
    ///
    /// Les tests de modification comparent une saisie à une révision existante.
    /// Réutiliser une même instance ferait passer une comparaison de CONTENU pour
    /// une comparaison de RÉFÉRENCE, et le test de « modification à l'identique »
    /// réussirait pour la mauvaise raison.
    /// </summary>
    public static IReadOnlyList<GroupeDeSpecifications> FicheTechnique(string batterie = "4400 mAh")
        => new List<GroupeDeSpecifications>
        {
            new("Écran", new List<SpecificationSaisie>
            {
                new("Type", "Super Retina XDR OLED"),
                new("Taille", "6,3 pouces"),
            }),
            new("Batterie", new List<SpecificationSaisie>
            {
                new("Capacité", batterie),
            }),
        };

    /// <summary>Un brouillon nu : ni image, ni rien de ce que la soumission exige.</summary>
    public static Product Brouillon(ContenuProduit? contenu = null, Guid? storeId = null)
        => Product.Create(Vendeur, storeId ?? Boutique, contenu ?? Contenu()).Value;

    /// <summary>Un brouillon COMPLET : il passe la soumission tel quel.</summary>
    public static Product Soumettable(ContenuProduit? contenu = null)
    {
        var produit = Brouillon(contenu);
        produit.AddMedia(Guid.NewGuid(), "https://cdn.hba.test/main.webp", ProductMediaType.Image, "principale", isPrimary: true);
        return produit;
    }

    /// <summary>Soumis, en attente d'un administrateur.</summary>
    public static Product Soumis(ContenuProduit? contenu = null)
    {
        var produit = Soumettable(contenu);
        produit.SubmitForReview(Maintenant).IsSuccess.Should().BeTrue();
        return produit;
    }

    /// <summary>Approuvé, pas encore publié.</summary>
    public static Product Approuve(ContenuProduit? contenu = null)
    {
        var produit = Soumis(contenu);
        produit.Approve(Administrateur, Maintenant).IsSuccess.Should().BeTrue();
        return produit;
    }

    /// <summary>En vente.</summary>
    public static Product Publie(ContenuProduit? contenu = null)
    {
        var produit = Approuve(contenu);
        produit.Publish(Maintenant).IsSuccess.Should().BeTrue();
        return produit;
    }
}
