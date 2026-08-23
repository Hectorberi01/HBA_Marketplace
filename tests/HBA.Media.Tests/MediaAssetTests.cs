using FluentAssertions;
using HBA.Media.Domain.Assets;
using HBA.Media.Domain.Assets.Events;
using Xunit;

namespace HBA.Media.Tests;

/// <summary>
/// L'agrégat média. Il ne contient aucun octet : ce qu'il garantit, c'est OÙ est
/// le fichier, QUI peut le voir, et QUAND il peut disparaître.
/// </summary>
public sealed class MediaAssetTests
{
    private static readonly Guid Proprietaire = Guid.NewGuid();
    private static readonly Guid Auteur = Guid.NewGuid();
    private static readonly DateTime Maintenant = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static MediaAsset Media(
        MediaType nature = MediaType.ProductImage,
        string nom = "photo.jpg",
        string typeMime = "image/jpeg",
        long taille = 2_000)
        => MediaAsset.Register(
            MediaOwnerType.Product, Proprietaire, nature, nom,
            "hba-public", "products/abc/def.jpg", typeMime, taille, "abc123", Auteur).Value;

    // ────────────────────────────────────────────────────────── Enregistrement

    /// <summary>
    /// LA VISIBILITÉ VIENT DE LA POLITIQUE, JAMAIS DE L'APPELANT.
    ///
    /// `Register` n'a même pas de paramètre de visibilité, et c'est la garantie
    /// elle-même : une pièce d'identité est privée parce qu'elle est une pièce
    /// d'identité, pas parce qu'un développeur y a pensé ce jour-là.
    /// </summary>
    [Fact]
    public void La_visibilite_est_imposee_par_la_nature_du_fichier()
    {
        Media(MediaType.ProductImage).Visibility.Should().Be(MediaVisibility.Public);

        MediaAsset.Register(
                MediaOwnerType.Seller, Proprietaire, MediaType.SellerDocument, "cni.pdf",
                "hba-private", "sellers/documents/x.pdf", "application/pdf", 2_000, "abc123", Auteur)
            .Value.Visibility.Should().Be(MediaVisibility.Private);
    }

    [Fact]
    public void Un_media_neuf_est_a_l_etat_uploaded_et_deja_servable()
    {
        var media = Media();

        media.Status.Should().Be(MediaStatus.Uploaded);
        media.IsUsable.Should().BeTrue();
    }

    [Fact]
    public void Un_media_sans_proprietaire_est_refuse()
        => MediaAsset.Register(
                MediaOwnerType.Product, Guid.Empty, MediaType.ProductImage, "photo.jpg",
                "hba-public", "products/a/b.jpg", "image/jpeg", 2_000, "abc123", Auteur)
            .Error.Code.Should().Be("media.owner_required");

    [Fact]
    public void Un_media_sans_empreinte_est_refuse()
        => MediaAsset.Register(
                MediaOwnerType.Product, Proprietaire, MediaType.ProductImage, "photo.jpg",
                "hba-public", "products/a/b.jpg", "image/jpeg", 2_000, "  ", Auteur)
            .Error.Code.Should().Be("media.checksum_required");

    [Fact]
    public void Un_media_sans_cle_de_stockage_est_refuse()
        => MediaAsset.Register(
                MediaOwnerType.Product, Proprietaire, MediaType.ProductImage, "photo.jpg",
                "hba-public", "  ", "image/jpeg", 2_000, "abc123", Auteur)
            .Error.Code.Should().Be("media.key_required");

    /// <summary>La politique de format s'applique dès l'enregistrement.</summary>
    [Fact]
    public void Un_format_hors_liste_blanche_est_refuse_des_l_enregistrement()
        => MediaAsset.Register(
                MediaOwnerType.Product, Proprietaire, MediaType.ProductImage, "logo.svg",
                "hba-public", "products/a/b.svg", "image/svg+xml", 2_000, "abc123", Auteur)
            .Error.Code.Should().Be("media.content_type_not_allowed");

    // ─────────────────────────────────────────────────── Nom et clé de stockage

    /// <summary>
    /// LE NOM D'ORIGINE EST CONSERVÉ, DONC IL DOIT ÊTRE ASSAINI.
    ///
    /// Il ne sert jamais de clé — mais il est réaffiché et proposé au
    /// téléchargement, donc il finit dans un en-tête HTTP. Un séparateur ou un
    /// caractère de contrôle qui y survivrait sortirait du fichier.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("dossier/photo.jpg", "photo.jpg")]
    [InlineData("photo.jpg", "photo.jpg")]
    public void Le_nom_d_origine_est_assaini(string fourni, string attendu)
        => Media(nom: fourni).OriginalFileName.Should().Be(attendu);

