using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Application.Offers;
using HBA.Catalog.Domain.Offers;
using HBA.Catalog.Domain.Products;
using HBA.Catalog.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Shared.Infrastructure.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace HBA.Catalog.UnitTests;

/// <summary>
/// Le retrait du catalogue quand un vendeur est sanctionné (ISSUE-025) ou qu'une
/// boutique ferme (ISSUE-041).
/// </summary>
public sealed class SuspensionDuCatalogueTests
{
    private static readonly Guid Vendeur = Guid.NewGuid();
    private static readonly Guid Boutique = Guid.NewGuid();

    // LE MARQUEUR

    /// <summary>SANS L'ÉTAT D'AVANT, LA LEVÉE REMET EN VENTE DES OFFRES SANS STOCK.</summary>
    [Theory]
    [InlineData(OfferStatus.Active)]
    [InlineData(OfferStatus.OutOfStock)]
    [InlineData(OfferStatus.Paused)]
    public void Le_motif_conserve_l_etat_d_avant(OfferStatus avant)
    {
        var motif = SellerCatalogSuspension.ComposeReason("fraude avérée", avant);

        SellerCatalogSuspension.IsSellerSuspension(motif).Should().BeTrue();
        SellerCatalogSuspension.ReadPreviousStatus(motif).Should().Be(avant);
        motif.Should().Contain("fraude avérée");
    }

    /// <summary>UNE LIGNE ÉCRITE AVANT CETTE ÉVOLUTION DOIT RESTER RECONNUE.</summary>
    [Fact]
    public void Un_motif_sans_etat_reste_reconnu_et_se_releve_en_Active()
    {
        const string ancien = "seller_suspended: posé à la main";

        SellerCatalogSuspension.IsSellerSuspension(ancien).Should().BeTrue();
        SellerCatalogSuspension.ReadPreviousStatus(ancien).Should().Be(OfferStatus.Active);
    }

    /// <summary>LES DEUX MARQUEURS NE DOIVENT JAMAIS SE CONFONDRE.</summary>
    [Fact]
    public void Une_fermeture_de_boutique_n_est_pas_une_suspension_de_vendeur()
    {
        var boutique = StoreCatalogClosure.ComposeReason("congés", OfferStatus.Active);
        var vendeur = SellerCatalogSuspension.ComposeReason("fraude", OfferStatus.Active);

        SellerCatalogSuspension.IsSellerSuspension(boutique).Should().BeFalse();
        StoreCatalogClosure.IsStoreClosure(vendeur).Should().BeFalse();
    }

    // ISSUE-025 — LE VENDEUR EST SUSPENDU

    /// <summary>LE TEST CENTRAL D'ISSUE-025 : un vendeur sanctionné ne vend plus.</summary>
    [Fact]
    public async Task Suspendre_un_vendeur_retire_ses_offres_de_la_vente()
    {
        var enVente = UneOffre(OfferStatus.Active);
        var enRupture = UneOffre(OfferStatus.OutOfStock);
        var enPause = UneOffre(OfferStatus.Paused);
        var brouillon = UneOffre(OfferStatus.Draft);
        var archivee = UneOffre(OfferStatus.Archived);

        var depot = new DepotDOffres(enVente, enRupture, enPause, brouillon, archivee);
        var travail = new UniteDeTravail();

        await CreerRetrait(depot, travail).HandleAsync(new SellerSuspendedIntegrationEvent
        {
            SellerId = Vendeur,
            UserId = Guid.NewGuid(),
            Reason = "dossier KYB rejeté"
        });

        enVente.Status.Should().Be(OfferStatus.Suspended);
        enRupture.Status.Should().Be(OfferStatus.Suspended);
        enPause.Status.Should().Be(OfferStatus.Suspended);

        brouillon.Status.Should().Be(OfferStatus.Draft);
        archivee.Status.Should().Be(OfferStatus.Archived);

        travail.Sauvegardes.Should().Be(1);
    }

    /// <summary>CHAQUE OFFRE REVIENT OÙ ELLE ÉTAIT, PAS TOUTES EN VENTE.</summary>
    [Fact]
    public async Task La_levee_rend_a_chaque_offre_l_etat_qu_elle_avait()
    {
        var enVente = UneOffre(OfferStatus.Active);
        var enRupture = UneOffre(OfferStatus.OutOfStock);
        var enPause = UneOffre(OfferStatus.Paused);

        var depot = new DepotDOffres(enVente, enRupture, enPause);
        var travail = new UniteDeTravail();

        await CreerRetrait(depot, travail).HandleAsync(new SellerSuspendedIntegrationEvent
        {
            SellerId = Vendeur, UserId = Guid.NewGuid(), Reason = null
        });

        await CreerLevee(depot, travail).HandleAsync(new SellerSuspensionLiftedIntegrationEvent
        {
            SellerId = Vendeur, UserId = Guid.NewGuid()
        });

        enVente.Status.Should().Be(OfferStatus.Active);
        enRupture.Status.Should().Be(OfferStatus.OutOfStock);
        enPause.Status.Should().Be(OfferStatus.Paused);
    }

    /// <summary>LA RÉHABILITATION N'ANNULE PAS LES SANCTIONS DES AUTRES.</summary>
    [Fact]
    public async Task La_levee_ne_releve_pas_ce_qu_un_moderateur_avait_suspendu()
    {
        var moderee = UneOffre(OfferStatus.Active);
        moderee.Suspend("contrefaçon signalée par la marque");

        var depot = new DepotDOffres(moderee);
        var travail = new UniteDeTravail();

        await CreerLevee(depot, travail).HandleAsync(new SellerSuspensionLiftedIntegrationEvent
        {
            SellerId = Vendeur, UserId = Guid.NewGuid()
        });

        moderee.Status.Should().Be(OfferStatus.Suspended);
        moderee.StatusReason.Should().Be("contrefaçon signalée par la marque");
    }

