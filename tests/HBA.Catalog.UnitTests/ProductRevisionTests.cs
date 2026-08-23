using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES RÉVISIONS (§6, §28 : « création nouvelle révision »).
///
/// Le §31 en fait un critère d'acceptation : « si les révisions publiées restent
/// stables pendant une nouvelle validation ». Ce fichier vérifie exactement cela,
/// dans les deux sens — la nouvelle version avance, l'ancienne ne bouge pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ProductRevisionTests
{
    [Fact]
    public void Un_produit_neuf_a_une_revision_en_version_1()
    {
        var produit = UnProduit.Brouillon();

        produit.Revisions.Should().HaveCount(1);
        produit.CurrentRevision.Version.Should().Be(1);
        produit.CurrentRevision.Status.Should().Be(RevisionStatus.Draft);
        produit.PublishedRevisionId.Should().BeNull();
    }

    /// <summary>
    /// LE TEST QUI PORTE TOUT LE §6.
    ///
    /// Renommer un produit publié ouvre une version 2 — et l'acheteur continue de
    /// voir la version 1 pendant toute la validation. Si ce test tombe, la
    /// marketplace sert du contenu non relu, et personne ne s'en aperçoit avant un
    /// signalement.
    /// </summary>
    [Fact]
    public void Modifier_un_produit_publie_ouvre_une_revision_sans_toucher_a_la_publiee()
    {
        var produit = UnProduit.Publie();
        var publieeAvant = produit.PublishedRevisionId;

        produit.UpdateContenu(UnProduit.Contenu(name: "iPhone 16 Pro Max")).IsSuccess.Should().BeTrue();

        produit.Revisions.Should().HaveCount(2);
        produit.CurrentRevision.Version.Should().Be(2);
        produit.CurrentRevision.Status.Should().Be(RevisionStatus.Draft);
        produit.CurrentRevision.Name.Should().Be("iPhone 16 Pro Max");

        produit.PublishedRevisionId.Should().Be(publieeAvant);
        produit.PublishedRevision!.Name.Should().Be("iPhone 16 Pro");
        produit.Status.Should().Be(ProductStatus.Published, "la fiche reste en vente pendant la relecture");
    }

    [Fact]
    public void La_revision_en_validation_ne_retire_pas_la_fiche_de_la_vente()
    {
        var produit = UnProduit.Publie();
        produit.UpdateContenu(UnProduit.Contenu(name: "Nouveau nom"));

        produit.SubmitForReview(UnProduit.Maintenant).IsSuccess.Should().BeTrue();

        produit.Status.Should().Be(ProductStatus.Published);
        produit.CurrentRevision.Status.Should().Be(RevisionStatus.PendingReview);
        produit.PublishedRevision!.Name.Should().Be("iPhone 16 Pro");
    }

    [Fact]
    public void Publier_la_nouvelle_revision_remplace_lancienne_sans_la_supprimer()
    {
        var produit = UnProduit.Publie();
        var premiere = produit.PublishedRevisionId;

        produit.UpdateContenu(UnProduit.Contenu(name: "Nouveau nom"));
        produit.SubmitForReview(UnProduit.Maintenant);
        produit.Approve(UnProduit.Administrateur, UnProduit.Maintenant);
        produit.Publish(UnProduit.Maintenant).IsSuccess.Should().BeTrue();

        produit.PublishedRevision!.Name.Should().Be("Nouveau nom");

        // L'ANCIENNE SURVIT, MARQUÉE « REMPLACÉE ».
        //
        // La supprimer ferait disparaître la seule trace de ce qu'un acheteur a
        // vu au moment de sa commande — et un litige sur la description d'un
        // produit deviendrait inarbitrable.
        var ancienne = produit.Revisions.Single(r => r.Id == premiere);
        ancienne.Status.Should().Be(RevisionStatus.Superseded);
        ancienne.Name.Should().Be("iPhone 16 Pro");
    }

    /// <summary>
    /// REPUBLIER LA MÊME RÉVISION NE LA REMPLACE PAS PAR ELLE-MÊME.
    ///
    /// Après une dépublication, `PublishedRevision` et `CurrentRevision` sont le
    /// MÊME objet. Marquer « remplacée » sans vérifier le faisait passer par
    /// Superseded avant Published — sans effet visible tant que l'ordre des deux
    /// lignes le rattrapait, et faux le jour où quelqu'un les inverse.
    /// </summary>
    [Fact]
    public void Republier_la_meme_revision_ne_la_marque_pas_remplacee()
    {
        var produit = UnProduit.Publie();
        var revision = produit.CurrentRevisionId;

        produit.Unpublish();
        produit.Publish(UnProduit.Maintenant).IsSuccess.Should().BeTrue();

        produit.Revisions.Should().HaveCount(1);
        produit.CurrentRevisionId.Should().Be(revision);
        produit.CurrentRevision.Status.Should().Be(RevisionStatus.Published);
        produit.PublishedRevisionId.Should().Be(revision);
    }

    /// <summary>
    /// LA FRONTIÈRE DU §6, DU CÔTÉ « NON CRITIQUE ».
    ///
    /// Poser une étiquette éditoriale ne doit pas mettre la fiche en file
    /// d'attente. Si ce test tombe dans l'autre sens, la file de validation se
    /// remplit de corrections de mots-clés, les administrateurs approuvent en
    /// série sans lire, et la validation ne vaut plus rien — un échec bien plus
    /// grave que celui qu'on croyait éviter.
    /// </summary>
    [Fact]
    public void Changer_les_mots_cles_ne_cree_pas_de_revision()
    {
        var produit = UnProduit.Publie();

        produit.SetTags(new[] { "featured", "promo" });

        produit.Revisions.Should().HaveCount(1);
        produit.CurrentRevision.Tags.Should().BeEquivalentTo("featured", "promo");
    }

    [Fact]
    public void Changer_le_prix_de_reference_est_une_modification_critique()
    {
        var produit = UnProduit.Publie();

        produit.UpdateContenu(UnProduit.Contenu(pricing: UnProduit.Prix(900_000)));

        produit.Revisions.Should().HaveCount(2);
        produit.CurrentRevision.Version.Should().Be(2);
    }

    [Fact]
    public void Changer_la_condition_commerciale_est_une_modification_critique()
    {
        var produit = UnProduit.Publie();
        var occasion = ProductCondition.Create(ProductConditionType.VeryGood, "A").Value;

        produit.UpdateContenu(UnProduit.Contenu(condition: occasion));

        produit.Revisions.Should().HaveCount(2);
    }

    [Fact]
    public void Corriger_un_brouillon_reecrit_la_revision_courante()
    {
        var produit = UnProduit.Brouillon();

        produit.UpdateContenu(UnProduit.Contenu(name: "Autre nom"));
        produit.UpdateContenu(UnProduit.Contenu(name: "Encore un autre"));

        // Six corrections avant soumission ne doivent pas produire six versions.
        produit.Revisions.Should().HaveCount(1);
        produit.CurrentRevision.Name.Should().Be("Encore un autre");
    }

    [Fact]
    public void Un_produit_archive_ne_se_modifie_plus()
    {
        var produit = UnProduit.Brouillon();
        produit.Archive().IsSuccess.Should().BeTrue();

        var resultat = produit.UpdateContenu(UnProduit.Contenu(name: "Tentative"));

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.product.not_editable");
    }

    /// <summary>
    /// Le slug est porté par la révision, et les deux versions d'un même produit
    /// le partagent — c'est ce qui interdit un index unique simple, et ce qui
    /// justifie l'index PARTIEL sur les seules révisions publiées.
    /// </summary>
    [Fact]
    public void Les_revisions_dun_meme_produit_partagent_le_slug()
    {
        var produit = UnProduit.Publie();
        var slug = produit.CurrentRevision.Slug.Value;

        produit.UpdateContenu(UnProduit.Contenu(name: "Nom entièrement différent"));

        produit.CurrentRevision.Slug.Value.Should().Be(slug,
            "le slug est figé à la création : le changer casserait les liens déjà partagés");
    }
}