    [Fact]
    public void Un_nom_vide_recoit_un_nom_de_repli()
        => Media(nom: "   ").OriginalFileName.Should().Be("fichier");

    /// <summary>
    /// LA CLÉ EST CONSTRUITE SUR LES IDENTIFIANTS, PAS SUR LE NOM UTILISATEUR.
    ///
    /// Un nom utilisateur comme clé, ce sont des collisions, des « ../ », et un
    /// jour un fichier écrit là où personne ne l'attendait. Elle est aussi
    /// DÉTERMINISTE : elle se recalcule sans charger l'agrégat, ce qui permet de
    /// déposer les octets avant de créer la ligne.
    /// </summary>
    [Fact]
    public void La_cle_de_stockage_ne_contient_jamais_le_nom_fourni()
    {
        var id = MediaAssetId.New();

        var cle = MediaAsset.BuildObjectKey(
            MediaType.SellerDocument, Proprietaire, id, "application/pdf");

        cle.Should().StartWith("sellers/documents/");
        cle.Should().EndWith(".pdf");
        cle.Should().Contain(id.Value.ToString("N"));
        cle.Should().NotContain("..");
    }

    [Fact]
    public void La_cle_de_stockage_est_deterministe()
    {
        var id = MediaAssetId.New();

        MediaAsset.BuildObjectKey(MediaType.ProductImage, Proprietaire, id, "image/jpeg")
            .Should().Be(MediaAsset.BuildObjectKey(MediaType.ProductImage, Proprietaire, id, "image/jpeg"));
    }

    // ──────────────────────────────────────────────────────────── Cycle de vie

    [Fact]
    public void Le_traitement_ne_demarre_que_depuis_l_etat_uploaded()
    {
        var media = Media();

        media.BeginProcessing().IsSuccess.Should().BeTrue();
        media.Status.Should().Be(MediaStatus.Processing);
        media.BeginProcessing().Error.Code.Should().Be("media.not_uploaded");
    }

    /// <summary>
    /// UN RETRAITEMENT REMPLACE LES VARIANTES, IL NE LES EMPILE PAS.
    ///
    /// Empiler produirait deux miniatures pour une même image, et l'affichage
    /// prendrait la première venue — donc, la moitié du temps, l'ancienne.
    /// </summary>
    [Fact]
    public void Un_retraitement_remplace_les_variantes()
    {
        var media = Media();
        media.CompleteProcessing([Vignette("v1.jpg")]);

        media.CompleteProcessing([Vignette("v2.jpg"), Moyenne("m2.jpg")]);

        media.Variants.Should().HaveCount(2);
        media.Variants.Select(v => v.ObjectKey).Should().BeEquivalentTo(new[] { "v2.jpg", "m2.jpg" });
    }

    [Fact]
    public void Le_traitement_acheve_rend_le_media_pret_et_leve_l_evenement()
    {
        var media = Media();

        media.CompleteProcessing([Vignette("v1.jpg")]).IsSuccess.Should().BeTrue();

        media.Status.Should().Be(MediaStatus.Ready);
        media.DomainEvents.Should().ContainSingle(e => e is MediaReadyDomainEvent);
    }

    /// <summary>
    /// « FAILED » RESTE SERVABLE, ET C'EST TOUT L'ENJEU.
    ///
    /// Seules les VARIANTES ont échoué ; l'original est intact dans le stockage.
    /// Le refuser perdrait une photo parfaitement valable parce qu'une miniature
    /// n'a pas pu être calculée.
    /// </summary>
    [Fact]
    public void Un_traitement_echoue_laisse_l_original_servable()
    {
        var media = Media();

        media.FailProcessing("décodage impossible");

        media.Status.Should().Be(MediaStatus.Failed);
        media.IsUsable.Should().BeTrue("l'original est intact, seules les variantes manquent");
        media.FailureReason.Should().Be("décodage impossible");
        media.DomainEvents.Should().ContainSingle(e => e is MediaProcessingFailedDomainEvent);
    }

    [Fact]
    public void Une_raison_d_echec_vide_ne_laisse_pas_le_champ_vide()
    {
        var media = Media();

        media.FailProcessing("  ");

        media.FailureReason.Should().Be("inconnu");
    }

    // ────────────────────────────────────────────────── Suppression et purge

