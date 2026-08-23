using HBA.Merchants.Domain.Stores;
using HBA.Merchants.Domain.Stores.Events;

namespace HBA.Merchants.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA BOUTIQUE — QUATRE STATUTS, DONT DEUX QUI SE RESSEMBLENT ET N'ONT RIEN À VOIR.
///
/// `Closed` est une décision du VENDEUR — congés, travaux, réversible d'un clic.
/// `Suspended` est une sanction de la PLATEFORME — seule elle la lève. Les deux
/// retirent la boutique de la vente ; confondre les deux, c'est laisser un vendeur
/// annuler sa propre sanction.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class StoreLifecycleTests
{
    [Fact]
    public void Une_boutique_neuve_est_en_brouillon()
    {
        var boutique = UnVendeur.BoutiqueBrouillon();

        boutique.Status.Should().Be(StoreStatus.Draft);
        boutique.DomainEvents.Should().ContainItemsAssignableTo<StoreCreatedDomainEvent>();
    }

    /// <summary>
    /// SANS POINT DE RETRAIT, UN COLIS N'A PAS D'ORIGINE.
    ///
    /// HBA Delivery ne peut pas bâtir la course, et l'acheteur découvrirait le
    /// blocage APRÈS avoir payé. On refuse à l'ouverture plutôt qu'à la livraison :
    /// c'est le seul moment où le refus ne coûte rien à personne.
    /// </summary>
    [Fact]
    public void Ouvrir_sans_lieu_d_expedition_est_refuse()
    {
        var boutique = UnVendeur.BoutiqueBrouillon();

        var resultat = boutique.Open();

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.store.location_required");
        boutique.Status.Should().Be(StoreStatus.Draft);
    }

    [Fact]
    public void Une_boutique_avec_lieu_d_expedition_ouvre()
    {
        var boutique = UnVendeur.BoutiqueOuvrable();
        boutique.ClearDomainEvents();

        boutique.Open().IsSuccess.Should().BeTrue();

        boutique.Status.Should().Be(StoreStatus.Open);
        boutique.DomainEvents.Should().ContainItemsAssignableTo<StoreOpenedDomainEvent>();
    }

    [Fact]
    public void Ouvrir_deux_fois_reussit_sans_reemettre()
    {
        var boutique = UnVendeur.BoutiqueOuverte();
        boutique.ClearDomainEvents();

        boutique.Open().IsSuccess.Should().BeTrue();

        boutique.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Fermer_pour_conges_est_reversible()
    {
        var boutique = UnVendeur.BoutiqueOuverte();

        boutique.Close("congés annuels").IsSuccess.Should().BeTrue();
        boutique.Status.Should().Be(StoreStatus.Closed);
        boutique.StatusReason.Should().Be("congés annuels");

        boutique.Open().IsSuccess.Should().BeTrue();
        boutique.Status.Should().Be(StoreStatus.Open);
        boutique.StatusReason.Should().BeNull("la réouverture efface le motif de fermeture");
    }

    /// <summary>
    /// LA GARDE QUI EMPÊCHE UN VENDEUR D'ANNULER SA PROPRE SANCTION.
    ///
    /// Sans elle, la suspension durerait le temps d'un clic : le vendeur rouvre, et
    /// la décision de la plateforme est effacée sans que personne ne l'apprenne.
    /// </summary>
    [Fact]
    public void Une_boutique_suspendue_ne_se_rouvre_pas_depuis_l_espace_vendeur()
    {
        var boutique = UnVendeur.BoutiqueOuverte();
        boutique.Suspend("produits contrefaits");

        var resultat = boutique.Open();

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.store.suspended");
        boutique.Status.Should().Be(StoreStatus.Suspended);
    }

    /// <summary>
    /// LEVER LA SUSPENSION NE ROUVRE PAS LA BOUTIQUE, ET C'EST VOULU.
    ///
    /// La plateforme lève sa sanction ; elle ne décide pas à la place du vendeur
    /// qu'il est prêt à vendre. C'est lui qui rouvre, quand son stock et ses prix
    /// sont à jour — sinon on rouvrirait une vitrine périmée en son nom.
    /// </summary>
    [Fact]
    public void Lever_la_suspension_repasse_la_boutique_en_fermee_pas_en_ouverte()
    {
        var boutique = UnVendeur.BoutiqueOuverte();
        boutique.Suspend("produits contrefaits");

        boutique.LiftSuspension().IsSuccess.Should().BeTrue();

        boutique.Status.Should().Be(StoreStatus.Closed);
        boutique.StatusReason.Should().BeNull();

        boutique.Open().IsSuccess.Should().BeTrue("le vendeur reprend la main");
        boutique.Status.Should().Be(StoreStatus.Open);
    }

    [Fact]
    public void On_ne_leve_pas_la_suspension_d_une_boutique_qui_n_en_a_pas()
    {
        var boutique = UnVendeur.BoutiqueOuverte();

        var resultat = boutique.LiftSuspension();

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.store.not_suspended");
    }

    [Fact]
    public void Fermer_une_boutique_suspendue_ne_l_ecrase_pas()
    {
        var boutique = UnVendeur.BoutiqueOuverte();
        boutique.Suspend("produits contrefaits");

        boutique.Close("congés").IsSuccess.Should().BeTrue();

        boutique.Status.Should().Be(StoreStatus.Suspended,
            "sinon le vendeur transformerait sa sanction en fermeture volontaire, "
            + "qu'il peut lever lui-même");
        boutique.StatusReason.Should().Be("produits contrefaits");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Les événements de sanction — deux écarts fermés au lot 2
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// UNE SANCTION N'EST PAS DES CONGÉS, ET LE TYPE DE L'ÉVÉNEMENT LE DIT.
    ///
    /// Cet emplacement portait un test `Ecart_` : `Suspend` émettait
    /// `StoreClosedDomainEvent`, le MÊME type que la fermeture volontaire. Un
    /// consommateur qui recevait « boutique fermée » ne pouvait pas savoir s'il
    /// s'agissait d'un vendeur parti en congés ou d'une boutique écartée pour
    /// contrefaçon — le motif était bien transporté, mais c'est du texte libre :
    /// rien ne peut s'y brancher.
    ///
    /// Ce que cela empêchait : afficher « temporairement fermée, de retour
    /// bientôt » dans un cas et retirer la boutique des résultats dans l'autre.
    /// </summary>
    [Fact]
    public void Suspendre_emet_un_evenement_distinct_d_une_fermeture_volontaire()
    {
        var suspendue = UnVendeur.BoutiqueOuverte();
        suspendue.ClearDomainEvents();
        suspendue.Suspend("produits contrefaits");

        var fermee = UnVendeur.BoutiqueOuverte();
        fermee.ClearDomainEvents();
        fermee.Close("congés annuels");

        var sanction = suspendue.DomainEvents.OfType<StoreSuspendedDomainEvent>()
            .Should().ContainSingle().Subject;
        sanction.Reason.Should().Be("produits contrefaits");

        suspendue.DomainEvents.Should().NotContainItemsAssignableTo<StoreClosedDomainEvent>(
            "une sanction ne doit plus emprunter le type de la fermeture volontaire");

        fermee.DomainEvents.Should().ContainItemsAssignableTo<StoreClosedDomainEvent>();
        fermee.DomainEvents.Should().NotContainItemsAssignableTo<StoreSuspendedDomainEvent>();
    }

    /// <summary>
    /// LA LEVÉE DE SANCTION SE PROPAGE MAINTENANT.
    ///
    /// Cet emplacement portait un test `Ecart_` : `LiftSuspension` ne produisait
    /// rien. La boutique reste hors vente, donc rien d'urgent ne s'ensuivait —
    /// c'est pourquoi l'absence n'avait jamais gêné. Mais un service qui a mémorisé
    /// « cette boutique est sanctionnée », pour l'exclure d'un classement ou d'une
    /// mise en avant, ne l'apprenait jamais autrement qu'en relisant tout.
    /// </summary>
    [Fact]
    public void Lever_la_suspension_annonce_la_levee()
    {
        var boutique = UnVendeur.BoutiqueOuverte();
        boutique.Suspend("produits contrefaits");
        boutique.ClearDomainEvents();

        boutique.LiftSuspension().IsSuccess.Should().BeTrue();

        boutique.DomainEvents.OfType<StoreSuspensionLiftedDomainEvent>().Should().ContainSingle();
        boutique.Status.Should().Be(StoreStatus.Closed, "la levée ne rouvre toujours pas");
    }
}
