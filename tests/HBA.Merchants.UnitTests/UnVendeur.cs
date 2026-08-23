using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Domain.Stores;

namespace HBA.Merchants.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE MONTEUR DE VENDEURS DES TESTS.
///
/// IL S'APPELLE « UnVendeur » ET NON « Seller », DÉLIBÉRÉMENT.
///
/// Une classe d'aide nommée comme le type qu'elle construit MASQUE ce type dans
/// tout le fichier de test : les appels statiques — `Seller.Register(...)` — se
/// mettent alors à désigner l'aide, et il faut qualifier le vrai type partout. Le
/// piège a déjà coûté une passe de correction dans les tests de promotion, et la
/// même convention protège `UnProduit` côté catalogue.
///
/// Ce que ce monteur apporte : un vendeur ACTIVABLE. L'activation exige un KYB
/// vérifié ET des coordonnées de reversement ; écrites à la main dans chaque test,
/// ces deux préconditions seraient recopiées vingt fois, et l'oubli de l'une ferait
/// échouer un test pour la mauvaise raison.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal static class UnVendeur
{
    public static readonly Guid Compte = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public const string Boutique = "Chez Awa";

    /// <summary>Taux par défaut. Voir l'encadré de `Seller.CommissionRate` : colonne morte.</summary>
    public const decimal Commission = 0.10m;

    public static PayoutAccount Reversement()
        => PayoutAccount.Create(PayoutProvider.MtnMomo, "97000000", "Awa Codjo").Value;

    /// <summary>Fraîchement inscrit : `Pending`, KYB `NotStarted`, sans pièce ni reversement.</summary>
    public static Seller Inscrit(string? nom = null)
        => Seller.Register(Compte, nom ?? Boutique, Commission).Value;

    /// <summary>Dossier déposé : KYB `InReview`, une pièce.</summary>
    public static Seller DossierDepose()
    {
        var vendeur = Inscrit();
        vendeur.AddKybDocument(KybDocumentType.IdCard, Guid.NewGuid()).IsSuccess.Should().BeTrue();
        return vendeur;
    }

    /// <summary>KYB vérifié et reversement renseigné : il ne manque que l'activation.</summary>
    public static Seller Activable()
    {
        var vendeur = DossierDepose();
        vendeur.ApproveKyb().IsSuccess.Should().BeTrue();
        vendeur.SetPayoutAccount(Reversement()).IsSuccess.Should().BeTrue();
        return vendeur;
    }

    /// <summary>En activité.</summary>
    public static Seller Actif()
    {
        var vendeur = Activable();
        vendeur.Activate().IsSuccess.Should().BeTrue();
        return vendeur;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Boutiques
    // ═════════════════════════════════════════════════════════════════════════

    public static BusinessContact Contact()
        => BusinessContact.Create("97000000", "awa@example.bj").Value;

    /// <summary>Boutique en brouillon, sans lieu d'expédition.</summary>
    public static Store BoutiqueBrouillon(Guid? sellerId = null)
        => Store.Create(sellerId ?? Guid.NewGuid(), "HBA Fashion", Contact()).Value;

    /// <summary>Boutique prête à ouvrir : lieu d'expédition rattaché.</summary>
    public static Store BoutiqueOuvrable(Guid? sellerId = null)
    {
        var boutique = BoutiqueBrouillon(sellerId);
        boutique.AttachFulfillmentLocation(Guid.NewGuid()).IsSuccess.Should().BeTrue();
        return boutique;
    }

    /// <summary>Boutique en vente.</summary>
    public static Store BoutiqueOuverte(Guid? sellerId = null)
    {
        var boutique = BoutiqueOuvrable(sellerId);
        boutique.Open().IsSuccess.Should().BeTrue();
        return boutique;
    }
}