    /// <summary>
    /// SUPPRESSION LOGIQUE : LES OCTETS NE PARTENT PAS TOUT DE SUITE.
    ///
    /// Un vendeur qui retire sa pièce d'identité par erreur, ou un litige qui
    /// remonte trois mois plus tard, ne doivent pas se heurter à un octet effacé
    /// la veille.
    /// </summary>
    [Fact]
    public void Une_suppression_est_logique_et_leve_l_evenement()
    {
        var media = Media();

        media.SoftDelete(Maintenant).IsSuccess.Should().BeTrue();

        media.Status.Should().Be(MediaStatus.Deleted);
        media.DeletedOnUtc.Should().Be(Maintenant);
        media.IsUsable.Should().BeFalse();
        media.DomainEvents.Should().ContainSingle(e => e is MediaDeletedDomainEvent);
    }

    /// <summary>
    /// Kafka livre au moins une fois : le consommateur qui supprime sur retrait
    /// d'une pièce KYB rejouera. Une seconde suppression doit être un succès
    /// silencieux, sans second événement.
    /// </summary>
    [Fact]
    public void Supprimer_deux_fois_est_sans_effet()
    {
        var media = Media();
        media.SoftDelete(Maintenant);

        media.SoftDelete(Maintenant.AddHours(1)).IsSuccess.Should().BeTrue();

        media.DeletedOnUtc.Should().Be(Maintenant, "la première suppression fait foi");
        media.DomainEvents.OfType<MediaDeletedDomainEvent>().Should().HaveCount(1);
    }

    /// <summary>
    /// LA RÉTENTION DÉPEND DE LA NATURE, ET UNE FACTURE SE GARDE DIX ANS.
    ///
    /// Purger sur un délai unique effacerait des pièces comptables encore
    /// exigibles.
    /// </summary>
    [Fact]
    public void Une_facture_n_est_pas_purgeable_apres_le_delai_d_une_photo()
    {
        var facture = MediaAsset.Register(
            MediaOwnerType.Order, Proprietaire, MediaType.Invoice, "facture.pdf",
            "hba-private", "invoices/a/b.pdf", "application/pdf", 2_000, "abc123", Auteur).Value;

        facture.SoftDelete(Maintenant);

        facture.IsPurgeable(Maintenant.AddDays(31)).Should().BeFalse();
        facture.IsPurgeable(Maintenant.AddDays(3_651)).Should().BeTrue();
    }

    [Fact]
    public void Une_photo_produit_est_purgeable_apres_sa_retention()
    {
        var media = Media();
        media.SoftDelete(Maintenant);

        media.IsPurgeable(Maintenant.AddDays(29)).Should().BeFalse();
        media.IsPurgeable(Maintenant.AddDays(31)).Should().BeTrue();
    }

    [Fact]
    public void Un_media_vivant_n_est_jamais_purgeable()
        => Media().IsPurgeable(Maintenant.AddYears(50)).Should().BeFalse();

    /// <summary>
    /// LA PURGE DOIT EFFACER L'ORIGINAL **ET** SES DÉRIVÉES.
    ///
    /// N'effacer que l'original laisserait les miniatures d'une pièce retirée dans
    /// le stockage — c'est-à-dire le document lui-même, en plus petit.
    /// </summary>
    [Fact]
    public void Toutes_les_cles_a_effacer_incluent_les_variantes()
    {
        var media = Media();
        media.CompleteProcessing([Vignette("v1.jpg"), Moyenne("m1.jpg")]);

        media.AllObjectKeys().Should().BeEquivalentTo(new[] { media.ObjectKey, "v1.jpg", "m1.jpg" });
    }

    // ──────────────────────────────────────────────────────────── Lisibilité

    [Fact]
    public void Un_fichier_prive_n_est_jamais_lisible_par_url_permanente()
    {
        var piece = MediaAsset.Register(
            MediaOwnerType.Seller, Proprietaire, MediaType.SellerDocument, "cni.pdf",
            "hba-private", "sellers/documents/x.pdf", "application/pdf", 2_000, "abc123", Auteur).Value;

        piece.IsPubliclyReadable.Should().BeFalse();
    }

    [Fact]
    public void Un_fichier_public_supprime_cesse_d_etre_lisible()
    {
        var media = Media();
        media.IsPubliclyReadable.Should().BeTrue();

        media.SoftDelete(Maintenant);

        media.IsPubliclyReadable.Should().BeFalse();
    }

    [Fact]
    public void Des_dimensions_negatives_sont_refusees()
        => Media().SetDimensions(0, 100).Error.Code.Should().Be("media.dimensions_invalid");

    // ─────────────────────────────────────────────────────────────── Fixtures

    private static VariantToRecord Vignette(string cle)
        => new(MediaVariantType.Thumbnail, cle, "image/webp", 200, 200, 5_000);

    private static VariantToRecord Moyenne(string cle)
        => new(MediaVariantType.Medium, cle, "image/webp", 1024, 768, 40_000);
}
