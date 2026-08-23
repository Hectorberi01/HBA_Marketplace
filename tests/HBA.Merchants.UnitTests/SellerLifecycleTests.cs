using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Domain.Sellers.Events;

namespace HBA.Merchants.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CYCLE DE VIE DU VENDEUR — CINQ STATUTS, DEUX PARCOURS QUI SE CROISENT.
///
/// CE FICHIER EST LE PREMIER TEST DE DOMAINE DE CE SERVICE.
///
/// `seller-service` compte 83 fichiers et 6 422 lignes. Jusqu'ici, sa seule
/// couverture était `HBA.Merchants.AuthorizationTests` — cinq tests, qui vérifient
/// QUI entre, jamais ce qui se passe ensuite.
///
/// Or l'agrégat porte deux machines à états imbriquées : le compte
/// (`Pending → Active → Suspended → Closed → PendingReactivation`) et le dossier
/// KYB (`NotStarted → InReview → Verified | Rejected`). Elles se contraignent
/// mutuellement — l'activation exige un KYB vérifié, un refus de KYB suspend un
/// compte actif — et personne n'avait jamais posé la question « peut-on suspendre
/// un vendeur déjà fermé ? » à autre chose qu'au code lui-même.
///
/// CE QUE CES TESTS FIXENT VOLONTAIREMENT, Y COMPRIS QUAND C'EST DISCUTABLE.
///
/// Le lot 1 a posé cinq tests préfixés `Ecart_` : des comportements testés TELS
/// QU'ILS ÉTAIENT, avec l'explication de ce qui clochait. Quatre ont été corrigés
/// au lot 2 et leurs tests décrivent maintenant la règle voulue — chacun garde
/// dans son encadré le rappel de ce qu'il y avait avant, pour qu'on ne le rouvre
/// pas par distraction.
///
/// Un seul subsiste ici : `Ecart_un_compte_ferme_est_reactivable_sans_demande_
/// prealable`. Il n'est pas nécessairement faux — un administrateur peut vouloir
/// rouvrir un compte fermé par erreur — mais le nom de la méthode et son message
/// d'erreur affirment le contraire. C'est au cahier de trancher, pas au test.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SellerLifecycleTests
{
    // ═════════════════════════════════════════════════════════════════════════
    // Inscription
    // ═════════════════════════════════════════════════════════════════════════

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

    // ═════════════════════════════════════════════════════════════════════════
    // Activation
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LES DEUX PRÉCONDITIONS DE L'ACTIVATION NE SONT PAS DE MÊME NATURE.
    ///
    /// Le KYB protège la PLATEFORME — elle doit savoir à qui elle ouvre une
    /// boutique. Le compte de reversement protège le VENDEUR : sans lui, il vend,
    /// accumule des gains, et rien ne peut les lui verser. La seconde est plus
    /// facile à oublier parce qu'elle ne bloque personne d'autre que lui.
    /// </summary>
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

    // ═════════════════════════════════════════════════════════════════════════
    // Suspension
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LA SUSPENSION DOIT ÉMETTRE SON ÉVÉNEMENT, ET C'EST TOUT L'ENJEU.
    ///
    /// L'encadré de `Suspend` raconte l'incident : la méthode posait le statut sans
    /// rien émettre. L'administrateur suspendait un vendeur frauduleux, voyait
    /// « Suspendu » dans sa console — et les acheteurs continuaient de commander et
    /// de payer quelqu'un que la plateforme venait d'écarter.
    ///
    /// C'est l'événement, pas le statut, qui retire le catalogue de la vente. Ce
    /// test porte donc sur l'événement.
    /// </summary>
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

    /// <summary>
    /// LA GARDE QUI PROTÈGE LA TRACE D'UNE DÉCISION DU VENDEUR.
    ///
    /// Sans elle, suspendre un compte FERMÉ écrasait `Closed` par `Suspended` : la
    /// trace de la demande du vendeur disparaissait, et la réactivation qu'il
    /// pouvait demander devenait inatteignable, `RequestReactivation` exigeant un
    /// compte fermé. Un clic d'administrateur enfermait le vendeur dehors.
    /// </summary>
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

    /// <summary>
    /// LEVER UNE SUSPENSION REND LE COMPTE LÀ D'OÙ IL VIENT.
    ///
    /// Cet emplacement portait un test `Ecart_` : `LiftSuspension` posait `Active`
    /// sans regarder ce que le compte était AVANT. Un vendeur encore `Pending` —
    /// jamais activé — suspendu puis rétabli arrivait donc en activité sans jamais
    /// passer par `Activate()`, donc sans que `SellerActivatedDomainEvent` ne soit
    /// émis. Tout consommateur qui attend l'activation pour agir — ouvrir un
    /// portefeuille, autoriser la mise en vente — ne voyait jamais passer ce
    /// vendeur.
    ///
    /// Il repart maintenant en `Pending` et devra passer par l'activation, qui
    /// annoncera son entrée en activité comme pour tout le monde.
    /// </summary>
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

    // ═════════════════════════════════════════════════════════════════════════
    // Fermeture et réactivation
    // ═════════════════════════════════════════════════════════════════════════

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

    /// <summary>
    /// LA RÉACTIVATION EXIGE UN COMPTE DE REVERSEMENT, COMME LES DEUX AUTRES.
    ///
    /// Cet emplacement portait un test `Ecart_`. Des trois chemins qui mènent à
    /// `Active`, `ApproveReactivation` était le seul à ne pas le vérifier — alors
    /// que `LiftSuspension` porte la raison écrite noir sur blanc : « un vendeur qui
    /// vend sans compte de reversement accumule des gains que rien ne peut lui
    /// verser ».
    ///
    /// Un compte fermé puis rétabli revendait donc sans que personne ne puisse le
    /// payer, et le problème ne se manifestait qu'au premier versement, des semaines
    /// plus tard, du côté de Wallet.
    /// </summary>
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
    ///    prealable`, ET IL FIGEAIT LE CONTRAIRE DE CE QU'IL VÉRIFIE MAINTENANT.
    ///
    /// La garde acceptait un compte simplement `Closed`, alors que le nom de la
    /// méthode et son code d'erreur — `no_reactivation_request` — affirmaient
    /// qu'une demande était requise. Le test décrivait donc fidèlement le code, et
    /// son préfixe `Ecart_` disait qu'on attendait un arbitrage plutôt qu'on ne
    /// validait la règle.
    ///
    /// L'arbitrage est rendu : c'est le NOM qui avait raison. Le parcours est
    /// `Closed → RequestReactivation → PendingReactivation → ApproveReactivation`.
    ///
    /// Ce qu'on perd, et qui est assumé : rouvrir un compte fermé PAR ERREUR n'a
    /// plus de chemin direct. Si l'exploitation en a besoin, ce sera un geste
    /// distinct, nommé pour ce qu'il fait.
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

        // LA DEMANDE D'ABORD : sans elle, l'échec viendrait de la garde de
        // statut et ce test ne dirait plus rien du KYB.
        vendeur.RequestReactivation().IsSuccess.Should().BeTrue();

        var resultat = vendeur.ApproveReactivation();

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.seller.kyb_not_verified");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Suppression définitive
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// CHAQUE PIÈCE D'IDENTITÉ DOIT ÊTRE NOMMÉE, UNE PAR UNE.
    ///
    /// Le retrait de l'agrégat emporte les lignes `kyb_documents` en cascade et
    /// LAISSE LES FICHIERS. Cartes d'identité, registres de commerce : sans un
    /// événement par pièce, ils resteraient dans le bucket privé sans qu'aucune
    /// ligne ne pointe vers eux — donc sans aucun moyen de les retrouver un jour
    /// pour les effacer.
    ///
    /// C'est une obligation de protection des données, pas une commodité.
    /// </summary>
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

    // ═════════════════════════════════════════════════════════════════════════
    // Compteurs alimentés par d'autres modules
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// POSER LE TOTAL EST IDEMPOTENT, INCRÉMENTER NE L'EST PAS.
    ///
    /// L'alimentation vient de Kafka, qui livre au moins une fois. `RecordSale`
    /// double-compterait au premier rejeu ; `SetSalesCount` recalculé depuis la
    /// source ne bouge pas. C'est le même raisonnement que l'inbox du §19.5, appliqué
    /// à un compteur.
    /// </summary>
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
