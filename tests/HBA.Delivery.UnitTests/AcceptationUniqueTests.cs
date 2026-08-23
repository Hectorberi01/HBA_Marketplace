using HBA.Deliveries.Domain.Deliveries;
using HBA.Dispatch.Application;
using HBA.Shared.IntegrationEvents;

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

    // ─────────────────────────────────────────────────────────────────────────
    // LA MAQUETTE DE DISPATCH — SIMULTANÉITÉ RÉELLE
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// LE TEST QUE L'AUDIT EXIGE : deux acceptations SIMULTANÉES, une seule
    /// réussit.
    ///
    /// Ici la simultanéité n'est pas simulée : deux tâches entrent réellement en
    /// même temps dans `AssignAsync`, et c'est `ConcurrentDictionary.TryAdd` qui
    /// arbitre. Avant la correction, les DEUX repartaient avec une affectation.
    /// </summary>
    [Fact]
    public async Task Deux_acceptations_simultanees_une_seule_reussit()
    {
        var store = new DispatchStore();
        var publieur = new PublieurMuet();
        var course = Guid.NewGuid();
        var premier = Guid.NewGuid();
        var second = Guid.NewGuid();

        var barriere = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<(bool Assigned, Assignment Assignment)> Accepter(Guid livreur)
        {
            await barriere.Task;
            return await store.AssignAsync(course, livreur, "AUTO", publieur);
        }

        var courseA = Accepter(premier);
        var courseB = Accepter(second);

        barriere.SetResult();
        var resultats = await Task.WhenAll(courseA, courseB);

        resultats.Count(r => r.Assigned).Should().Be(1, "une seule affectation peut gagner");

        var gagnant = resultats.Single(r => r.Assigned).Assignment.DriverId;
        var perdant = resultats.Single(r => !r.Assigned);

        // Le perdant reçoit l'affectation QUI A GAGNÉ, pas la sienne : c'est ce
        // qui permet à l'appelant de dire « quelqu'un a été plus rapide » plutôt
        // que « erreur ».
        perdant.Assignment.DriverId.Should().Be(gagnant);

        store.TryGetAssignment(course, out var enregistree).Should().BeTrue();
        enregistree!.DriverId.Should().Be(gagnant);
    }

    /// <summary>
    /// Un rejeu du MÊME livreur n'est pas un conflit : l'appelant est derrière un
    /// délai, sa seconde requête doit retrouver son affectation. Mais rien ne
    /// doit être republié — sinon le livreur est notifié deux fois et la course
    /// comptée deux fois en aval.
    /// </summary>
    [Fact]
    public async Task Un_rejeu_du_meme_livreur_rend_son_affectation_sans_republier()
    {
        var store = new DispatchStore();
        var publieur = new PublieurMuet();
        var course = Guid.NewGuid();
        var livreur = Guid.NewGuid();

        var premiere = await store.AssignAsync(course, livreur, "MANUAL", publieur);
        var publicationsApresPremiere = publieur.Publies;

        var seconde = await store.AssignAsync(course, livreur, "MANUAL", publieur);

        seconde.Assigned.Should().BeTrue();
        seconde.Assignment.Id.Should().Be(premiere.Assignment.Id, "c'est la MÊME affectation");
        publieur.Publies.Should().Be(publicationsApresPremiere, "un rejeu ne republie rien");
    }

    /// <summary>
    /// LA RÉGRESSION QUE LA CORRECTION AURAIT PU CRÉER.
    ///
    /// Refuser d'écraser sans libérer l'affectation morte aurait rendu toute
    /// course refusée DÉFINITIVEMENT non affectable : `TryAdd` serait retombé
    /// éternellement sur l'affectation du tour précédent. Un défaut pire que
    /// celui qu'on ferme — une course qui ne part jamais.
    /// </summary>
    [Fact]
    public async Task Apres_un_nouveau_tour_de_dispatch_un_autre_livreur_peut_etre_affecte()
    {
        var store = new DispatchStore();
        var publieur = new PublieurMuet();
        var course = Guid.NewGuid();

        (await store.AssignAsync(course, Guid.NewGuid(), "AUTO", publieur)).Assigned.Should().BeTrue();

        await store.RetryAsync(course, publieur);

        var suivant = Guid.NewGuid();
        var apresRelance = await store.AssignAsync(course, suivant, "AUTO", publieur);

        apresRelance.Assigned.Should().BeTrue("un nouveau tour rouvre l'affectation");
        apresRelance.Assignment.DriverId.Should().Be(suivant);
    }

    [Fact]
    public async Task Une_course_annulee_libere_son_affectation()
    {
        var store = new DispatchStore();
        var publieur = new PublieurMuet();
        var course = Guid.NewGuid();

        await store.AssignAsync(course, Guid.NewGuid(), "AUTO", publieur);
        store.Cancel(course);

        var suivant = Guid.NewGuid();
        (await store.AssignAsync(course, suivant, "AUTO", publieur)).Assigned.Should().BeTrue();
    }

    /// <summary>
    /// Publieur d'événements qui ne fait que compter.
    ///
    /// Il ne remplace pas Kafka et ne prétend rien en dire : ISSUE-007 signale
    /// que la file de ces maquettes n'est de toute façon jamais drainée. Ce qu'on
    /// mesure ici, c'est le NOMBRE d'appels — c'est-à-dire qu'un rejeu ne
    /// redéclenche pas la chaîne aval.
    /// </summary>
    private sealed class PublieurMuet : IIntegrationEventPublisher
    {
        private int _publies;

        public int Publies => _publies;

        public Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _publies);
            return Task.CompletedTask;
        }
    }
}
