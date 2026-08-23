using HBA.Catalog.Application.Products;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE LA VITRINE MONTRE, ET CE QU'ELLE NE DOIT PAS MONTRER (§17).
///
/// CES TESTS COUVRENT UN DÉFAUT QUI A ÉTÉ OUVERT EN PRODUCTION.
///
/// Les trois routes produit anonymes projetaient `ToSellerSummary` — la révision
/// COURANTE — et la route de liste était câblée sur la requête de la console
/// d'administration, dont le filtre de statut est facultatif. Un visiteur obtenait
/// donc les brouillons, les fiches en attente de validation, les rejetées et les
/// suspendues ; pour une fiche publiée, la version en cours de relecture.
///
/// Ce fichier fixe la frontière du côté où elle se vérifie sans base de données :
/// la projection. Le filtre SQL, lui, vit dans `ProductRepository.SearchPublishedAsync`
/// et demandera un test d'intégration (lot 7).
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ProjectionPubliqueTests
{
    [Fact]
    public void Un_brouillon_ne_sort_pas_en_public()
        => ProductMapping.ToPublicSummary(UnProduit.Soumettable()).Should().BeNull();

    [Fact]
    public void Une_fiche_en_validation_ne_sort_pas_en_public()
        => ProductMapping.ToPublicSummary(UnProduit.Soumis()).Should().BeNull();

    /// <summary>
    /// APPROUVÉ N'EST PAS PUBLIÉ, ET C'EST LA CONFUSION LA PLUS NATURELLE.
    ///
    /// On lit le §5 de haut en bas et l'on retient « validé, donc en ligne ». Le
    /// vendeur, lui, prépare parfois une fiche pour une date précise.
    /// </summary>
    [Fact]
    public void Une_fiche_approuvee_mais_non_publiee_ne_sort_pas_en_public()
        => ProductMapping.ToPublicSummary(UnProduit.Approuve()).Should().BeNull();

    [Fact]
    public void Une_fiche_depubliee_ne_sort_plus_en_public()
    {
        var produit = UnProduit.Publie();
        produit.Unpublish();

        ProductMapping.ToPublicSummary(produit).Should().BeNull();
    }

    /// <summary>
    /// LA RÉVISION RESTE `Published` APRÈS UNE DÉPUBLICATION.
    ///
    /// C'est voulu — cela réserve l'URL et permet de republier sans nouvelle
    /// validation. Mais une recherche qui partirait de la RÉVISION plutôt que du
    /// PRODUIT rendrait donc les fiches retirées de la vente. Ce test fixe le
    /// comportement attendu à côté du précédent pour que le lien soit visible.
    /// </summary>
    [Fact]
    public void La_revision_reste_publiee_apres_une_depublication()
    {
        var produit = UnProduit.Publie();
        produit.Unpublish();

        produit.PublishedRevision!.Status.Should().Be(RevisionStatus.Published);
        produit.Status.Should().Be(ProductStatus.Unpublished);
    }

    [Fact]
    public void Une_fiche_suspendue_ne_sort_pas_en_public()
    {
        var produit = UnProduit.Publie();
        produit.Suspend("signalement");

        ProductMapping.ToPublicSummary(produit).Should().BeNull();
    }

    [Fact]
    public void Une_fiche_archivee_ne_sort_pas_en_public()
    {
        var produit = UnProduit.Brouillon();
        produit.Archive();

        ProductMapping.ToPublicSummary(produit).Should().BeNull();
    }

    [Fact]
    public void Une_fiche_publiee_sort_en_public()
    {
        var resume = ProductMapping.ToPublicSummary(UnProduit.Publie());

        resume.Should().NotBeNull();
        resume!.Name.Should().Be("iPhone 16 Pro");
        resume.Status.Should().Be("Published");
    }

    /// <summary>
    /// LE TEST CENTRAL DE TOUT LE LOT.
    ///
    /// Une fiche en vente dont une nouvelle version attend validation : le public
    /// doit voir l'ANCIENNE, le vendeur la NOUVELLE. C'est le §6 et le §17 réunis,
    /// et c'est précisément ce que l'ancienne projection unique ne pouvait pas
    /// faire — elle n'avait qu'une réponse pour les deux questions.
    /// </summary>
    [Fact]
    public void Pendant_une_validation_le_public_voit_lancienne_version_et_le_vendeur_la_nouvelle()
    {
        var produit = UnProduit.Publie();
        produit.UpdateContenu(UnProduit.Contenu(name: "iPhone 16 Pro Max"));
        produit.SubmitForReview(UnProduit.Maintenant);

        ProductMapping.ToPublicSummary(produit)!.Name.Should().Be("iPhone 16 Pro");
        ProductMapping.ToSellerSummary(produit).Name.Should().Be("iPhone 16 Pro Max");
    }

    /// <summary>
    /// Même chose après un REJET : le contenu refusé ne doit jamais atteindre la
    /// vitrine, alors qu'il reste la révision courante du vendeur.
    /// </summary>
    [Fact]
    public void Un_contenu_rejete_natteint_pas_la_vitrine()
    {
        var produit = UnProduit.Publie();
        produit.UpdateContenu(UnProduit.Contenu(name: "Nom refusé"));
        produit.SubmitForReview(UnProduit.Maintenant);
        produit.Reject(UnProduit.Administrateur, UnProduit.Maintenant);

        ProductMapping.ToPublicSummary(produit)!.Name.Should().Be("iPhone 16 Pro");
        ProductMapping.ToSellerSummary(produit).Name.Should().Be("Nom refusé");
    }

    /// <summary>
    /// LE TRI PUBLIC EST UNE LISTE BLANCHE, PAS UNE CHAÎNE LIBRE.
    ///
    /// Un tri arbitraire venu du client permettrait de trier sur `cost_price` — le
    /// coût d'achat du vendeur, que le §17 interdit d'exposer. On ne pourrait pas
    /// le LIRE, mais on pourrait l'ordonner, donc l'encadrer.
    /// </summary>
    [Theory]
    [InlineData(null, TriPublic.Nouveaute)]
    [InlineData("", TriPublic.Nouveaute)]
    [InlineData("cost_price", TriPublic.Nouveaute)]
    [InlineData("Pricing.CostPrice", TriPublic.Nouveaute)]
    [InlineData("price_asc", TriPublic.PrixCroissant)]
    [InlineData("PRICE_DESC", TriPublic.PrixDecroissant)]
    [InlineData("name", TriPublic.Nom)]
    public void Le_tri_public_retombe_sur_la_nouveaute_hors_liste_blanche(string? demande, string attendu)
        => TriPublic.Normaliser(demande).Should().Be(attendu);
}
