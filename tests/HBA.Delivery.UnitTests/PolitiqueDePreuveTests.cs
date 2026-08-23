using HBA.Deliveries.Domain.Deliveries;

namespace HBA.Delivery.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ISSUE-057 — « `RequiredProof` n'est renseigné par aucun producteur ».
///
/// Le champ existait, était persisté, était projeté vers l'application livreur —
/// et valait `None` sur TOUTE course de la plateforme, parce que c'était la
/// valeur par défaut du contrat et qu'aucun des deux producteurs ne la
/// remplaçait. `MarkDelivered` ne demande rien quand `RequiredProof` vaut
/// `None` : n'importe quelle course se clôturait d'un geste.
///
/// La politique est désormais appliquée par `Delivery.Create` — voir
/// `ProofPolicy`. Ces tests éprouvent qu'aucun chemin de création ne peut plus
/// produire une course sans exigence.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class PolitiqueDePreuveTests
{
    /// <summary>
    /// Le test que l'audit exige : une course créée porte une politique de preuve
    /// NON NULLE. C'est le plancher, et il ne dépend d'aucun paramètre.
    /// </summary>
    [Fact]
    public void Une_course_creee_porte_toujours_une_exigence_de_preuve()
    {
        UneCourse.Express().RequiredProof.Should().NotBe(ProofOfDeliveryKind.None);
    }

    [Fact]
    public void Une_course_ordinaire_exige_une_photo()
    {
        var course = UneCourse.Express(valeurDeclaree: 4_000m);

        course.RequiredProof.Should().Be(ProofOfDeliveryKind.Photo);

        // Pas de code émis pour une preuve par photo : `IssuedPin` ne vaut que
        // pour `Pin`, et un code posé là serait un secret que personne n'utilise.
        course.IssuedPin.Should().BeNull();
    }

    [Fact]
    public void Une_course_de_valeur_exige_un_code()
    {
        var course = UneCourse.Express(valeurDeclaree: ProofPolicy.HighValueThreshold);

        course.RequiredProof.Should().Be(ProofOfDeliveryKind.Pin);
        course.IssuedPin.Should().NotBeNullOrWhiteSpace("le code est émis À LA CRÉATION, pas à la remise");
    }

    /// <summary>
    /// Le seuil est INCLUSIF, et le test le dit explicitement : « au-dessus de
    /// 50 000 » et « à partir de 50 000 » sont deux règles différentes, et celle
    /// qu'on a choisie doit être lisible ailleurs que dans un `&gt;=`.
    /// </summary>
    [Fact]
    public void Le_seuil_de_valeur_est_inclusif()
    {
        UneCourse.Express(valeurDeclaree: ProofPolicy.HighValueThreshold - 1m)
            .RequiredProof.Should().Be(ProofOfDeliveryKind.Photo);

        UneCourse.Express(valeurDeclaree: ProofPolicy.HighValueThreshold)
            .RequiredProof.Should().Be(ProofOfDeliveryKind.Pin);
    }

    /// <summary>
    /// Le paiement à la livraison l'emporte SEUL, sans condition de montant : de
    /// l'argent change de main, et c'est le seul cas où « livré » sans preuve
    /// signifie que le livreur garde le colis ET l'espèce.
    /// </summary>
    [Fact]
    public void Le_paiement_a_la_livraison_exige_un_code_meme_pour_un_petit_montant()
    {
        var course = UneCourse.Express(valeurDeclaree: 500m, paiementALaLivraison: true);

        course.RequiredProof.Should().Be(ProofOfDeliveryKind.Pin);
        course.IssuedPin.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Une valeur inconnue ne fait pas retomber la course en `None` : c'est
    /// exactement le trou d'ISSUE-057. Elle est traitée comme faible — donc
    /// photo — et l'encadré de `ProofPolicy` dit que c'est le choix risqué côté
    /// litige.
    /// </summary>
    [Fact]
    public void Une_valeur_inconnue_ne_supprime_pas_l_exigence()
    {
        var course = UneCourse.Express(valeurDeclaree: null);

        course.RequiredProof.Should().Be(ProofOfDeliveryKind.Photo);
        course.RequiredProof.Should().NotBe(ProofOfDeliveryKind.None);
    }

    /// <summary>
    /// Une valeur négative est une erreur d'intégration, pas une déclaration
    /// basse. L'accepter ferait choisir « Photo » sur une donnée dont on sait
    /// déjà qu'elle est fausse.
    /// </summary>
    [Fact]
    public void Une_valeur_negative_est_refusee()
    {
        var creation = HBA.Deliveries.Domain.Deliveries.Delivery.Create(
            reference: "REF-NEGATIVE",
            source: DeliverySource.HbaExpress,
            type: DeliveryType.Express,
            pickup: UneCourse.Collecte(),
            dropoff: UneCourse.Remise(),
            package: UneCourse.Colis(),
            declaredValue: -1m,
            isCashOnDelivery: false,
            partnerId: null,
            scheduledForUtc: null,
            nowUtc: UneCourse.Maintenant);

        creation.IsFailure.Should().BeTrue();
        creation.Error.Code.Should().Be("delivery.declared_value_negative");
    }

    /// <summary>
    /// CE QUE LA POLITIQUE NE PRODUIT JAMAIS, ÉCRIT NOIR SUR BLANC.
    ///
    /// `Signature` existe dans l'énumération et l'agrégat sait la vérifier ;
    /// aucune règle ne la choisit, parce que l'application livreur ne sait pas
    /// encore capturer une signature. Ce test échouera le jour où quelqu'un
    /// ajoutera la règle — et c'est le but : il faudra alors vérifier que
    /// l'écran existe.
    /// </summary>
    [Fact]
    public void La_politique_ne_produit_jamais_de_signature_aujourd_hui()
    {
        var cas = new[]
        {
            ProofPolicy.RequiredFor(null, false),
            ProofPolicy.RequiredFor(0m, false),
            ProofPolicy.RequiredFor(1_000_000m, false),
            ProofPolicy.RequiredFor(null, true),
            ProofPolicy.RequiredFor(1_000_000m, true)
        };

        cas.Should().NotContain(ProofOfDeliveryKind.Signature);
        cas.Should().NotContain(ProofOfDeliveryKind.None);
    }
}
