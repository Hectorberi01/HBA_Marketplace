using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Domain.Sellers.Events;

namespace HBA.Merchants.UnitTests;

/// <summary>LE CYCLE DE VIE DU VENDEUR — CINQ STATUTS, DEUX PARCOURS QUI SE CROISENT.</summary>
public sealed class SellerLifecycleTests
{
    // Inscription

    [Fact]
    public void Un_vendeur_neuf_est_en_attente_et_sans_dossier()
    {
        var vendeur = UnVendeur.Inscrit();

        vendeur.Status.Should().Be(SellerStatus.Pending);
        vendeur.KybStatus.Should().Be(KybStatus.NotStarted);
        vendeur.PayoutAccount.Should().BeNull();
        vendeur.KybDocuments.Should().BeEmpty();

        vendeur.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<SellerRegisteredDomainEvent>(
                "c'est cet événement qui greffe le rôle Seller au compte");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Une_boutique_sans_nom_est_refusee(string nom)
    {
        var resultat = Seller.Register(UnVendeur.Compte, nom, UnVendeur.Commission);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.seller.shop_name_required");
    }

    [Fact]
    public void Une_inscription_sans_compte_est_refusee()
    {
        var resultat = Seller.Register(Guid.Empty, UnVendeur.Boutique, UnVendeur.Commission);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.seller.user_required");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Un_taux_de_commission_hors_bornes_est_refuse(decimal taux)
    {
        var resultat = Seller.Register(UnVendeur.Compte, UnVendeur.Boutique, taux);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.seller.commission_invalid");
    }

    // Activation

    /// <summary>LES DEUX PRÉCONDITIONS DE L'ACTIVATION NE SONT PAS DE MÊME NATURE.</summary>
    [Fact]
    public void L_activation_exige_un_kyb_verifie()
    {
        var vendeur = UnVendeur.DossierDepose();
        vendeur.SetPayoutAccount(UnVendeur.Reversement());

        var resultat = vendeur.Activate();

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.seller.kyb_not_verified");
        vendeur.Status.Should().Be(SellerStatus.Pending);
    }

    [Fact]
    public void L_activation_exige_des_coordonnees_de_reversement()
    {
        var vendeur = UnVendeur.DossierDepose();
        vendeur.ApproveKyb();

        var resultat = vendeur.Activate();

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.seller.payout_required");
        vendeur.Status.Should().Be(SellerStatus.Pending);
    }

    [Fact]
    public void Un_vendeur_activable_devient_actif()
    {
        var vendeur = UnVendeur.Activable();

        vendeur.Activate().IsSuccess.Should().BeTrue();

        vendeur.Status.Should().Be(SellerStatus.Active);
        vendeur.DomainEvents.Should().ContainItemsAssignableTo<SellerActivatedDomainEvent>();
    }

    // Suspension

    /// <summary>LA SUSPENSION DOIT ÉMETTRE SON ÉVÉNEMENT, ET C'EST TOUT L'ENJEU.</summary>
    [Fact]
    public void Suspendre_un_vendeur_actif_emet_l_evenement_qui_retire_son_catalogue()
    {
        var vendeur = UnVendeur.Actif();
        vendeur.ClearDomainEvents();

        vendeur.Suspend("fraude signalée").IsSuccess.Should().BeTrue();

        vendeur.Status.Should().Be(SellerStatus.Suspended);

        var evenement = vendeur.DomainEvents.OfType<SellerSuspendedDomainEvent>().Should()
            .ContainSingle().Subject;
        evenement.Reason.Should().Be("fraude signalée");
    }

    /// <summary>
    /// Suspendre deux fois n'est pas une erreur — l'appelant a obtenu ce qu'il
    /// voulait — mais ne doit PAS réémettre : le catalogue est déjà retiré, et
    /// rejouer la suspension relancerait le travail pour rien.
    /// </summary>
    [Fact]
    public void Suspendre_deux_fois_reussit_sans_reemettre()
    {
        var vendeur = UnVendeur.Actif();
        vendeur.Suspend("fraude");
        vendeur.ClearDomainEvents();

        vendeur.Suspend("fraude").IsSuccess.Should().BeTrue();

        vendeur.DomainEvents.Should().BeEmpty();
    }

    /// <summary>LA GARDE QUI PROTÈGE LA TRACE D'UNE DÉCISION DU VENDEUR.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Un_compte_ferme_ne_peut_pas_etre_suspendu(bool avecDemandeDeReactivation)
    {
        var vendeur = UnVendeur.Actif();
        vendeur.RequestClosure();

        if (avecDemandeDeReactivation)
        {
            vendeur.RequestReactivation();
        }

        var attendu = avecDemandeDeReactivation
            ? SellerStatus.PendingReactivation
            : SellerStatus.Closed;

        var resultat = vendeur.Suspend("tentative");

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.seller.closed_cannot_suspend");
        vendeur.Status.Should().Be(attendu, "le statut d'origine doit survivre au refus");
    }

    [Fact]
    public void Lever_une_suspension_remet_le_vendeur_en_activite()
    {
        var vendeur = UnVendeur.Actif();
        vendeur.Suspend("erreur de modération");
        vendeur.ClearDomainEvents();

        vendeur.LiftSuspension().IsSuccess.Should().BeTrue();

        vendeur.Status.Should().Be(SellerStatus.Active);
        vendeur.DomainEvents.Should().ContainItemsAssignableTo<SellerSuspensionLiftedDomainEvent>();
    }

    [Fact]
    public void On_ne_leve_pas_une_suspension_qui_n_existe_pas()
    {
        var vendeur = UnVendeur.Actif();

        var resultat = vendeur.LiftSuspension();

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.seller.not_suspended");
    }

    /// <summary>LEVER UNE SUSPENSION REND LE COMPTE LÀ D'OÙ IL VIENT.</summary>
    [Fact]
    public void Lever_la_suspension_d_un_compte_jamais_active_le_rend_en_attente()
    {
        var vendeur = UnVendeur.Activable();
        vendeur.Status.Should().Be(SellerStatus.Pending);

        vendeur.Suspend("contrôle").IsSuccess.Should().BeTrue();
        vendeur.LiftSuspension().IsSuccess.Should().BeTrue();

        vendeur.Status.Should().Be(SellerStatus.Pending,
            "il n'a jamais été activé : la levée ne peut pas le faire à sa place");

        // Et l'activation reste possible, en annonçant enfin l'entrée en activité.
        vendeur.ClearDomainEvents();
        vendeur.Activate().IsSuccess.Should().BeTrue();
        vendeur.Status.Should().Be(SellerStatus.Active);
        vendeur.DomainEvents.Should().ContainItemsAssignableTo<SellerActivatedDomainEvent>();
    }

    [Fact]
    public void Lever_la_suspension_d_un_compte_actif_le_rend_actif()
    {
        var vendeur = UnVendeur.Actif();
        vendeur.Suspend("erreur de modération");

        vendeur.LiftSuspension().IsSuccess.Should().BeTrue();

        vendeur.Status.Should().Be(SellerStatus.Active);
        vendeur.SuspendedFromStatus.Should().BeNull("la valeur est effacée une fois consommée");
    }

    // Fermeture et réactivation

    [Fact]
    public void Fermer_son_compte_emet_l_evenement_qui_retire_les_produits()
    {
        var vendeur = UnVendeur.Actif();
        vendeur.ClearDomainEvents();

        vendeur.RequestClosure().IsSuccess.Should().BeTrue();

        vendeur.Status.Should().Be(SellerStatus.Closed);
        vendeur.DomainEvents.Should().ContainItemsAssignableTo<SellerClosedDomainEvent>();
    }

    [Fact]
    public void Fermer_deux_fois_est_refuse()
    {
        var vendeur = UnVendeur.Actif();
        vendeur.RequestClosure();

        var resultat = vendeur.RequestClosure();

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.seller.already_closed");
    }

    [Fact]
    public void Seul_un_compte_ferme_peut_demander_sa_reactivation()
    {
        var vendeur = UnVendeur.Actif();

        var resultat = vendeur.RequestReactivation();

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.seller.not_closed");
    }

    [Fact]
    public void Le_parcours_complet_de_fermeture_puis_retour()
    {
        var vendeur = UnVendeur.Actif();

        vendeur.RequestClosure().IsSuccess.Should().BeTrue();
        vendeur.RequestReactivation().IsSuccess.Should().BeTrue();
        vendeur.Status.Should().Be(SellerStatus.PendingReactivation);

        vendeur.ClearDomainEvents();
        vendeur.ApproveReactivation().IsSuccess.Should().BeTrue();

        vendeur.Status.Should().Be(SellerStatus.Active);
        vendeur.DomainEvents.Should().ContainItemsAssignableTo<SellerReactivatedDomainEvent>(
            "c'est lui qui permet au catalogue de reprendre le vendeur en compte");
    }

    /// <summary>LA RÉACTIVATION EXIGE UN COMPTE DE REVERSEMENT, COMME LES DEUX AUTRES.</summary>
    [Fact]
    public void La_reactivation_exige_un_compte_de_reversement()
    {
        var vendeur = UnVendeur.DossierDepose();
        vendeur.ApproveKyb().IsSuccess.Should().BeTrue();

        vendeur.RequestClosure().IsSuccess.Should().BeTrue();
        vendeur.RequestReactivation().IsSuccess.Should().BeTrue();

        var resultat = vendeur.ApproveReactivation();

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.seller.payout_required");
        vendeur.Status.Should().Be(SellerStatus.PendingReactivation,
            "le compte reste en attente tant que le versement n'est pas possible");
    }

    /// <summary>
    /// CE TEST S'APPELAIT `Ecart_un_compte_ferme_est_reactivable_sans_demande_
    /// prealable`, ET IL FIGEAIT LE CONTRAIRE DE CE QU'IL VÉRIFIE MAINTENANT.
    /// </summary>
    [Fact]
    public void Un_compte_ferme_n_est_pas_reactivable_sans_demande_prealable()
    {
        var vendeur = UnVendeur.Actif();
        vendeur.RequestClosure();
        vendeur.Status.Should().Be(SellerStatus.Closed);

        var resultat = vendeur.ApproveReactivation();

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.seller.no_reactivation_request");
        vendeur.Status.Should().Be(SellerStatus.Closed,
            "un refus ne doit rien changer à l'état du compte");
    }

    /// <summary>Le parcours complet, celui qui doit passer.</summary>
    [Fact]
    public void Un_compte_qui_a_demande_sa_reactivation_est_reactivable()
    {
        var vendeur = UnVendeur.Actif();
        vendeur.RequestClosure().IsSuccess.Should().BeTrue();
        vendeur.RequestReactivation().IsSuccess.Should().BeTrue();

        vendeur.ApproveReactivation().IsSuccess.Should().BeTrue();

        vendeur.Status.Should().Be(SellerStatus.Active);
    }

    [Fact]
    public void La_reactivation_exige_un_kyb_verifie()
    {
        var vendeur = UnVendeur.Inscrit();
        vendeur.RequestClosure().IsSuccess.Should().BeTrue();

        // LA DEMANDE D'ABORD : sans elle, l'échec viendrait de la garde de statut
        // et ce test ne dirait plus rien du KYB.
        vendeur.RequestReactivation().IsSuccess.Should().BeTrue();

        var resultat = vendeur.ApproveReactivation();

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.seller.kyb_not_verified");
    }

    // Suppression définitive

    /// <summary>CHAQUE PIÈCE D'IDENTITÉ DOIT ÊTRE NOMMÉE, UNE PAR UNE.</summary>
    [Fact]
    public void La_suppression_nomme_chaque_piece_a_effacer()
    {
        var vendeur = UnVendeur.Inscrit();
        vendeur.AddKybDocument(KybDocumentType.IdCard, Guid.NewGuid());
        vendeur.AddKybDocument(KybDocumentType.BusinessRegistry, Guid.NewGuid());
        vendeur.AddKybDocument(KybDocumentType.TaxId, Guid.NewGuid());
        vendeur.ClearDomainEvents();

        vendeur.MarkForDeletion().IsSuccess.Should().BeTrue();

        vendeur.DomainEvents.OfType<KybDocumentRemovedDomainEvent>().Should().HaveCount(3,
            "un événement par pièce : si l'effacement de l'une échoue durablement, les "
            + "autres partent quand même et le message en souffrance nomme le fichier qui résiste");

        vendeur.DomainEvents.Should().ContainItemsAssignableTo<SellerDeletedDomainEvent>();
    }

    // Compteurs alimentés par d'autres modules

    /// <summary>POSER LE TOTAL EST IDEMPOTENT, INCRÉMENTER NE L'EST PAS.</summary>
    [Fact]
    public void Poser_le_total_des_ventes_est_rejouable_sans_dommage()
    {
        var vendeur = UnVendeur.Actif();

        vendeur.SetSalesCount(42);
        vendeur.SetSalesCount(42);

        vendeur.SalesCount.Should().Be(42);
    }

    [Fact]
    public void Un_total_de_ventes_negatif_est_ramene_a_zero()
    {
        var vendeur = UnVendeur.Actif();

        vendeur.SetSalesCount(-5);

        vendeur.SalesCount.Should().Be(0);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(5.1)]
    public void Une_note_hors_bornes_est_refusee(decimal note)
    {
        var vendeur = UnVendeur.Actif();

        var resultat = vendeur.UpdateRating(note);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.seller.rating_invalid");
    }
}
