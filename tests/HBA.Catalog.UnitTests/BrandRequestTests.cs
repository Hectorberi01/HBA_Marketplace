using HBA.Catalog.Domain.Brands;

namespace HBA.Catalog.UnitTests;

/// <summary>LES DEMANDES DE MARQUE (§10, §16).</summary>
public sealed class BrandRequestTests
{
    private static readonly Guid Marque = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void Une_demande_nait_en_attente()
    {
        var demande = BrandRequest.Create(UnProduit.Vendeur, "  Samsung  ").Value;

        demande.Status.Should().Be(BrandRequestStatus.Pending);
        demande.Name.Should().Be("Samsung");
        demande.BrandId.Should().BeNull();
    }

    [Fact]
    public void Une_demande_sans_nom_est_refusee()
        => BrandRequest.Create(UnProduit.Vendeur, "   ")
            .Error.Code.Should().Be("catalog.brand_request.name_required");

    /// <summary>LE CAS FRÉQUENT : RATTACHER, PAS CRÉER.</summary>
    [Fact]
    public void Une_approbation_peut_designer_une_marque_existante()
    {
        var demande = BrandRequest.Create(UnProduit.Vendeur, "samsumg").Value;

        demande.Approve(Marque, UnProduit.Administrateur, UnProduit.Maintenant).IsSuccess.Should().BeTrue();

        demande.Status.Should().Be(BrandRequestStatus.Approved);
        demande.BrandId.Should().Be(Marque);
        demande.ReviewedBy.Should().Be(UnProduit.Administrateur);
    }

    [Fact]
    public void Une_approbation_sans_marque_est_refusee()
    {
        var demande = BrandRequest.Create(UnProduit.Vendeur, "Samsung").Value;

        var resultat = demande.Approve(Guid.Empty, UnProduit.Administrateur, UnProduit.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.brand_request.brand_required");
    }

    /// <summary>MÊME EXIGENCE QUE SUR UN REJET DE FICHE : LE MOTIF EST OBLIGATOIRE.</summary>
    [Fact]
    public void Un_refus_sans_motif_est_refuse()
    {
        var demande = BrandRequest.Create(UnProduit.Vendeur, "Samsung").Value;

        var resultat = demande.Reject("  ", UnProduit.Administrateur, UnProduit.Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.brand_request.reason_required");
    }

    [Fact]
    public void Une_demande_ne_recoit_quune_seule_decision()
    {
        var demande = BrandRequest.Create(UnProduit.Vendeur, "Samsung").Value;
        demande.Approve(Marque, UnProduit.Administrateur, UnProduit.Maintenant);

        var seconde = demande.Reject("Finalement non.", UnProduit.Administrateur, UnProduit.Maintenant);

        seconde.IsFailure.Should().BeTrue();
        seconde.Error.Code.Should().Be("catalog.brand_request.already_reviewed");
        demande.Status.Should().Be(BrandRequestStatus.Approved);
    }
}
