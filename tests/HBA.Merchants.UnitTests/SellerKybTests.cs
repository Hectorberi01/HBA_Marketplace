using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Domain.Sellers.Events;

namespace HBA.Merchants.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE DOSSIER KYB — L'AUTRE MACHINE À ÉTATS, ET CELLE QUI TOUCHE À DE L'IDENTITÉ.
///
/// CE QUI SE JOUE ICI N'EST PAS UN STATUT, C'EST UN RÉTABLISSEMENT.
///
/// Un refus sans motif laisse le vendeur devant le mot « Rejeté » sans savoir quoi
/// corriger : il redépose la même pièce, la modération la refuse à nouveau, et les
/// deux s'épuisent. Le motif, sa conservation et son EFFACEMENT au nouveau dépôt
/// sont donc des règles de fond, pas du confort d'affichage.
///
/// ET LE REFUS DOIT AVOIR UNE CONSÉQUENCE SUR L'ACTIVITÉ.
///
/// L'entrée était gardée — `Activate()` refuse sans KYB vérifié — mais pas la
/// sortie. Un vendeur déjà actif dont la modération rejetait le dossier restait
/// `Active` et continuait de vendre : le rejet ne changeait qu'une colonne que
/// personne ne relisait.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SellerKybTests
{
    [Fact]
    public void Deposer_une_premiere_piece_met_le_dossier_en_revue()
    {
        var vendeur = UnVendeur.Inscrit();

        vendeur.AddKybDocument(KybDocumentType.IdCard, Guid.NewGuid()).IsSuccess.Should().BeTrue();

        vendeur.KybStatus.Should().Be(KybStatus.InReview);
        vendeur.KybDocuments.Should().ContainSingle();
    }

    [Fact]
    public void Une_piece_sans_media_est_refusee()
    {
        var vendeur = UnVendeur.Inscrit();

        var resultat = vendeur.AddKybDocument(KybDocumentType.IdCard, Guid.Empty);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.kyb.file_required");
        vendeur.KybStatus.Should().Be(KybStatus.NotStarted);
    }

    /// <summary>
    /// AJOUTER UNE PIÈCE À UN DOSSIER DÉJÀ VÉRIFIÉ NE LE DÉ-VÉRIFIE PAS.
    ///
    /// C'est un choix, et il se lit dans le sens du commerce : un renouvellement de
    /// pièce ne doit pas interrompre l'activité d'un vendeur en règle. L'admin peut
    /// revalider s'il le souhaite ; le vendeur continue de vendre pendant ce temps.
    ///
    /// Le comportement inverse — repasser en revue à chaque dépôt — serait
    /// défendable, mais il ferait tomber en suspension tout vendeur qui met à jour
    /// un document expirant, c'est-à-dire tous, une fois par an.
    /// </summary>
    [Fact]
    public void Une_piece_ajoutee_a_un_dossier_verifie_ne_le_remet_pas_en_revue()
    {
        var vendeur = UnVendeur.Actif();
        vendeur.KybStatus.Should().Be(KybStatus.Verified);

        vendeur.AddKybDocument(KybDocumentType.ProofOfAddress, Guid.NewGuid()).IsSuccess.Should().BeTrue();

        vendeur.KybStatus.Should().Be(KybStatus.Verified);
        vendeur.Status.Should().Be(SellerStatus.Active, "l'activité ne s'interrompt pas");
    }

    [Fact]
    public void Valider_un_dossier_sans_piece_est_refuse()
    {
        var vendeur = UnVendeur.Inscrit();

        var resultat = vendeur.ApproveKyb();

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.kyb.no_documents");
    }

    [Fact]
    public void Valider_le_dossier_marque_chaque_piece_verifiee()
    {
        var vendeur = UnVendeur.Inscrit();
        vendeur.AddKybDocument(KybDocumentType.IdCard, Guid.NewGuid());
        vendeur.AddKybDocument(KybDocumentType.BusinessRegistry, Guid.NewGuid());
        vendeur.ClearDomainEvents();

        vendeur.ApproveKyb().IsSuccess.Should().BeTrue();

        vendeur.KybStatus.Should().Be(KybStatus.Verified);
        vendeur.KybDocuments.Should().OnlyContain(d => d.VerifiedAtUtc != null);
        vendeur.DomainEvents.Should().ContainItemsAssignableTo<SellerKybVerifiedDomainEvent>();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Refus
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Rejeter_un_dossier_jamais_depose_est_refuse()
    {
        var vendeur = UnVendeur.Inscrit();

        var resultat = vendeur.RejectKyb("pièce illisible");

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.kyb.nothing_to_reject");
    }

    [Fact]
    public void Le_motif_du_refus_est_conserve_et_transporte_par_l_evenement()
    {
        var vendeur = UnVendeur.DossierDepose();
        vendeur.ClearDomainEvents();

        vendeur.RejectKyb("La carte d'identité est expirée.").IsSuccess.Should().BeTrue();

        vendeur.KybStatus.Should().Be(KybStatus.Rejected);
        vendeur.KybRejectionReason.Should().Be("La carte d'identité est expirée.");

        var evenement = vendeur.DomainEvents.OfType<SellerKybRejectedDomainEvent>()
            .Should().ContainSingle().Subject;
        evenement.Reason.Should().Be("La carte d'identité est expirée.");
    }

    /// <summary>
    /// LE CŒUR DU CORRECTIF DÉCRIT DANS `RejectKyb` : LE REFUS SUSPEND.
    ///
    /// Sans cela, un vendeur déjà actif dont la modération rejette le dossier —
    /// pièce expirée, document falsifié — restait `Active` et continuait de vendre.
    /// La suspension emprunte le chemin de l'exploitation : le même événement, donc
    /// le même retrait du catalogue.
    /// </summary>
    [Fact]
    public void Rejeter_le_dossier_d_un_vendeur_actif_le_suspend()
    {
        var vendeur = UnVendeur.Actif();
        vendeur.ClearDomainEvents();

        vendeur.RejectKyb("document falsifié").IsSuccess.Should().BeTrue();

        vendeur.Status.Should().Be(SellerStatus.Suspended);

        var suspension = vendeur.DomainEvents.OfType<SellerSuspendedDomainEvent>()
            .Should().ContainSingle().Subject;
        suspension.Reason.Should().Be("dossier KYB rejeté : document falsifié",
            "le motif du refus doit se lire sur la suspension, sinon le vendeur voit "
            + "une sanction sans cause");
    }

    [Fact]
    public void Un_refus_sans_motif_donne_une_suspension_au_motif_generique()
    {
        var vendeur = UnVendeur.Actif();
        vendeur.ClearDomainEvents();

        vendeur.RejectKyb().IsSuccess.Should().BeTrue();

        vendeur.KybRejectionReason.Should().BeNull();
        vendeur.DomainEvents.OfType<SellerSuspendedDomainEvent>().Should().ContainSingle()
            .Which.Reason.Should().Be("dossier KYB rejeté");
    }

    /// <summary>
    /// ON NE TOUCHE PAS AUX COMPTES FERMÉS : ILS NE VENDENT DÉJÀ PLUS.
    ///
    /// Écraser leur statut par `Suspended` coûterait la raison de leur état — et
    /// rendrait leur réactivation inatteignable, `RequestReactivation` exigeant un
    /// compte fermé.
    /// </summary>
    [Fact]
    public void Rejeter_le_dossier_d_un_compte_ferme_ne_change_pas_son_statut()
    {
        var vendeur = UnVendeur.Actif();
        vendeur.RequestClosure();

        vendeur.RejectKyb("contrôle a posteriori").IsSuccess.Should().BeTrue();

        vendeur.KybStatus.Should().Be(KybStatus.Rejected);
        vendeur.Status.Should().Be(SellerStatus.Closed, "un compte fermé le reste");
    }

    [Fact]
    public void Rejeter_deux_fois_est_idempotent_et_ne_reemet_pas()
    {
        var vendeur = UnVendeur.DossierDepose();
        vendeur.RejectKyb("pièce illisible");
        vendeur.ClearDomainEvents();

        vendeur.RejectKyb("pièce illisible").IsSuccess.Should().BeTrue();

        vendeur.DomainEvents.Should().BeEmpty(
            "réémettre relancerait notification et suspension déjà faites");
    }

    /// <summary>
    /// LE MOTIF DU REFUS PRÉCÉDENT NE SURVIT PAS AU NOUVEAU DÉPÔT.
    ///
    /// Affiché sur un dossier de nouveau en revue, il ferait croire au vendeur que
    /// sa correction a déjà été refusée — c'est-à-dire exactement le contraire de ce
    /// qui vient de se passer.
    /// </summary>
    [Fact]
    public void Redeposer_apres_un_refus_efface_le_motif_et_remet_en_revue()
    {
        var vendeur = UnVendeur.DossierDepose();
        vendeur.RejectKyb("La carte d'identité est expirée.");

        vendeur.AddKybDocument(KybDocumentType.IdCard, Guid.NewGuid()).IsSuccess.Should().BeTrue();

        vendeur.KybStatus.Should().Be(KybStatus.InReview);
        vendeur.KybRejectionReason.Should().BeNull();
    }

    [Fact]
    public void Valider_le_dossier_efface_un_motif_de_refus_anterieur()
    {
        var vendeur = UnVendeur.DossierDepose();
        vendeur.RejectKyb("pièce illisible");
        vendeur.AddKybDocument(KybDocumentType.IdCard, Guid.NewGuid());

        vendeur.ApproveKyb().IsSuccess.Should().BeTrue();

        vendeur.KybRejectionReason.Should().BeNull();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Retrait d'une pièce
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// RETIRER LA LIGNE SANS PRÉVENIR PERSONNE LAISSERAIT LA PIÈCE D'IDENTITÉ
    ///    DANS LE BUCKET PRIVÉ, POUR TOUJOURS.
    ///
    /// Sans que rien ne la désigne — donc sans que le ménage de rétention puisse
    /// jamais la trouver. L'événement porte le `MediaId` jusqu'au composition root,
    /// qui seul connaît le service média.
    /// </summary>
    [Fact]
    public void Retirer_une_piece_annonce_le_fichier_a_effacer()
    {
        var vendeur = UnVendeur.Inscrit();
        var mediaId = Guid.NewGuid();
        var piece = vendeur.AddKybDocument(KybDocumentType.IdCard, mediaId).Value;
        vendeur.ClearDomainEvents();

        vendeur.RemoveKybDocument(piece.Id).IsSuccess.Should().BeTrue();

        vendeur.KybDocuments.Should().BeEmpty();
        vendeur.DomainEvents.OfType<KybDocumentRemovedDomainEvent>().Should().ContainSingle()
            .Which.MediaId.Should().Be(mediaId);
    }

    [Fact]
    public void Retirer_une_piece_inconnue_est_refuse()
    {
        var vendeur = UnVendeur.DossierDepose();

        var resultat = vendeur.RemoveKybDocument(Guid.NewGuid());

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.kyb.not_found");
    }

    /// <summary>
    /// RETIRER SA DERNIÈRE PIÈCE RAMÈNE LE DOSSIER À « NON COMMENCÉ ».
    ///
    /// Cet emplacement portait un test `Ecart_` : un vendeur qui déposait une pièce
    /// puis la retirait laissait un dossier `InReview` SANS AUCUNE PIÈCE. Il
    /// occupait la file d'un administrateur qui, en l'ouvrant, ne trouvait rien à
    /// regarder — et ne pouvait même pas le rejeter utilement, `RejectKyb` supposant
    /// qu'il y a eu quelque chose à examiner.
    /// </summary>
    [Fact]
    public void Retirer_la_derniere_piece_sort_le_dossier_de_la_file_de_validation()
    {
        var vendeur = UnVendeur.Inscrit();
        var piece = vendeur.AddKybDocument(KybDocumentType.IdCard, Guid.NewGuid()).Value;

        vendeur.RemoveKybDocument(piece.Id).IsSuccess.Should().BeTrue();

        vendeur.KybDocuments.Should().BeEmpty();
        vendeur.KybStatus.Should().Be(KybStatus.NotStarted);
    }

    /// <summary>
    /// ON NE DÉFAIT PAS LA DÉCISION D'UN ADMINISTRATEUR.
    ///
    /// Le retour à `NotStarted` ne concerne QUE les dossiers en revue. Un dossier
    /// VÉRIFIÉ dont on retire la dernière pièce reste vérifié : la décision a été
    /// prise, et la défaire au retrait d'un document interromprait l'activité d'un
    /// vendeur en règle.
    /// </summary>
    [Fact]
    public void Retirer_la_derniere_piece_d_un_dossier_verifie_ne_le_devalide_pas()
    {
        var vendeur = UnVendeur.Actif();
        var piece = vendeur.KybDocuments.First();

        vendeur.RemoveKybDocument(piece.Id).IsSuccess.Should().BeTrue();

        vendeur.KybDocuments.Should().BeEmpty();
        vendeur.KybStatus.Should().Be(KybStatus.Verified);
        vendeur.Status.Should().Be(SellerStatus.Active);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // La soumission explicite (§10.3) — ajoutée au lot 2
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// SOUMETTRE EST UN GESTE, PAS UN EFFET DE BORD.
    ///
    /// C'est le vendeur qui déclare avoir fini. La distinction sépare une file
    /// d'administrateurs remplie de dossiers exploitables d'une file remplie de
    /// dossiers en cours de constitution.
    ///
    /// POURQUOI CE TEST PASSE PAR UN REFUS POUR OBSERVER LA SOUMISSION.
    ///
    /// Tant que la bascule automatique dépréciée vit, elle DEVANCE le geste
    /// explicite : le dépôt de la première pièce met déjà le dossier en revue, et
    /// `SubmitKyb` n'a plus rien à faire — il rend succès sans émettre, ce qui est
    /// le bon comportement idempotent.
    ///
    /// Le seul chemin où la soumission agit vraiment est donc aujourd'hui la
    /// CORRECTION après refus. C'est une conséquence directe du choix de ne pas
    /// casser l'app déployée, et elle mérite d'être écrite : le jour où la bascule
    /// sera retirée, ce test redeviendra le cas nominal.
    ///
    /// La première version de ce test montait un dossier neuf et attendait
    /// l'événement. Il a échoué au premier `make test`, et il avait tort : c'est le
    /// test qui ignorait la bascule, pas le code qui se trompait.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public void Soumettre_met_le_dossier_en_revue_et_annonce_le_nombre_de_pieces()
    {
        var vendeur = UnVendeur.Inscrit();
        vendeur.AddKybDocument(KybDocumentType.IdCard, Guid.NewGuid());
        vendeur.AddKybDocument(KybDocumentType.BusinessRegistry, Guid.NewGuid());

        // Le refus est ce qui sort le dossier de la revue sans vider ses pièces.
        vendeur.RejectKyb("La carte d'identité est expirée.").IsSuccess.Should().BeTrue();
        vendeur.ClearDomainEvents();

        vendeur.SubmitKyb().IsSuccess.Should().BeTrue();

        vendeur.KybStatus.Should().Be(KybStatus.InReview);
        vendeur.DomainEvents.OfType<SellerKybSubmittedDomainEvent>().Should().ContainSingle()
            .Which.DocumentCount.Should().Be(2,
                "l'administrateur doit savoir ce qui l'attend avant d'ouvrir le dossier");
    }

    [Fact]
    public void Soumettre_un_dossier_vide_est_refuse()
    {
        var vendeur = UnVendeur.Inscrit();

        var resultat = vendeur.SubmitKyb();

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("sellers.kyb.no_documents");
    }

    /// <summary>
    /// Idempotent : l'app pourra appeler « soumettre » après chaque dépôt sans avoir
    /// à savoir si c'est la première pièce. Réémettre, en revanche, relancerait une
    /// notification à l'administrateur pour un dossier qu'il a déjà dans sa file.
    /// </summary>
    [Fact]
    public void Soumettre_deux_fois_reussit_sans_reemettre()
    {
        var vendeur = UnVendeur.DossierDepose();
        vendeur.ClearDomainEvents();

        vendeur.SubmitKyb().IsSuccess.Should().BeTrue();

        vendeur.DomainEvents.Should().BeEmpty("le dossier était déjà en revue");
    }

    /// <summary>
    /// ON NE DÉ-VÉRIFIE PAS UN DOSSIER EN RÈGLE.
    ///
    /// Un vendeur qui renouvelle une pièce puis appelle « soumettre » ne doit pas
    /// retomber en attente de validation : il vend, et l'interrompre pour une mise à
    /// jour de routine coûterait plus que ça ne protège.
    /// </summary>
    [Fact]
    public void Soumettre_un_dossier_deja_verifie_ne_le_remet_pas_en_revue()
    {
        var vendeur = UnVendeur.Actif();

        vendeur.SubmitKyb().IsSuccess.Should().BeTrue();

        vendeur.KybStatus.Should().Be(KybStatus.Verified);
        vendeur.Status.Should().Be(SellerStatus.Active);
    }

    [Fact]
    public void Soumettre_apres_un_refus_relance_la_validation_et_efface_le_motif()
    {
        var vendeur = UnVendeur.DossierDepose();
        vendeur.RejectKyb("La carte d'identité est expirée.");
        vendeur.ClearDomainEvents();

        vendeur.SubmitKyb().IsSuccess.Should().BeTrue();

        vendeur.KybStatus.Should().Be(KybStatus.InReview);
        vendeur.KybRejectionReason.Should().BeNull();
        vendeur.DomainEvents.Should().ContainItemsAssignableTo<SellerKybSubmittedDomainEvent>();
    }

    /// <summary>
    /// LA BASCULE AUTOMATIQUE VIT ENCORE, ET CE TEST LA GARDE VIVANTE.
    ///
    /// L'application vendeur déjà déployée n'appelle pas « soumettre » : elle
    /// téléverse, et c'est tout. Retirer la bascule aujourd'hui ferait que plus aucun
    /// dossier n'atteindrait la file de validation — l'onboarding s'arrêterait net,
    /// sans erreur ni trace.
    ///
    /// Ce test tombera le jour où quelqu'un retirera la bascule. Qu'il vérifie
    /// d'abord que l'app envoie bien la soumission : c'est la condition écrite dans
    /// l'encadré de `Seller.AddKybDocument`.
    /// </summary>
    [Fact]
    public void La_bascule_automatique_au_premier_depot_fonctionne_encore()
    {
        var vendeur = UnVendeur.Inscrit();
        vendeur.ClearDomainEvents();

        vendeur.AddKybDocument(KybDocumentType.IdCard, Guid.NewGuid()).IsSuccess.Should().BeTrue();

        vendeur.KybStatus.Should().Be(KybStatus.InReview);
        vendeur.DomainEvents.Should().ContainItemsAssignableTo<SellerKybSubmittedDomainEvent>(
            "la bascule dépréciée doit annoncer la soumission comme le geste explicite");
    }
}
