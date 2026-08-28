using HBA.Deliveries.Domain.Deliveries;

namespace HBA.Delivery.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA PREUVE DE REMISE — RÉÉCRIT LE 28 AOÛT CONTRE L'AGRÉGAT QUI LA PORTE.
///
/// CE FICHIER ÉPROUVAIT `ProofStore`, ET CE MAGASIN N'EXISTE PLUS (D43).
///
/// `proof-of-delivery-service` tenait la preuve dans un `ConcurrentDictionary` de
/// processus : sans base, sans migration, perdu au redémarrage, non partagé entre
/// réplicas — et sans aucun appelant dans tout le dépôt. Il a été retiré parce que
/// `delivery-service` porte la MÊME capacité, persistée.
///
/// CE FICHIER EST DONC LA VÉRIFICATION DE CETTE AFFIRMATION. Chacun des tests
/// ci-dessous reprend une règle que les anciens éprouvaient sur la maquette, et
/// l'éprouve sur `Delivery` / `ProofOfDelivery`. Si l'une d'elles manquait ici, le
/// retrait aurait emporté une garantie — c'est exactement ce qu'un retrait de
/// service doit prouver, et ce qu'aucun contrôle statique ne peut dire.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUI A CHANGÉ DE FORME EN CHANGEANT DE PORTEUR, ET IL FAUT LE SAVOIR.
///
///   • LE CODE N'EXPIRE PLUS TOUT SEUL. `ProofStore` posait une échéance de
///     quinze minutes sur l'OTP, et deux tests l'éprouvaient. `Delivery.IssuedPin`
///     n'a AUCUNE échéance : le code émis à la prise en charge reste valable
///     jusqu'à la remise. Ce n'est pas un oubli de ce fichier, c'est une
///     différence RÉELLE entre les deux implémentations — et le retrait l'a
///     rendue effective sans que personne ne la décide. Les deux tests
///     correspondants ne sont donc pas portés : ils échoueraient, et à juste
///     titre.
///
///   • LA SIMULTANÉITÉ N'EST PLUS TESTABLE ICI. « Deux soumissions simultanées du
///     bon code, une seule vérifie » s'éprouvait sur un `ConcurrentDictionary`.
///     Sur un agrégat persisté, ce qui arbitre est le jeton `xmin` et une seconde
///     transaction — donc une base. Non porté, et non couvert ailleurs.
///
///   • LE VERROU EST À CINQ TENTATIVES, PAS TROIS. `MaxFailedProofAttempts = 5`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class PreuveDeRemiseTests
{
    /// <summary>
    /// Amène une course jusqu'au seuil de la remise, avec un livreur engagé.
    /// On passe par toutes les transitions : un état fabriqué à la main serait un
    /// état que la production ne peut pas produire.
    /// </summary>
    private static HBA.Deliveries.Domain.Deliveries.Delivery ADeuxDoigtsDeLaRemise(
        decimal? valeurDeclaree = null, bool paiementALaLivraison = false)
    {
        var livreur = DriverId.New();
        var course = UneCourse.Express(valeurDeclaree, paiementALaLivraison);

        course.StartSearching(UneCourse.Maintenant).IsSuccess.Should().BeTrue();
        course.AssignTo(livreur).IsSuccess.Should().BeTrue();
        course.AcceptByDriver(livreur).IsSuccess.Should().BeTrue();
        course.MarkArrivedAtPickup().IsSuccess.Should().BeTrue();
        course.MarkPickedUp().IsSuccess.Should().BeTrue();
        course.MarkInTransit().IsSuccess.Should().BeTrue();
        course.MarkArrivedAtDropoff().IsSuccess.Should().BeTrue();

        return course;
    }

    /// <summary>
    /// ISSUE-057 : aucune course ne naît plus sans exigence de preuve.
    ///
    /// C'était le défaut d'origine — `RequiredProof` valait `None` sur TOUTE la
    /// plateforme, donc `MarkDelivered` ne demandait jamais rien.
    /// </summary>
    [Fact]
    public void Une_course_ordinaire_exige_au_moins_une_photo()
    {
        var course = UneCourse.Express();

        course.RequiredProof.Should().Be(ProofOfDeliveryKind.Photo);
        course.IssuedPin.Should().BeNull("une photo ne se dicte pas");
    }

    /// <summary>Paiement à la livraison : de l'argent change de main, donc un code.</summary>
    [Fact]
    public void Le_paiement_a_la_livraison_exige_un_code()
    {
        var course = UneCourse.Express(paiementALaLivraison: true);

        course.RequiredProof.Should().Be(ProofOfDeliveryKind.Pin);
        course.IssuedPin.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>Au-dessus du seuil, le litige coûte plus cher que la friction.</summary>
    [Fact]
    public void Une_valeur_declaree_elevee_exige_un_code()
    {
        var course = UneCourse.Express(valeurDeclaree: ProofPolicy.HighValueThreshold);

        course.RequiredProof.Should().Be(ProofOfDeliveryKind.Pin);
    }

    /// <summary>
    /// LE CODE EST ALÉATOIRE ET PROPRE À CHAQUE COURSE.
    ///
    /// L'ancien défaut était un OTP constant « 123456 ». Vingt tirages suffisent :
    /// une constante donnerait vingt fois la même valeur.
    /// </summary>
    [Fact]
    public void Le_code_emis_est_aleatoire_et_propre_a_chaque_course()
    {
        var codes = Enumerable.Range(0, 20)
            .Select(_ => UneCourse.Express(paiementALaLivraison: true).IssuedPin)
            .ToList();

        codes.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c));
        codes.Distinct().Should().HaveCountGreaterThan(1, "un code constant n'atteste de rien");
    }

    [Fact]
    public void Le_bon_code_verifie_la_preuve()
    {
        var course = ADeuxDoigtsDeLaRemise(paiementALaLivraison: true);
        var code = course.IssuedPin!;

        course.MarkDelivered(code, 0.8m).IsSuccess.Should().BeTrue();

        course.Status.Should().Be(DeliveryStatus.Delivered);
        course.Proof.Should().NotBeNull();
        course.Proof!.Kind.Should().Be(ProofOfDeliveryKind.Pin);
    }

    /// <summary>
    /// UN CODE FAUX EST REFUSÉ, ET LA COURSE N'AVANCE PAS.
    ///
    /// C'est la règle que l'ancien code violait le plus grossièrement : toute
    /// chaîne non vide satisfaisait n'importe quel genre de preuve.
    /// </summary>
    [Fact]
    public void Un_code_faux_est_refuse_et_la_course_reste_ouverte()
    {
        var course = ADeuxDoigtsDeLaRemise(paiementALaLivraison: true);

        var resultat = course.MarkDelivered("000000", 0.8m);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("delivery.proof.pin_mismatch");
        course.Status.Should().NotBe(DeliveryStatus.Delivered);
        course.Proof.Should().BeNull();
    }

    /// <summary>
    /// LE COMPTEUR NE COMPTE QUE LES MAUVAISES RÉPONSES, PAS LES ABSENCES.
    ///
    /// Sans cette distinction, cinq appels sans corps verrouilleraient la course
    /// d'un livreur — un déni de service à un appel près.
    /// </summary>
    [Fact]
    public void Une_preuve_absente_ne_consomme_pas_de_tentative()
    {
        var course = ADeuxDoigtsDeLaRemise(paiementALaLivraison: true);

        for (var i = 0; i < HBA.Deliveries.Domain.Deliveries.Delivery.MaxFailedProofAttempts + 2; i++)
        {
            course.MarkDelivered(null, 0.8m).IsFailure.Should().BeTrue();
        }

        course.FailedProofAttempts.Should().Be(0);
        course.IsProofLocked.Should().BeFalse();

        // Et le bon code passe encore.
        course.MarkDelivered(course.IssuedPin, 0.8m).IsSuccess.Should().BeTrue();
    }

    /// <summary>Cinq mauvaises réponses verrouillent la preuve.</summary>
    [Fact]
    public void Un_code_faux_epuise_les_tentatives_puis_bloque()
    {
        var course = ADeuxDoigtsDeLaRemise(paiementALaLivraison: true);
        var max = HBA.Deliveries.Domain.Deliveries.Delivery.MaxFailedProofAttempts;

        for (var i = 0; i < max; i++)
        {
            course.MarkDelivered("000000", 0.8m).Error.Code.Should().Be("delivery.proof.pin_mismatch");
        }

        course.IsProofLocked.Should().BeTrue();

        // LE VERROU TIENT MÊME CONTRE LE BON CODE. C'est le point : après cinq
        // essais, ce n'est plus au livreur de trancher, c'est au support.
        var apresVerrou = course.MarkDelivered(course.IssuedPin, 0.8m);
        apresVerrou.IsFailure.Should().BeTrue();
        apresVerrou.Error.Code.Should().Be("delivery.proof.locked");
        course.Status.Should().NotBe(DeliveryStatus.Delivered);
    }

    /// <summary>
    /// UNE COURSE DÉJÀ REMISE NE SE REMET PAS DEUX FOIS — le rejeu est refusé par
    /// la machine à états, avant même de regarder la preuve.
    /// </summary>
    [Fact]
    public void Une_course_deja_remise_n_est_pas_rejouable()
    {
        var course = ADeuxDoigtsDeLaRemise(paiementALaLivraison: true);
        var code = course.IssuedPin!;

        course.MarkDelivered(code, 0.8m).IsSuccess.Should().BeTrue();

        var rejeu = course.MarkDelivered(code, 0.8m);
        rejeu.IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// UNE PHOTO DOIT AVOIR LA FORME D'UNE RÉFÉRENCE DE STOCKAGE.
    ///
    /// C'est ce qui écarte le cas qui a motivé la correction : le livreur qui tape
    /// « ok » et clôt la course.
    /// </summary>
    [Fact]
    public void Une_photo_doit_ressembler_a_une_reference_de_stockage()
    {
        var course = ADeuxDoigtsDeLaRemise();
        course.RequiredProof.Should().Be(ProofOfDeliveryKind.Photo);

        course.MarkDelivered("ok", 0.8m).IsFailure.Should().BeTrue("« ok » n'est pas un fichier");

        course.MarkDelivered("https://media.hba/preuves/9f2c1a4e.jpg", 0.8m)
            .IsSuccess.Should().BeTrue();

        course.Proof!.Kind.Should().Be(ProofOfDeliveryKind.Photo);
    }

    /// <summary>
    /// UN CODE NE SE RATTRAPE PAS AVEC UNE PHOTO. Le genre exigé est décidé à la
    /// création, jamais par celui qui remet.
    /// </summary>
    [Fact]
    public void Un_code_ne_se_rattrape_pas_avec_une_photo()
    {
        var course = ADeuxDoigtsDeLaRemise(paiementALaLivraison: true);

        var resultat = course.MarkDelivered("https://media.hba/preuves/9f2c1a4e.jpg", 0.8m);

        resultat.IsFailure.Should().BeTrue();
        course.Status.Should().NotBe(DeliveryStatus.Delivered);
    }
}
