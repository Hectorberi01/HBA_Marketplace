using HBA.Delivery.Driver.Domain.Enums;
using HBA.Delivery.Driver.Domain.Policies;
using Dossier = HBA.Delivery.Driver.Domain.Aggregates.DriverAccount;

namespace HBA.Delivery.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LOT 5.2 — LE DOSSIER DU LIVREUR (driver-service), ISSUE-029.
///
/// CE QUI ÉTAIT CASSÉ : `DriverStore` naissait avec UN livreur, identifiant
/// codé en dur, déjà « ACTIVE » et « VERIFIED ». Les six routes `/me` opéraient
/// toutes dessus — tous les livreurs étaient le même livreur — et aucun code du
/// service ne savait ce qu'était une pièce justificative. « Vérifié » ne
/// désignait rien.
///
/// L'ALIAS `Dossier` EST OBLIGATOIRE, PAS COSMÉTIQUE. L'espace de noms de ce
/// projet est `HBA.Delivery.UnitTests` ; écrire `Driver...` ici ferait résoudre
/// vers l'espace de noms `HBA.Delivery.Driver`, pas vers un type. C'est la même
/// collision que `UneCourse` contourne avec l'alias `Course`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DossierLivreurTests
{
    [Fact]
    public void Un_dossier_naissant_n_est_pas_dispatchable()
    {
        var dossier = UnDossier();

        dossier.VerificationStatus.Should().Be(DriverVerificationStatus.PendingDocuments);
        dossier.IsDispatchable.Should().BeFalse(
            "l'inscription ne prouve rien : n'importe quel compte peut se déclarer livreur");
    }

    /// <summary>
    /// ON NE PEUT PAS VÉRIFIER UN DOSSIER QUI N'A PAS ÉTÉ SOUMIS. Sans cette
    /// garde, l'exploitation pourrait valider d'un clic un dossier sans aucune
    /// pièce — ce que faisait la maquette, qui naissait vérifiée.
    /// </summary>
    [Fact]
    public void Un_dossier_incomplet_ne_peut_etre_ni_soumis_ni_verifie()
    {
        var dossier = UnDossier();

        var soumission = dossier.SubmitForReview();
        soumission.IsFailure.Should().BeTrue();
        soumission.Error.Code.Should().Be("driver.documents_incomplete");

        var verification = dossier.Verify();
        verification.IsFailure.Should().BeTrue();
        verification.Error.Code.Should().Be("driver.not_submitted");
        dossier.IsDispatchable.Should().BeFalse();
    }

    /// <summary>
    /// Le véhicule est exigé AVANT la vérification : la projection dispatchable de
    /// delivery-service se crée avec un véhicule, et un dossier vérifié sans
    /// véhicule produirait un livreur que le dispatch ne saurait pas classer.
    /// </summary>
    [Fact]
    public void Un_dossier_complet_mais_sans_vehicule_n_est_pas_soumis()
    {
        var dossier = UnDossier();
        DeposerLesPiecesObligatoires(dossier);

        var soumission = dossier.SubmitForReview();

        soumission.IsFailure.Should().BeTrue();
        soumission.Error.Code.Should().Be("driver.vehicle_required");
    }

    [Fact]
    public void Un_dossier_complet_soumis_puis_verifie_devient_dispatchable()
    {
        var dossier = UnDossierPret();

        dossier.SubmitForReview().IsSuccess.Should().BeTrue();
        dossier.VerificationStatus.Should().Be(DriverVerificationStatus.UnderReview);
        dossier.IsDispatchable.Should().BeFalse("être regardé n'est pas être autorisé");

        dossier.Verify().IsSuccess.Should().BeTrue();
        dossier.IsDispatchable.Should().BeTrue();
        dossier.Documents.Should().OnlyContain(piece => piece.Status == DriverDocumentStatus.Approved);
    }

    /// <summary>
    /// REDÉPOSER UNE PIÈCE APRÈS VÉRIFICATION ROUVRE LE DOSSIER. Le laisser
    /// « vérifié » signifierait que la plateforme a validé une pièce que personne
    /// n'a regardée — un permis expiré remplacé par n'importe quel fichier.
    /// </summary>
    [Fact]
    public void Redeposer_une_piece_apres_verification_rouvre_le_dossier()
    {
        var dossier = UnDossierPret();
        dossier.SubmitForReview().IsSuccess.Should().BeTrue();
        dossier.Verify().IsSuccess.Should().BeTrue();

        dossier.SubmitDocument(DriverDocumentType.DrivingLicence, "media/permis-v2").IsSuccess.Should().BeTrue();

        dossier.VerificationStatus.Should().Be(DriverVerificationStatus.PendingDocuments);
        dossier.IsDispatchable.Should().BeFalse();
    }

    /// <summary>
    /// Une pièce redéposée REMPLACE la précédente. Empiler les versions ferait
    /// valider par le vérificateur l'une des deux au hasard.
    /// </summary>
    [Fact]
    public void Une_piece_redeposee_remplace_la_precedente()
    {
        var dossier = UnDossier();

        dossier.SubmitDocument(DriverDocumentType.IdentityCard, "media/cni-v1").IsSuccess.Should().BeTrue();
        dossier.SubmitDocument(DriverDocumentType.IdentityCard, "media/cni-v2").IsSuccess.Should().BeTrue();

        dossier.Documents.Should().ContainSingle(piece => piece.Type == DriverDocumentType.IdentityCard);
        dossier.Documents.Single().ObjectKey.Should().Be("media/cni-v2");
    }

    /// <summary>
    /// LA SUSPENSION NE SE LÈVE PAS TOUTE SEULE : un livreur écarté ne redevient
    /// pas dispatchable en redéposant une pièce. C'est l'incident que la
    /// séparation statut / disponibilité existe pour éviter.
    /// </summary>
    [Fact]
    public void Un_dossier_suspendu_n_est_plus_dispatchable_et_refuse_les_depots()
    {
        var dossier = UnDossierPret();
        dossier.SubmitForReview().IsSuccess.Should().BeTrue();
        dossier.Verify().IsSuccess.Should().BeTrue();

        dossier.Suspend("Colis non remis").IsSuccess.Should().BeTrue();

        dossier.IsDispatchable.Should().BeFalse();
        dossier.StatusReason.Should().Be("Colis non remis");
        dossier.SubmitDocument(DriverDocumentType.IdentityCard, "media/cni-v3").IsFailure.Should().BeTrue();
        dossier.SubmitForReview().IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// L'IDENTITÉ VIENT DU COMPTE, ET LA SIGNATURE L'EXIGE EN PREMIER. Un
    /// dossier sans compte n'est pas ouvrable : c'est ce qui rend impossible, par
    /// construction, l'inscription au nom d'un autre.
    /// </summary>
    [Fact]
    public void Un_dossier_sans_compte_n_est_pas_ouvrable()
    {
        var refuse = Dossier.Register(Guid.Empty, "Kossi Adjovi", "+2290197000042");

        refuse.IsFailure.Should().BeTrue();
        refuse.Error.Code.Should().Be("driver.user_required");
    }

    /// <summary>
    /// Le numéro est NORMALISÉ, pas seulement accepté : c'est par lui que le
    /// client rappelle son livreur, et deux formes du même numéro rendraient les
    /// doublons indétectables.
    /// </summary>
    [Fact]
    public void Le_numero_est_normalise_a_l_inscription()
    {
        var dossier = Dossier.Register(Guid.NewGuid(), "  Kossi Adjovi  ", "01 97 00 00 42");

        dossier.IsSuccess.Should().BeTrue();
        dossier.Value.Phone.Should().Be("+2290197000042");
        dossier.Value.FullName.Should().Be("Kossi Adjovi");
    }

    /// <summary>
    /// UN NUMÉRO À HUIT CHIFFRES EST REFUSÉ, et ce n'est pas de la rigueur
    /// gratuite : c'est un numéro d'avant la migration béninoise de 2024, il
    /// n'aboutit plus. L'accepter reviendrait à inscrire un livreur que ni le
    /// client ni l'exploitation ne pourraient joindre.
    /// </summary>
    [Fact]
    public void Un_numero_illisible_est_refuse_sans_exception()
    {
        var refuse = Dossier.Register(Guid.NewGuid(), "Kossi Adjovi", "97000042");

        refuse.IsFailure.Should().BeTrue("un refus métier ne passe jamais par une exception");
        refuse.Error.Code.Should().Be("driver.phone_invalid");
    }

    /// <summary>
    /// LA PLAQUE N'EST EXIGÉE QUE DES VÉHICULES QUI EN PORTENT UNE. L'exiger de
    /// tous rendrait le vélo indéclarable, donc ses courses inattribuables — et la
    /// parade évidente, saisir « N/A », remplirait la base de fausses plaques.
    /// </summary>
    [Fact]
    public void La_plaque_est_exigee_de_la_moto_et_pas_du_velo()
    {
        var dossier = UnDossier();

        dossier.DeclareVehicle(DriverVehicleType.Motorcycle, null, null, null, 25m)
            .IsFailure.Should().BeTrue();

        dossier.DeclareVehicle(DriverVehicleType.Bicycle, null, null, null, 10m)
            .IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Un seul véhicule actif : laisser deux véhicules actifs ferait dépendre la
    /// capacité de charge retenue de l'ordre d'énumération.
    /// </summary>
    [Fact]
    public void Declarer_un_vehicule_desactive_le_precedent()
    {
        var dossier = UnDossier();

        dossier.DeclareVehicle(DriverVehicleType.Motorcycle, null, null, "ab-1234", 25m)
            .IsSuccess.Should().BeTrue();
        dossier.DeclareVehicle(DriverVehicleType.Tricycle, null, null, "cd-5678", 150m)
            .IsSuccess.Should().BeTrue();

        dossier.Vehicles.Count(vehicule => vehicule.Active).Should().Be(1);
        dossier.ActiveVehicle!.Type.Should().Be(DriverVehicleType.Tricycle);
        dossier.ActiveVehicle.Plate.Should().Be("CD-5678", "la plaque est normalisée");
    }

    /// <summary>
    /// La liste des pièces manquantes est rendue au livreur : sans elle, l'écran ne
    /// peut afficher que « dossier incomplet » et le livreur redépose au hasard.
    /// </summary>
    [Fact]
    public void Les_pieces_manquantes_sont_nommees()
    {
        var dossier = UnDossier();
        dossier.SubmitDocument(DriverDocumentType.IdentityCard, "media/cni").IsSuccess.Should().BeTrue();

        var manquantes = DriverDocumentPolicy.MissingRequired(dossier.Documents.Select(piece => piece.Type));

        manquantes.Should().Contain(nameof(DriverDocumentType.DrivingLicence));
        manquantes.Should().Contain(nameof(DriverDocumentType.ProfilePhoto));
        manquantes.Should().NotContain(nameof(DriverDocumentType.IdentityCard));
    }

    // ── Fabriques ───────────────────────────────────────────────────────────

    private static Dossier UnDossier()
    {
        var dossier = Dossier.Register(Guid.NewGuid(), "Kossi Adjovi", "+2290197000042");
        dossier.IsSuccess.Should().BeTrue("la fabrique de test doit produire un dossier valide");
        return dossier.Value;
    }

    private static Dossier UnDossierPret()
    {
        var dossier = UnDossier();
        DeposerLesPiecesObligatoires(dossier);
        dossier.DeclareVehicle(DriverVehicleType.Motorcycle, "Bajaj", "Boxer", "AB-1234", 25m)
            .IsSuccess.Should().BeTrue();
        return dossier;
    }

    private static void DeposerLesPiecesObligatoires(Dossier dossier)
    {
        foreach (var type in DriverDocumentPolicy.Required)
        {
            dossier.SubmitDocument(type, "media/" + type).IsSuccess.Should().BeTrue();
        }
    }
}