    /// <summary>UN REJEU NE DOIT PAS RECOMPOSER LE MOTIF.</summary>
    [Fact]
    public async Task Un_evenement_deja_traite_ne_retire_rien_une_seconde_fois()
    {
        var offre = UneOffre(OfferStatus.Active);
        var depot = new DepotDOffres(offre);
        var travail = new UniteDeTravail();
        var boite = new BoiteDeReception { DejaTraite = true };

        var handler = new SellerSuspendedOfferWithdrawalHandler(
            depot, boite, travail, NullLogger<SellerSuspendedOfferWithdrawalHandler>.Instance);

        await handler.HandleAsync(new SellerSuspendedIntegrationEvent
        {
            SellerId = Vendeur, UserId = Guid.NewGuid(), Reason = null
        });

        offre.Status.Should().Be(OfferStatus.Active);
        travail.Sauvegardes.Should().Be(0);
    }

    // ISSUE-041 — LA BOUTIQUE FERME

    /// <summary>LE TEST CENTRAL D'ISSUE-041 : une boutique fermée ne vend plus.</summary>
    [Fact]
    public async Task Fermer_une_boutique_retire_ses_offres_de_la_vente()
    {
        var enVente = UneOffre(OfferStatus.Active);
        var brouillon = UneOffre(OfferStatus.Draft);

        var depot = new DepotDOffres(enVente, brouillon);
        var travail = new UniteDeTravail();

        var resultat = await CommandesBoutique(depot, travail)
            .Handle(new SuspendStoreCatalogCommand(Boutique, "congés annuels"), default);

        resultat.IsSuccess.Should().BeTrue();
        enVente.Status.Should().Be(OfferStatus.Suspended);
        brouillon.Status.Should().Be(OfferStatus.Draft);
    }

    /// <summary>ROUVRIR UNE BOUTIQUE NE RÉHABILITE PAS SON VENDEUR.</summary>
    [Fact]
    public async Task Rouvrir_une_boutique_ne_releve_pas_les_offres_d_un_vendeur_suspendu()
    {
        var sanctionnee = UneOffre(OfferStatus.Active);
        sanctionnee.Suspend(SellerCatalogSuspension.ComposeReason("fraude", OfferStatus.Active));

        var depot = new DepotDOffres(sanctionnee);
        var travail = new UniteDeTravail();

        await CommandesBoutique(depot, travail)
            .Handle(new ReinstateStoreCatalogCommand(Boutique), default);

        sanctionnee.Status.Should().Be(OfferStatus.Suspended);
    }

    /// <summary>MÊME EXIGENCE QU'À LA LEVÉE DE SUSPENSION VENDEUR.</summary>
    [Fact]
    public async Task Rouvrir_rend_a_une_offre_en_rupture_son_etat_de_rupture()
    {
        var enRupture = UneOffre(OfferStatus.OutOfStock);
        var depot = new DepotDOffres(enRupture);
        var travail = new UniteDeTravail();
        var commandes = CommandesBoutique(depot, travail);

        await commandes.Handle(new SuspendStoreCatalogCommand(Boutique, null), default);
        await commandes.Handle(new ReinstateStoreCatalogCommand(Boutique), default);

        enRupture.Status.Should().Be(OfferStatus.OutOfStock);
    }

    // OUTILLAGE

    /// <summary>Une offre amenée dans l'état voulu par des transitions LÉGALES.</summary>
    private static ProductOffer UneOffre(OfferStatus etat)
    {
        var offre = ProductOffer.Create(
            Guid.NewGuid(), Guid.NewGuid(), Boutique, Vendeur,
            sellerPrice: 10_000m, currency: "XOF",
            OfferCondition.New, FulfillmentType.Fbs,
            shipFromLocationId: Guid.NewGuid(), handlingTimeDays: 2,
            new OfferPricingRates(0.10m, 0.02m)).Value;

        switch (etat)
        {
            case OfferStatus.Draft:
                break;

            case OfferStatus.Archived:
                offre.Archive();
                break;

            case OfferStatus.Active:
                offre.Activate();
                break;

            case OfferStatus.OutOfStock:
                offre.Activate();
                offre.MarkOutOfStock();
                break;

            case OfferStatus.Paused:
                offre.Activate();
                offre.Pause();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(etat), etat, "État non gréé par ce constructeur.");
        }

        offre.Status.Should().Be(etat, "le constructeur de test doit produire l'état demandé");

        return offre;
    }

    private static SellerSuspendedOfferWithdrawalHandler CreerRetrait(
        DepotDOffres depot, UniteDeTravail travail)
        => new(depot, new BoiteDeReception(), travail,
            NullLogger<SellerSuspendedOfferWithdrawalHandler>.Instance);

    private static SellerSuspensionLiftedOfferReinstatementHandler CreerLevee(
        DepotDOffres depot, UniteDeTravail travail)
        => new(depot, new BoiteDeReception(), travail,
            NullLogger<SellerSuspensionLiftedOfferReinstatementHandler>.Instance);

    private static StoreCatalogCommandHandler CommandesBoutique(
        DepotDOffres depot, UniteDeTravail travail)
        => new(new DepotDeProduits(), depot, travail,
            NullLogger<StoreCatalogCommandHandler>.Instance);
}
