using HBA.Shared.IntegrationEvents;
using HBA.Tracking.Application;

namespace HBA.Delivery.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ISSUE-058 — n'importe qui publiait la position de n'importe quel livreur.
///
/// La route était anonyme, `driverId` venait du CORPS, et — le pire —
/// `AddLocationsAsync` OUVRAIT LA SESSION elle-même quand il n'y en avait pas.
/// Les trois ensemble suffisaient à se déclarer livreur d'une course qu'on
/// n'avait jamais acceptée : le client suivait alors un point qui n'était pas
/// son colis.
///
/// CE QUE CES TESTS N'ÉPROUVENT PAS : les politiques d'autorisation des
/// routes. Elles vivent dans `TrackingEndpoints` et demandent l'hôte entier —
/// c'est le rôle des projets `*.AuthorizationTests`, qu'aucun service du domaine
/// livraison n'a encore. Ce qui est éprouvé ici est le contrôle d'AFFECTATION,
/// dans le store, qui est ce sur quoi la route s'appuie.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SuiviDeCourseTests
{
    private static readonly DateTimeOffset Maintenant = DateTimeOffset.UtcNow;

    private static IReadOnlyList<LocationPointRequest> UnPoint(long sequence = 1) =>
        [new LocationPointRequest(sequence, 6.36, 2.42, 10, 8, null, Maintenant)];

    /// <summary>
    /// LE CŒUR DE LA CORRECTION : sans session ouverte par le port interne, on
    /// n'écrit rien — et surtout, on n'ouvre plus la session soi-même.
    /// </summary>
    [Fact]
    public async Task Sans_session_aucune_position_n_est_acceptee()
    {
        var store = new TrackingStore();

        var resultat = await store.AddLocationsAsync(
            Guid.NewGuid(), Guid.NewGuid(), UnPoint(), new PublieurMuet());

        resultat.Status.Should().Be(LocationBatchStatus.NoSession);
        resultat.Accepted.Should().Be(0);
    }

    [Fact]
    public async Task Un_autre_livreur_ne_peut_pas_publier_sur_une_course_qui_n_est_pas_la_sienne()
    {
        var store = new TrackingStore();
        var publieur = new PublieurMuet();
        var course = Guid.NewGuid();
        var affecte = Guid.NewGuid();

        await store.StartAsync(course, affecte, publieur);

        var intrus = await store.AddLocationsAsync(course, Guid.NewGuid(), UnPoint(), publieur);

        intrus.Status.Should().Be(LocationBatchStatus.NotAssigned);
        store.TryGetLatest(course, out _).Should().BeFalse("aucune position n'a été retenue");
    }

    [Fact]
    public async Task Le_livreur_affecte_publie_sa_position()
    {
        var store = new TrackingStore();
        var publieur = new PublieurMuet();
        var course = Guid.NewGuid();
        var livreur = Guid.NewGuid();

        await store.StartAsync(course, livreur, publieur);

        var resultat = await store.AddLocationsAsync(course, livreur, UnPoint(), publieur);

        resultat.Status.Should().Be(LocationBatchStatus.Accepted);
        resultat.Accepted.Should().Be(1);
        store.TryGetLatest(course, out var position).Should().BeTrue();
        position!.DriverId.Should().Be(livreur);
    }

    /// <summary>
    /// Une course remise ne bouge plus. Sans cette garde, un livreur continuerait
    /// d'alimenter le trajet d'une course close.
    /// </summary>
    [Fact]
    public async Task Une_session_close_n_accepte_plus_de_position()
    {
        var store = new TrackingStore();
        var publieur = new PublieurMuet();
        var course = Guid.NewGuid();
        var livreur = Guid.NewGuid();

        await store.StartAsync(course, livreur, publieur);
        await store.StopAsync(course, publieur);

        var resultat = await store.AddLocationsAsync(course, livreur, UnPoint(), publieur);

        resultat.Status.Should().Be(LocationBatchStatus.SessionEnded);
    }

    /// <summary>
    /// `DriverOf` est ce sur quoi la garde de lecture s'appuie : sans session,
    /// personne n'est le livreur, donc personne ne lit la position.
    /// </summary>
    [Fact]
    public async Task DriverOf_ne_designe_personne_tant_qu_aucune_session_n_existe()
    {
        var store = new TrackingStore();
        var course = Guid.NewGuid();
        var livreur = Guid.NewGuid();

        store.DriverOf(course).Should().BeNull();

        await store.StartAsync(course, livreur, new PublieurMuet());

        store.DriverOf(course).Should().Be(livreur);
    }

    private sealed class PublieurMuet : IIntegrationEventPublisher
    {
        public Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
