using FluentAssertions;
using HBA.Media.Domain.Assets;
using Xunit;

namespace HBA.Media.Tests;

/// <summary>
/// La table des politiques. Cinq décisions par nature de fichier, et c'est leur
/// COHÉRENCE qui protège : une pièce d'identité rangée sous « products/ » finirait
/// servie par le CDN.
/// </summary>
public sealed class MediaTypePolicyTests
{
    /// <summary>
    /// LE TEST QUI EMPÊCHE UNE PIÈCE D'IDENTITÉ D'ATTERRIR DANS UN BUCKET PUBLIC.
    ///
    /// La visibilité est déduite de la NATURE du fichier, jamais fournie par
    /// l'appelant. Ce test énumère les natures sensibles une par une : une
    /// assertion générique « au moins une est privée » passerait encore le jour où
    /// quelqu'un rendrait `DriverDocument` public.
    /// </summary>
    [Theory]
    [InlineData(MediaType.SellerDocument)]
    [InlineData(MediaType.DriverDocument)]
    [InlineData(MediaType.Invoice)]
    public void Les_pieces_sensibles_sont_privees(MediaType nature)
        => MediaTypePolicy.For(nature).DefaultVisibility.Should().Be(MediaVisibility.Private);

    [Theory]
    [InlineData(MediaType.ProductImage)]
    [InlineData(MediaType.StoreMedia)]
    [InlineData(MediaType.RestaurantMedia)]
    [InlineData(MediaType.UserAvatar)]
    public void Les_images_vitrine_sont_publiques(MediaType nature)
        => MediaTypePolicy.For(nature).DefaultVisibility.Should().Be(MediaVisibility.Public);

    /// <summary>
    /// Une preuve de livraison n'est ni publique ni strictement privée : le client,
    /// le livreur et le support ont chacun une raison légitime de la voir. C'est
    /// delivery-service qui tranche, pas media.
    /// </summary>
    [Fact]
    public void Une_preuve_de_livraison_est_restreinte()
        => MediaTypePolicy.For(MediaType.DeliveryProof).DefaultVisibility
            .Should().Be(MediaVisibility.Restricted);

    /// <summary>
    /// AUCUNE VARIANTE SUR UN DOCUMENT PRIVÉ.
    ///
    /// Décliner une CNI en cinq tailles, c'est cinq copies d'un document sensible
    /// au lieu d'une — donc cinq fuites le jour où un bucket est mal configuré,
    /// et cinq objets à retrouver le jour d'une demande d'effacement.
    /// </summary>
    [Theory]
    [InlineData(MediaType.SellerDocument)]
    [InlineData(MediaType.DriverDocument)]
    [InlineData(MediaType.DeliveryProof)]
    [InlineData(MediaType.Invoice)]
    public void Un_document_prive_ou_restreint_ne_genere_pas_de_variantes(MediaType nature)
        => MediaTypePolicy.For(nature).GeneratesVariants.Should().BeFalse();

    /// <summary>
    /// La liste blanche, testée par ce qu'elle REFUSE. Vérifier qu'elle accepte le
    /// JPEG ne prouve rien : c'est le format exotique qui sert de vecteur.
    /// </summary>
    [Fact]
    public void Une_image_produit_refuse_un_format_hors_liste_blanche()
    {
        var resultat = MediaTypePolicy.For(MediaType.ProductImage)
            .Validate("image/svg+xml", "logo.svg", 1_000);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("media.content_type_not_allowed");
    }

    [Fact]
    public void Une_facture_n_accepte_que_le_pdf()
    {
        var politique = MediaTypePolicy.For(MediaType.Invoice);

        politique.Validate("application/pdf", "facture.pdf", 1_000).IsSuccess.Should().BeTrue();
        politique.Validate("image/png", "facture.png", 1_000)
            .Error.Code.Should().Be("media.content_type_not_allowed");
    }

    [Fact]
    public void Un_fichier_trop_volumineux_est_refuse()
    {
        var resultat = MediaTypePolicy.For(MediaType.UserAvatar)
            .Validate("image/png", "moi.png", 6L * 1024 * 1024);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("media.too_large");
    }

    [Fact]
    public void Un_fichier_vide_est_refuse()
        => MediaTypePolicy.For(MediaType.ProductImage)
            .Validate("image/png", "vide.png", 0)
            .Error.Code.Should().Be("media.empty");

    /// <summary>
    /// « facture.pdf » ANNONCÉ EN image/jpeg MENT SUR L'UN DES DEUX.
    ///
    /// On ne sait pas lequel, et c'est précisément pourquoi on refuse : deviner
    /// reviendrait à faire confiance à la moitié qu'on a choisie.
    /// </summary>
    [Fact]
    public void Une_extension_qui_contredit_le_type_mime_est_refusee()
    {
        var resultat = MediaTypePolicy.For(MediaType.SellerDocument)
            .Validate("image/jpeg", "facture.pdf", 1_000);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("media.extension_mismatch");
    }

