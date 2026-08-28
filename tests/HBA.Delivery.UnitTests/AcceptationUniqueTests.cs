using HBA.Deliveries.Domain.Deliveries;

namespace HBA.Delivery.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ISSUE-028 — « deux livreurs peuvent accepter la même course » (CRITICAL).
///
/// Le défaut avait deux moitiés, et il fallait les deux pour qu'il morde :
///
///   • `DispatchStore.AssignAsync` écrasait sans relire — `_assignments[id] = …`.
///     Comme `RequestAsync` propose la course à PLUSIEURS candidats, deux
///     acceptations concurrentes étaient le cas NOMINAL, et les deux repartaient
///     avec une affectation en main.
///
///   • L'agrégat `Delivery` n'avait aucun jeton de concurrence, et rien en base
///     n'empêchait un livreur d'être engagé sur deux courses.
///
/// CE QUE CES TESTS ÉPROUVENT VRAIMENT, ET CE QU'ILS NE PEUVENT PAS ÉPROUVER.
///
/// La simultanéité de `DispatchStore` est RÉELLE ici : c'est un
/// `ConcurrentDictionary` en mémoire, on peut vraiment lancer deux acceptations
/// en parallèle et compter les gagnants.
///
/// Celle de la BASE ne l'est pas. `xmin` et `ux_deliveries_engaged_driver` ne
/// s'évaluent que dans PostgreSQL, sur deux transactions concurrentes. Ce qui est
/// éprouvé côté agrégat, c'est donc la garde APPLICATIVE : la seconde
/// acceptation est refusée dès lors qu'elle voit l'état écrit par la première.
/// Le cas où les deux lisent AVANT que l'une n'écrive appartient à la base, et
/// ces tests-là restent à écrire le jour où le dépôt aura des tests
/// d'intégration sur le domaine livraison.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class AcceptationUniqueTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // L'AGRÉGAT
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Une_seule_acceptation_reussit_sur_une_course_deja_acceptee()
    {
        var premier = DriverId.New();
        var course = UneCourse.ProposeeA(premier);

        course.AcceptByDriver(premier).IsSuccess.Should().BeTrue();

        // Le second n'a même pas d'offre en cours : la course a quitté
        // `DriverAssigned` au moment où le premier a accepté.
        var second = course.AcceptByDriver(DriverId.New());

        second.IsFailure.Should().BeTrue("la course n'est plus proposée à personne");
        course.AssignedDriverId.Should().Be(premier);
        course.Status.Should().Be(DeliveryStatus.DriverAccepted);
    }

    /// <summary>
    /// Le MÊME livreur qui rejoue son acceptation ne doit pas non plus obtenir un
    /// second succès : `AcceptedAtUtc` serait réécrit, et l'événement
    /// `DeliveryAcceptedDomainEvent` levé deux fois — donc, en bout de chaîne,
    /// deux rémunérations.
    /// </summary>
    [Fact]
    public void Le_meme_livreur_ne_peut_pas_accepter_deux_fois()
    {
        var livreur = DriverId.New();
        var course = UneCourse.ProposeeA(livreur);

        course.AcceptByDriver(livreur).IsSuccess.Should().BeTrue();
        course.AcceptByDriver(livreur).IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// CE TEST GARDE LE VERROU OPTIMISTE HONNÊTE — VOIR L'ENCADRÉ DE
    /// `InventoryItem.StockVersion`.
    ///
    /// `xmin` n'est évalué que si EF émet un `UPDATE` sur la ligne PARENTE. Si
    /// quelqu'un réécrivait un jour `AcceptByDriver` pour ne muter que la
    /// proposition enfant, le jeton deviendrait inerte — visible dans la
    /// configuration, rassurant à la relecture, sans aucun effet.
    ///
    /// On éprouve donc que l'acceptation écrit bien des colonnes de la COURSE.
    /// </summary>
    [Fact]
    public void Accepter_ecrit_sur_la_ligne_parente_et_pas_seulement_sur_la_proposition()
    {
        var livreur = DriverId.New();
        var course = UneCourse.ProposeeA(livreur);

        var statutAvant = course.Status;
        var affecteAvant = course.AssignedDriverId;
        var accepteAvant = course.AcceptedAtUtc;

        course.AcceptByDriver(livreur).IsSuccess.Should().BeTrue();

        course.Status.Should().NotBe(statutAvant, "le statut de la COURSE doit changer");
        course.AssignedDriverId.Should().NotBe(affecteAvant, "l'affecté est une colonne de la COURSE");
        course.AcceptedAtUtc.Should().NotBe(accepteAvant, "l'horodatage est une colonne de la COURSE");
    }

    [Fact]
    public void Un_livreur_qui_n_a_pas_recu_l_offre_ne_peut_pas_accepter()
    {
        var course = UneCourse.ProposeeA(DriverId.New());

        course.AcceptByDriver(DriverId.New()).IsFailure.Should().BeTrue();
        course.AssignedDriverId.Should().BeNull();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LES QUATRE TESTS DE `DispatchStore` ONT ÉTÉ RETIRÉS AVEC LEUR SUJET (D42).
    //
    // Ils éprouvaient `AssignAsync` en SIMULTANÉITÉ RÉELLE — deux tâches entrant
    // ensemble dans un `ConcurrentDictionary`, avec `TryAdd` pour arbitre. C'était
    // le seul endroit du domaine livraison où une course entre deux écritures se
    // testait pour de bon.
    //
    // CE QUI EST PERDU, ET IL FAUT LE SAVOIR. Les quatre tests ci-dessus éprouvent
    // la garde APPLICATIVE de l'agrégat : la seconde acceptation est refusée parce
    // que l'état a changé. Ils sont séquentiels. Ce qui ferme le cas VRAIMENT
    // simultané, sur deux connexions PostgreSQL, est l'index unique partiel
    // `ux_deliveries_engaged_driver` et le jeton `xmin` — et aucun test en mémoire
    // ne peut les éprouver. La couverture de ce cas est donc passée de « éprouvée
    // sur une maquette » à « éprouvée nulle part », et c'est un recul réel : la
    // maquette n'était pas la production, mais elle était le seul banc d'essai.
    //
    // Le combler demande un projet d'intégration avec une base — il n'en existe
    // aucun pour ce domaine.
    // ═════════════════════════════════════════════════════════════════════════
}
