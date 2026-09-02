using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Application.Offers;
using HBA.Catalog.Domain.Offers;
using HBA.Catalog.Domain.Products;
using HBA.Catalog.Infrastructure.Integration;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Shared.Infrastructure.Inbox;
using Microsoft.Extensions.Logging.Abstractions;

namespace HBA.Catalog.UnitTests;

/// <summary>
/// Le retrait du catalogue quand un vendeur est sanctionné (ISSUE-025) ou qu'une
/// boutique ferme (ISSUE-041).
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CES TESTS EMPÊCHENT DE REVENIR.
///
/// Les deux mécanismes étaient ÉCRITS et n'avaient AUCUN APPELANT. Suspendre un
/// vendeur — y compris par refus de dossier KYB — ne retirait rien de la vente ;
/// fermer une boutique non plus. Le vendeur voyait « Suspendu » ou « Fermée » sur
/// son écran, et les commandes continuaient d'arriver.
///
/// CE QUI EST ÉPROUVÉ ICI, ET CE QUI NE L'EST PAS.
///
/// Ces tests portent sur la DÉCISION : quelles offres sont retirées, lesquelles
/// sont épargnées, et dans quel état chacune revient. Ils n'ont besoin ni de base
/// ni de Kafka, donc ils tournent vraiment.
///
/// Ils ne prouvent PAS le câblage — que l'événement d'intégration arrive bien
/// jusqu'à ces gestionnaires. C'est l'enregistrement dans `CatalogModuleInstaller`
/// qui en répond, et c'est précisément ce qui manquait : un événement sans
/// gestionnaire enregistré est ignoré EN SILENCE. le contrôle `(supprimé le 28 août 2026)` vérifie
/// que rien n'est injecté sans être fourni ; il ne vérifie pas qu'un consommateur
/// existe. Un test de bout en bout avec courtier reste à écrire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class SuspensionDuCatalogueTests
{
    private static readonly Guid Vendeur = Guid.NewGuid();
    private static readonly Guid Boutique = Guid.NewGuid();

    // ═════════════════════════════════════════════════════════════════════════
    // LE MARQUEUR
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// SANS L'ÉTAT D'AVANT, LA LEVÉE REMET EN VENTE DES OFFRES SANS STOCK.
    ///
    /// Le retrait ramène tout à `Suspended`. Si le motif ne disait pas d'où vient
    /// chaque offre, la levée les relèverait toutes en `Active` — y compris celles
    /// qui étaient en rupture. Le client commanderait, et la réservation de stock
    /// échouerait APRÈS le paiement.
    /// </summary>
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

    /// <summary>
    /// UNE LIGNE ÉCRITE AVANT CETTE ÉVOLUTION DOIT RESTER RECONNUE.
    ///
    /// La reconnaissance se fait par `StartsWith` : la parenthèse d'état s'insère
    /// APRÈS le préfixe. Un motif ancien reste donc identifié, et se relève en
    /// `Active` — le seul état qui ne raconte rien de faux.
    /// </summary>
    [Fact]
    public void Un_motif_sans_etat_reste_reconnu_et_se_releve_en_Active()
    {
        const string ancien = "seller_suspended: posé à la main";

        SellerCatalogSuspension.IsSellerSuspension(ancien).Should().BeTrue();
        SellerCatalogSuspension.ReadPreviousStatus(ancien).Should().Be(OfferStatus.Active);
    }

    /// <summary>
    /// LES DEUX MARQUEURS NE DOIVENT JAMAIS SE CONFONDRE.
    ///
    /// Un vendeur suspendu peut avoir une boutique déjà fermée. Si l'un des
    /// préfixes était le début de l'autre, rouvrir la boutique remettrait en vente
    /// le catalogue d'un vendeur toujours sanctionné — sans que personne ne le
    /// voie, l'offre redeviendrait simplement achetable.
    /// </summary>
    [Fact]
    public void Une_fermeture_de_boutique_n_est_pas_une_suspension_de_vendeur()
    {
        var boutique = StoreCatalogClosure.ComposeReason("congés", OfferStatus.Active);
        var vendeur = SellerCatalogSuspension.ComposeReason("fraude", OfferStatus.Active);

        SellerCatalogSuspension.IsSellerSuspension(boutique).Should().BeFalse();
        StoreCatalogClosure.IsStoreClosure(vendeur).Should().BeFalse();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ISSUE-025 — LE VENDEUR EST SUSPENDU
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LE TEST CENTRAL D'ISSUE-025 : un vendeur sanctionné ne vend plus.
    ///
    /// `Draft` est déjà invisible et `Archived` est terminal : les suspendre
    /// n'aurait aucun effet et leur poserait un motif que la levée devrait ensuite
    /// défaire.
    /// </summary>
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

    /// <summary>
    /// CHAQUE OFFRE REVIENT OÙ ELLE ÉTAIT, PAS TOUTES EN VENTE.
    ///
    /// C'est ce que le motif transporte, et c'est ce qui empêche une offre en
    /// rupture de redevenir achetable sans stock à la réhabilitation.
    /// </summary>
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

    /// <summary>
    /// LA RÉHABILITATION N'ANNULE PAS LES SANCTIONS DES AUTRES.
    ///
    /// Sans le filtre sur le motif, lever une suspension de vendeur relèverait
    /// TOUT ce qui est suspendu — y compris une offre qu'un modérateur avait
    /// retirée pour contrefaçon ou prix aberrant. Le vendeur obtiendrait, en prime
    /// de son rétablissement, l'annulation de décisions sans rapport.
    /// </summary>
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

    /// <summary>
    /// UN REJEU NE DOIT PAS RECOMPOSER LE MOTIF.
    ///
    /// Le rééquilibrage d'une partition Kafka rejoue les messages. Ici le
    /// gestionnaire ÉCRIT le motif, et le motif porte l'état d'avant : un rejeu
    /// après une levée partielle inscrirait un « avant » qui n'a jamais existé.
    /// C'est ce que la garde d'inbox ferme.
    /// </summary>
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

    // ═════════════════════════════════════════════════════════════════════════
    // ISSUE-041 — LA BOUTIQUE FERME
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LE TEST CENTRAL D'ISSUE-041 : une boutique fermée ne vend plus.
    ///
    /// On ne touche PAS aux fiches produit, seulement aux offres : une fiche est
    /// portée par le VENDEUR, et plusieurs de ses boutiques — voire d'autres
    /// vendeurs — peuvent proposer le même article.
    /// </summary>
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

    /// <summary>
    /// ROUVRIR UNE BOUTIQUE NE RÉHABILITE PAS SON VENDEUR.
    ///
    /// Un vendeur suspendu peut fermer puis rouvrir sa boutique. Si la réouverture
    /// relevait tout ce qui est suspendu, il lui suffirait de ce geste pour
    /// remettre son catalogue en vente malgré la sanction.
    /// </summary>
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

    /// <summary>
    /// MÊME EXIGENCE QU'À LA LEVÉE DE SUSPENSION VENDEUR.
    ///
    /// Une offre en rupture avant la fermeture ne doit pas revenir achetable à la
    /// réouverture. Catalog-service ne connaît pas inventory : il ne peut pas
    /// redemander le stock, seul le motif porte l'information.
    /// </summary>
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

    // ═════════════════════════════════════════════════════════════════════════
    // OUTILLAGE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Une offre amenée dans l'état voulu par des transitions LÉGALES.</summary>
    /// <remarks>
    /// ON NE FORCE PAS LE STATUT. Le poser directement — par réflexion ou par un
    /// constructeur de test — fabriquerait des états que la liste blanche des
    /// transitions n'autorise pas, et le test vérifierait alors un comportement
    /// impossible en production.
    /// </remarks>
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
