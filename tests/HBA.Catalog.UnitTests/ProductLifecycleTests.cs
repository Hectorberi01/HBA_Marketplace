using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CYCLE DE VIE (§4, §5, §15, §28).
///
/// Le premier test de ce fichier est celui qui compte : « Un vendeur ne peut
/// jamais publier un produit qui n'a pas été approuvé par un administrateur. »
/// Tous les autres décrivent les chemins par lesquels on pourrait l'obtenir
/// quand même.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ProductLifecycleTests
{
    // ═════════════════════════════════════════════════════════════════════════
    // LA RÈGLE ABSOLUE
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Publier_un_brouillon_est_refuse()
    {
        var produit = UnProduit.Soumettable();

        var resultat = produit.Publish(UnProduit.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.product.not_approved");
        produit.Status.Should().Be(ProductStatus.Draft);
        produit.PublishedRevisionId.Should().BeNull("rien ne doit devenir visible");
    }

    [Fact]
    public void Publier_un_produit_soumis_mais_non_approuve_est_refuse()
    {
        var produit = UnProduit.Soumis();

        var resultat = produit.Publish(UnProduit.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.product.not_approved");
    }

    [Fact]
    public void Publier_un_produit_rejete_est_refuse()
    {
        var produit = UnProduit.Soumis();
        produit.Reject(UnProduit.Administrateur, UnProduit.Maintenant);

        var resultat = produit.Publish(UnProduit.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.product.not_approved");
    }

    [Fact]
    public void Publier_apres_approbation_rend_la_revision_visible()
    {
        var produit = UnProduit.Approuve();

        var resultat = produit.Publish(UnProduit.Maintenant);

        resultat.IsSuccess.Should().BeTrue();
        produit.Status.Should().Be(ProductStatus.Published);
        produit.PublishedRevisionId.Should().Be(produit.CurrentRevisionId);
        produit.PublishedRevision!.Status.Should().Be(RevisionStatus.Published);
        produit.PublishedAtUtc.Should().Be(UnProduit.Maintenant);
    }

    /// <summary>
    /// LE CHEMIN LE PLUS DANGEREUX DE TOUTE LA MACHINE À ÉTATS.
    ///
    /// Un produit suspendu a, par définition, une révision APPROUVÉE derrière lui :
    /// c'est bien un administrateur qui l'avait validée, avant la sanction. La
    /// garde « la révision est-elle approuvée ? » est donc satisfaite — et à elle
    /// seule, elle laisserait le vendeur republier ce que la plateforme vient de
    /// retirer. C'est la SECONDE garde, celle sur le statut du produit, qui ferme
    /// la porte, et c'est pour ce cas précis qu'elle existe.
    /// </summary>
    [Fact]
    public void Un_produit_suspendu_ne_peut_pas_etre_republie_par_le_vendeur()
    {
        var produit = UnProduit.Publie();
        produit.Suspend("Signalement en cours de vérification").IsSuccess.Should().BeTrue();

        var resultat = produit.Publish(UnProduit.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        produit.Status.Should().Be(ProductStatus.Suspended);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SOUMISSION — LES PRÉCONDITIONS QUE L'AGRÉGAT POSSÈDE (§23)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Soumettre_sans_image_est_refuse()
    {
        var produit = UnProduit.Brouillon();

        var resultat = produit.SubmitForReview(UnProduit.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.product.image_required");
    }

    [Fact]
    public void Soumettre_sans_description_est_refuse()
    {
        var produit = UnProduit.Soumettable(UnProduit.Contenu(description: "   "));

        var resultat = produit.SubmitForReview(UnProduit.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.product.description_required");
    }

    /// <summary>
    /// Une fiche antérieure au multi-boutique ne peut pas avancer. Voir l'encadré
    /// de <c>Product.StoreId</c> : c'est le seul endroit où l'absence de boutique
    /// se manifeste, et il faut qu'elle se manifeste.
    /// </summary>
    [Fact]
    public void Soumettre_sans_boutique_est_refuse()
    {
        var produit = Product.Create(UnProduit.Vendeur, storeId: null, UnProduit.Contenu()).Value;
        produit.AddMedia(Guid.NewGuid(), "https://cdn.hba.test/a.webp", ProductMediaType.Image, "a", isPrimary: true);

        var resultat = produit.SubmitForReview(UnProduit.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.product.store_required");
    }

    [Fact]
    public void Soumettre_deux_fois_est_refuse_avec_un_code_propre()
    {
        var produit = UnProduit.Soumis();

        var resultat = produit.SubmitForReview(UnProduit.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.product.already_submitted");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // REJET ET CORRECTION (§28 : « rejet et correction »)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Rejeter_puis_corriger_puis_resoumettre_mene_a_la_publication()
    {
        var produit = UnProduit.Soumis();

        produit.Reject(UnProduit.Administrateur, UnProduit.Maintenant).IsSuccess.Should().BeTrue();
        produit.Status.Should().Be(ProductStatus.Rejected);

        // LA CORRECTION NE CRÉE PAS DE NOUVELLE RÉVISION.
        //
        // Rien n'a jamais été publié : réécrire en place est exactement ce qu'il
        // faut. Une nouvelle version à chaque aller-retour de validation ferait
        // grimper le compteur à « version 7 » pour une fiche jamais mise en vente.
        var version = produit.CurrentRevision.Version;
        produit.UpdateContenu(UnProduit.Contenu(name: "iPhone 16 Pro (corrigé)")).IsSuccess.Should().BeTrue();
        produit.CurrentRevision.Version.Should().Be(version);

        // CORRIGER EST LA TRANSITION REJECTED → DRAFT.
        //
        // Le §4 porte l'étiquette « correction » sur cette flèche, et le §5
        // n'autorise PENDING_REVIEW que depuis DRAFT. Sans ce retour, la fiche
        // corrigée n'était plus soumettable du tout.
        produit.Status.Should().Be(ProductStatus.Draft);
        produit.CurrentRevision.Status.Should().Be(RevisionStatus.Draft);

        produit.SubmitForReview(UnProduit.Maintenant).IsSuccess.Should().BeTrue();
        produit.Approve(UnProduit.Administrateur, UnProduit.Maintenant).IsSuccess.Should().BeTrue();
        produit.Publish(UnProduit.Maintenant).IsSuccess.Should().BeTrue();

        produit.Status.Should().Be(ProductStatus.Published);
        produit.PublishedRevision!.Name.Should().Be("iPhone 16 Pro (corrigé)");
    }

    /// <summary>
    /// LE REJET D'UNE NOUVELLE VERSION NE RETIRE PAS LA FICHE DE LA VENTE.
    ///
    /// C'est le pendant du §6 côté rejet : l'acheteur continue de voir la version
    /// approuvée pendant que le vendeur corrige la suivante. Ramener le PRODUIT à
    /// DRAFT ici aurait dépublié une fiche en vente parce qu'un administrateur a
    /// refusé une modification de description.
    /// </summary>
    [Fact]
    public void Corriger_une_revision_rejetee_sur_un_produit_publie_ne_le_depublie_pas()
    {
        var produit = UnProduit.Publie();
        produit.UpdateContenu(UnProduit.Contenu(name: "Nom refusé"));
        produit.SubmitForReview(UnProduit.Maintenant).IsSuccess.Should().BeTrue();
        produit.Reject(UnProduit.Administrateur, UnProduit.Maintenant).IsSuccess.Should().BeTrue();

        produit.Status.Should().Be(ProductStatus.Published);
        produit.CurrentRevision.Status.Should().Be(RevisionStatus.Rejected);

        produit.UpdateContenu(UnProduit.Contenu(name: "Nom corrigé")).IsSuccess.Should().BeTrue();

        produit.Status.Should().Be(ProductStatus.Published, "la fiche approuvée reste en vente");
        produit.CurrentRevision.Status.Should().Be(RevisionStatus.Draft);
        produit.PublishedRevision!.Name.Should().Be("iPhone 16 Pro");

        produit.SubmitForReview(UnProduit.Maintenant).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Un_produit_en_validation_ne_peut_pas_etre_modifie()
    {
        var produit = UnProduit.Soumis();

        var resultat = produit.UpdateContenu(UnProduit.Contenu(name: "Autre nom"));

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.product.not_editable");
    }

    /// <summary>
    /// LE MÊME VERROU SUR UN PRODUIT DÉJÀ EN VENTE — ET C'EST LE CAS QUI
    ///    ÉCHAPPAIT À LA GARDE.
    ///
    /// Le produit reste PUBLISHED pendant que sa nouvelle version est relue (§6).
    /// Une garde posée sur le statut du PRODUIT ne se déclenchait donc pas, et le
    /// vendeur pouvait réécrire sous les yeux de l'administrateur — ou ouvrir une
    /// v3 en laissant la v2 bloquée dans la file de validation pour toujours.
    /// </summary>
    [Fact]
    public void Une_revision_en_validation_est_verrouillee_meme_si_le_produit_est_publie()
    {
        var produit = UnProduit.Publie();
        produit.UpdateContenu(UnProduit.Contenu(name: "Version 2"));
        produit.SubmitForReview(UnProduit.Maintenant).IsSuccess.Should().BeTrue();
        produit.Status.Should().Be(ProductStatus.Published);

        var resultat = produit.UpdateContenu(UnProduit.Contenu(name: "Version 2 bis"));

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.product.not_editable");
        produit.Revisions.Should().HaveCount(2, "aucune troisième version ne doit s'ouvrir");
        produit.CurrentRevision.Name.Should().Be("Version 2");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SUSPENSION ET RESTAURATION (§28)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Restaurer_rend_le_produit_a_approved_pas_a_published()
    {
        var produit = UnProduit.Publie();
        produit.Suspend("prix aberrant");

        produit.Restore().IsSuccess.Should().BeTrue();

        // C'EST LE VENDEUR QUI REMET EN VENTE, PAS LA PLATEFORME.
        //
        // Rendre la fiche directement à PUBLISHED remettrait en ligne, sans que
        // personne ne l'ait décidé, un produit que le vendeur a peut-être entre-temps
        // corrigé ou retiré de son offre.
        produit.Status.Should().Be(ProductStatus.Approved);
        produit.SuspensionReason.Should().BeNull();
    }

    [Fact]
    public void Un_produit_suspendu_ne_peut_pas_etre_archive()
    {
        var produit = UnProduit.Publie();
        produit.Suspend("signalement");

        var resultat = produit.Archive();

        // Sinon la sanction se contournerait en archivant puis en recréant la fiche.
        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.product.invalid_status_transition");
        produit.Status.Should().Be(ProductStatus.Suspended);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DÉPUBLICATION ET REPUBLICATION
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Republier_apres_depublication_ne_demande_pas_une_nouvelle_validation()
    {
        var produit = UnProduit.Publie();

        produit.Unpublish().IsSuccess.Should().BeTrue();
        produit.Status.Should().Be(ProductStatus.Unpublished);

        // La révision reste approuvée : le contenu n'a pas changé, il n'y a rien
        // de nouveau à relire.
        produit.Publish(UnProduit.Maintenant).IsSuccess.Should().BeTrue();
        produit.Status.Should().Be(ProductStatus.Published);
    }

    [Fact]
    public void Publier_un_produit_deja_publie_est_refuse_avec_un_code_propre()
    {
        var produit = UnProduit.Publie();

        var resultat = produit.Publish(UnProduit.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.product.already_published");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LA LISTE BLANCHE ELLE-MÊME
    // ═════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(ProductStatus.Draft, ProductStatus.Published)]
    [InlineData(ProductStatus.Draft, ProductStatus.Approved)]
    [InlineData(ProductStatus.PendingReview, ProductStatus.Draft)]
    [InlineData(ProductStatus.PendingReview, ProductStatus.Published)]
    [InlineData(ProductStatus.Rejected, ProductStatus.Published)]
    [InlineData(ProductStatus.Suspended, ProductStatus.Published)]
    [InlineData(ProductStatus.Suspended, ProductStatus.Archived)]
    [InlineData(ProductStatus.Published, ProductStatus.Archived)]
    [InlineData(ProductStatus.Archived, ProductStatus.Draft)]
    [InlineData(ProductStatus.Archived, ProductStatus.Published)]
    public void Les_transitions_hors_liste_blanche_sont_refusees(ProductStatus de, ProductStatus vers)
        => ProductStatusTransitions.IsAllowed(de, vers).Should().BeFalse();

    [Theory]
    [InlineData(ProductStatus.Draft, ProductStatus.PendingReview)]
    [InlineData(ProductStatus.PendingReview, ProductStatus.Approved)]
    [InlineData(ProductStatus.PendingReview, ProductStatus.Rejected)]
    [InlineData(ProductStatus.Rejected, ProductStatus.Draft)]
    [InlineData(ProductStatus.Approved, ProductStatus.Published)]
    [InlineData(ProductStatus.Published, ProductStatus.Unpublished)]
    [InlineData(ProductStatus.Unpublished, ProductStatus.Published)]
    [InlineData(ProductStatus.Published, ProductStatus.Suspended)]
    [InlineData(ProductStatus.Suspended, ProductStatus.Approved)]
    public void Les_transitions_du_cahier_sont_autorisees(ProductStatus de, ProductStatus vers)
        => ProductStatusTransitions.IsAllowed(de, vers).Should().BeTrue();

    /// <summary>
    /// UN SEUL STATUT EST PUBLIC, ET APPROVED N'EN FAIT PAS PARTIE.
    ///
    /// Ce test existe parce que la confusion « validé donc en ligne » est celle
    /// qu'on écrit le plus naturellement en lisant le §5 de haut en bas.
    /// </summary>
    [Fact]
    public void Seul_published_est_visible_publiquement()
    {
        foreach (var statut in Enum.GetValues<ProductStatus>())
        {
            ProductStatusTransitions.IsPubliclyVisible(statut)
                .Should().Be(statut == ProductStatus.Published, $"statut testé : {statut}");
        }
    }
}