    /// <summary>
    /// ET POURTANT « .jpg » ET « .jpeg » DOIVENT PASSER TOUS LES DEUX.
    ///
    /// Ils désignent la même chose. Refuser l'un des deux ferait échouer un upload
    /// sur trois, avec un message que l'utilisateur ne peut pas corriger — son
    /// appareil photo a choisi l'extension à sa place.
    /// </summary>
    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.jpeg")]
    [InlineData("photo.JPEG")]
    public void Les_deux_ecritures_du_jpeg_sont_acceptees(string nom)
        => MediaTypePolicy.For(MediaType.ProductImage)
            .Validate("image/jpeg", nom, 1_000).IsSuccess.Should().BeTrue();

    /// <summary>Un nom sans extension ne bloque pas : le type MIME fait foi.</summary>
    [Fact]
    public void Un_nom_sans_extension_est_accepte()
        => MediaTypePolicy.For(MediaType.ProductImage)
            .Validate("image/png", "capture", 1_000).IsSuccess.Should().BeTrue();

    /// <summary>
    /// L'extension vient du type MIME, jamais du nom fourni. `bin` pour l'inconnu :
    /// on ne devine pas, on range à part.
    /// </summary>
    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("image/webp", "webp")]
    [InlineData("application/pdf", "pdf")]
    [InlineData("application/x-msdownload", "bin")]
    public void L_extension_est_derivee_du_type_mime(string typeMime, string attendue)
        => MediaTypePolicy.ExtensionFor(typeMime).Should().Be(attendue);

    /// <summary>
    /// Une facture se garde dix ans, une photo produit trente jours. Un délai
    /// unique aurait soit effacé des pièces comptables, soit gardé des miniatures
    /// pour toujours.
    /// </summary>
    [Fact]
    public void La_retention_depend_de_la_nature_du_fichier()
    {
        MediaTypePolicy.For(MediaType.Invoice).RetentionDaysAfterDelete
            .Should().BeGreaterThan(MediaTypePolicy.For(MediaType.ProductImage).RetentionDaysAfterDelete);

        MediaTypePolicy.For(MediaType.DeliveryProof).RetentionDaysAfterDelete
            .Should().BeGreaterThan(MediaTypePolicy.For(MediaType.ProductImage).RetentionDaysAfterDelete,
                "un litige se déclare des semaines après la livraison");
    }

    /// <summary>
    /// CHAQUE NATURE DOIT AVOIR UNE POLITIQUE, SANS EXCEPTION.
    ///
    /// `For` lève sur une valeur inconnue. Ajouter une nature à l'énumération sans
    /// compléter la table ne casse aucune compilation : le service démarre, et
    /// c'est le premier upload de cette nature qui explose, en production.
    /// </summary>
    [Fact]
    public void Toute_nature_declaree_a_une_politique()
    {
        foreach (var nature in Enum.GetValues<MediaType>())
        {
            var politique = MediaTypePolicy.For(nature);

            politique.AllowedContentTypes.Should().NotBeEmpty($"{nature} doit admettre au moins un format");
            politique.MaxSizeBytes.Should().BePositive($"{nature} doit avoir un plafond");
            politique.KeyPrefix.Should().NotBeNullOrWhiteSpace($"{nature} doit être rangée quelque part");
            politique.RetentionDaysAfterDelete.Should().BePositive($"{nature} doit avoir une rétention");
        }
    }

    /// <summary>
    /// Deux natures de visibilité différente ne doivent pas partager de préfixe :
    /// c'est le préfixe qui range physiquement, et un document privé sous
    /// « products/ » finirait servi par le CDN.
    /// </summary>
    [Fact]
    public void Aucun_prefixe_n_est_partage_entre_le_public_et_le_prive()
    {
        var publics = Enum.GetValues<MediaType>()
            .Select(MediaTypePolicy.For)
            .Where(p => p.DefaultVisibility == MediaVisibility.Public)
            .Select(p => p.KeyPrefix)
            .ToHashSet();

        var proteges = Enum.GetValues<MediaType>()
            .Select(MediaTypePolicy.For)
            .Where(p => p.DefaultVisibility != MediaVisibility.Public)
            .Select(p => p.KeyPrefix);

        foreach (var prefixe in proteges)
        {
            publics.Should().NotContain(prefixe,
                "un fichier protégé rangé sous un préfixe public finirait servi par le CDN");
        }
    }
}
