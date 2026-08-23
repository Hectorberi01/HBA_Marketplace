using HBA.Catalog.Domain.Reviews;

namespace HBA.Catalog.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES DÉCISIONS D'ADMINISTRATION (§16, §20).
///
/// CETTE TABLE ÉTAIT CITÉE PAR TROIS COMMENTAIRES DU CODE ET N'EXISTAIT PAS.
///
/// `ProductLifecycleIntegrationEvents`, `Product.Reject` et `ProductStatus`
/// renvoyaient tous vers « ProductReview, où vivent les motifs ». Un rejet ne
/// conservait donc aucun motif : le vendeur apprenait que sa fiche était refusée,
/// jamais pourquoi.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ProductReviewTests
{
    private static readonly Guid Produit = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Revision = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>
    /// L'INVARIANT CENTRAL DE LA CLASSE.
    ///
    /// Le §16 montre un rejet avec un tableau `reasons` sans dire qu'il est
    /// obligatoire. Le rendre facultatif reproduirait le défaut d'origine : un
    /// vendeur qui ne sait pas quoi corriger resoumet à l'identique, et occupe la
    /// file une seconde fois pour la même raison.
    /// </summary>
    [Fact]
    public void Un_rejet_sans_motif_est_refuse()
    {
        var resultat = ProductReview.Rejet(
            Produit, Revision, 1, UnProduit.Vendeur, UnProduit.Administrateur,
            comment: "À corriger.", motifs: Array.Empty<MotifDeRejet>(), nowUtc: UnProduit.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.review.reason_required");
    }

    [Fact]
    public void Un_rejet_motive_conserve_ses_motifs()
    {
        var resultat = ProductReview.Rejet(
            Produit, Revision, 3, UnProduit.Vendeur, UnProduit.Administrateur,
            comment: "Le produit nécessite des corrections.",
            motifs: new[]
            {
                new MotifDeRejet(MotifsDeRejet.ImagesInvalides, "images", "Ajoutez une image principale plus claire."),
                new MotifDeRejet(MotifsDeRejet.DescriptionInsuffisante, "description", "Précisez la garantie."),
            },
            nowUtc: UnProduit.Maintenant);

        resultat.IsSuccess.Should().BeTrue();
        var decision = resultat.Value;

        decision.Decision.Should().Be(ReviewDecision.Rejected);
        decision.Reasons.Should().HaveCount(2);
        decision.Reasons.Select(m => m.Code).Should().Contain("INVALID_IMAGES");
        decision.Reasons.All(m => m.ReviewId == decision.Id).Should().BeTrue(
            "chaque motif doit être rattaché à sa décision, sinon la clé étrangère est refusée à l'insertion");
    }

    /// <summary>
    /// LE CODE EST NORMALISÉ, PAS REFUSÉ.
    ///
    /// La liste de `MotifsDeRejet` est un vocabulaire, pas une énumération fermée :
    /// un administrateur rencontrera des cas qu'aucune liste n'aura prévus, et lui
    /// imposer un code existant le ferait choisir le moins faux — l'information
    /// serait perdue. En revanche le client mobile compare des codes, pas de la
    /// casse.
    /// </summary>
    [Theory]
    [InlineData("invalid_images", "INVALID_IMAGES")]
    [InlineData("  Contenu Interdit  ", "CONTENU_INTERDIT")]
    [InlineData("MOTIF_INEDIT", "MOTIF_INEDIT")]
    public void Le_code_de_motif_est_normalise(string saisi, string attendu)
    {
        var decision = ProductReview.Rejet(
            Produit, Revision, 1, UnProduit.Vendeur, UnProduit.Administrateur, null,
            new[] { new MotifDeRejet(saisi, null, "Un message.") }, UnProduit.Maintenant).Value;

        decision.Reasons.Single().Code.Should().Be(attendu);
    }

    [Fact]
    public void Un_motif_sans_message_est_refuse()
    {
        var resultat = ProductReview.Rejet(
            Produit, Revision, 1, UnProduit.Vendeur, UnProduit.Administrateur, null,
            new[] { new MotifDeRejet(MotifsDeRejet.PrixSuspect, "pricing", "   ") }, UnProduit.Maintenant);

        // Un code seul ne dit rien au vendeur : c'est le message qu'il lit.
        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.review.reason_message_required");
    }

    /// <summary>
    /// Une approbation n'a pas de motif — et n'en exige pas. Le commentaire reste
    /// facultatif : rien à corriger, rien à expliquer.
    /// </summary>
    [Fact]
    public void Une_approbation_na_pas_besoin_de_motif()
    {
        var resultat = ProductReview.Approbation(
            Produit, Revision, 2, UnProduit.Vendeur, UnProduit.Administrateur, null, UnProduit.Maintenant);

        resultat.IsSuccess.Should().BeTrue();
        resultat.Value.Decision.Should().Be(ReviewDecision.Approved);
        resultat.Value.Reasons.Should().BeEmpty();
    }

    /// <summary>
    /// SANS RELECTEUR, LE JOURNAL NE VAUT RIEN.
    ///
    /// C'est sa seule raison d'exister : savoir QUI a approuvé, et sur quel
    /// contenu. Une décision anonyme occuperait une ligne sans répondre à la
    /// question qu'on lui posera.
    /// </summary>
    [Fact]
    public void Une_decision_sans_relecteur_est_refusee()
    {
        var resultat = ProductReview.Approbation(
            Produit, Revision, 1, UnProduit.Vendeur, Guid.Empty, null, UnProduit.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.review.reviewer_required");
    }

    /// <summary>
    /// La décision retient la RÉVISION jugée, pas seulement le produit. Sans ce
    /// champ, une fiche modifiée trois fois après approbation rendrait la décision
    /// illisible.
    /// </summary>
    [Fact]
    public void Une_decision_designe_la_revision_jugee()
    {
        var decision = ProductReview.Approbation(
            Produit, Revision, 5, UnProduit.Vendeur, UnProduit.Administrateur, "OK", UnProduit.Maintenant).Value;

        decision.RevisionId.Should().Be(Revision);
        decision.RevisionVersion.Should().Be(5);
        decision.ReviewedBy.Should().Be(UnProduit.Administrateur);
    }
}
