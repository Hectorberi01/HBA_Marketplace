using HBA.Deliveries.Application.Deliveries.Commands;
using HBA.Deliveries.Application.Drivers;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using Microsoft.Extensions.Logging.Abstractions;
using Livreur = HBA.Deliveries.Domain.Drivers.Driver;

namespace HBA.Delivery.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LOT 5.2 — ISSUE-029 / ISSUE-030 : LE DOMAINE LIVRAISON ÉTAIT INERTE.
///
/// Ce que ces tests gardent, dans l'ordre d'importance :
///
///   1. UNE POSITION PUBLIÉE ALIMENTE LE CACHE. `IDriverLocationCache.SetAsync`
///      n'avait AUCUN appelant : `DispatchDeliveryCommandHandler` lisait un cache
///      que rien ne remplissait, donc aucune course n'était jamais proposée à
///      personne. C'est le test qui garde le chaînon.
///
///   2. L'IDENTITÉ VIENT DU JETON. `ResolveDriverQuery` traduit le compte en
///      livreur ; aucune route livreur ne prend d'identifiant. Un compte sans
///      livreur obtient « introuvable », pas la course de quelqu'un d'autre.
///
///   3. UN LIVREUR NE FAIT PROGRESSER QUE SA COURSE. La garde est
///      `RequiredDriverId`, et elle rend « introuvable » — jamais « interdit ».
///
/// CE QUE CES TESTS N'ÉPROUVENT PAS : les ROUTES. Qu'elles passent bien
/// `RequiredDriverId` et qu'elles ne lisent aucun identifiant dans le corps ne
/// s'éprouve qu'avec l'hôte entier, et le domaine livraison n'a pas de projet
/// `*.AuthorizationTests`. C'est la faille ISSUE-017/018 qui se rouvre par là,
/// et rien ici ne la surveille.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SessionLivreurTests
{
    private const double LatitudeCotonou = 6.3654;
    private const double LongitudeCotonou = 2.4183;

    // ─────────────────────────────────────────────────────────────────────────
    // 1. LA POSITION ALIMENTE LE CACHE
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Une_position_publiee_alimente_le_cache_de_dispatch()
    {
        var livreur = UnLivreurEnService();
        var (handler, cache, _) = Atelier(livreur);

        var resultat = await handler.Handle(
            new ReportDriverPositionCommand(livreur.Id.Value, LatitudeCotonou, LongitudeCotonou),
            CancellationToken.None);

        resultat.IsSuccess.Should().BeTrue();
        cache.Ecritures.Should().Be(1, "c'est l'appelant qui manquait à SetAsync");

        var enCache = await cache.GetAsync(livreur.Id);
        enCache.Should().NotBeNull();
        enCache!.Value.Position.Latitude.Should().Be(LatitudeCotonou);
        enCache.Value.Position.Longitude.Should().Be(LongitudeCotonou);
    }

    /// <summary>
    /// LE BATTEMENT N'ÉCRIT PAS EN BASE À CHAQUE FOIS.
    ///
    /// C'est l'invariant que l'encadré de `IDriverLocationCache` exige : cent
    /// livreurs qui émettent toutes les cinq à quinze secondes feraient sept à
    /// vingt écritures PostgreSQL par seconde sur une donnée sans historique. La
    /// recopie en base est ÉPISODIQUE ; Redis reçoit tout.
    /// </summary>
    [Fact]
    public async Task Deux_battements_rapproches_n_ecrivent_qu_une_fois_en_base()
    {
        var livreur = UnLivreurEnService();
        var (handler, cache, unite) = Atelier(livreur);

        var commande = new ReportDriverPositionCommand(livreur.Id.Value, LatitudeCotonou, LongitudeCotonou);

        await handler.Handle(commande, CancellationToken.None);
        await handler.Handle(commande, CancellationToken.None);
        await handler.Handle(commande, CancellationToken.None);

        cache.Ecritures.Should().Be(3, "le cache reçoit CHAQUE battement");
        unite.Enregistrements.Should().Be(1, "la base n'en reçoit qu'un — la recopie est épisodique");
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UN CONFLIT DE CONCURRENCE SUR LA RECOPIE NE FAIT PAS ÉCHOUER LE BATTEMENT.
    ///
    /// `drivers` porte un jeton de concurrence depuis le lot 8.3, posé pour la
    /// DISPONIBILITÉ — écrite par le dispatch, par le livreur et par la course, qui
    /// ne s'attendent pas. Mais un jeton s'applique à TOUT `UPDATE` de la ligne, y
    /// compris la recopie épisodique de position.
    ///
    /// Sans tolérance, un battement GPS qui croiserait un changement de statut
    /// rendrait un 409 à l'application du livreur — pour une écriture de confort,
    /// alors que Redis a déjà reçu la donnée que le dispatch lit réellement.
    ///
    /// Ce test est le seul endroit où cette branche s'exécute. Sans lui, la
    /// tolérance serait du code que personne ne parcourt jamais — et l'on ne
    /// saurait pas si elle avale le conflit ou le laisse remonter.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public async Task Un_conflit_sur_la_recopie_laisse_le_battement_reussir()
    {
        var livreur = UnLivreurEnService();
        var (handler, cache, unite) = Atelier(livreur);

        unite.ProchainEnregistrementEnConflit = true;

        var resultat = await handler.Handle(
            new ReportDriverPositionCommand(livreur.Id.Value, LatitudeCotonou, LongitudeCotonou),
            CancellationToken.None);

        resultat.IsSuccess.Should().BeTrue(
            "la position est en cache — c'est la seule source du dispatch ; "
            + "la recopie en base est un instantané de confort");

        cache.Ecritures.Should().Be(1, "le cache est écrit AVANT la recopie, et il l'a été");
        unite.Enregistrements.Should().Be(0, "rien n'a été persisté : c'est ce que le conflit signifie");
    }

    /// <summary>
    /// La position d'un livreur hors service n'est pas conservée : c'est une
    /// donnée personnelle de géolocalisation, et la collecter hors service serait
    /// sans finalité.
    /// </summary>
    [Fact]
    public async Task Un_livreur_hors_service_ne_peut_pas_publier_sa_position()
    {
        var livreur = UnLivreurVerifie();
        var (handler, cache, _) = Atelier(livreur);

        var resultat = await handler.Handle(
            new ReportDriverPositionCommand(livreur.Id.Value, LatitudeCotonou, LongitudeCotonou),
            CancellationToken.None);

        resultat.IsFailure.Should().BeTrue();
        cache.Ecritures.Should().Be(0);
    }

    /// <summary>
    /// Passer hors service RETIRE du cache. Sans cela, le livreur resterait
    /// proposable jusqu'à l'expiration de sa clé horodatée — deux minutes pendant
    /// lesquelles on lui propose des courses qu'il ne prendra pas.
    /// </summary>
    [Fact]
    public async Task Quitter_le_service_retire_le_livreur_du_cache()
    {
        var livreur = UnLivreurEnService();
        var (handler, cache, _) = Atelier(livreur);

        await handler.Handle(
            new ReportDriverPositionCommand(livreur.Id.Value, LatitudeCotonou, LongitudeCotonou),
            CancellationToken.None);

        var resultat = await handler.Handle(new GoOfflineCommand(livreur.Id.Value), CancellationToken.None);

        resultat.IsSuccess.Should().BeTrue();
        cache.Retraits.Should().Be(1);
        (await cache.GetAsync(livreur.Id)).Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. UN LIVREUR NON VÉRIFIÉ N'EST PAS DISPATCHABLE
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// LA GARDE EST DANS L'AGRÉGAT, ET C'EST CE QUI EMPÊCHE UN LIVREUR SUSPENDU
    /// DE SE REMETTRE EN LIGNE DEPUIS SON TÉLÉPHONE.
    ///
    /// `DriverStore` naissait avec un livreur déjà « VERIFIED » et aucune route ne
    /// pouvait changer cet état : « vérifié » ne voulait rien dire, et cette garde
    /// était donc toujours satisfaite.
    /// </summary>
    [Fact]
    public async Task Un_livreur_non_verifie_ne_peut_pas_prendre_son_service()
    {
        var livreur = UnLivreurInscrit();
        var (handler, _, _) = Atelier(livreur);

        var resultat = await handler.Handle(new GoOnlineCommand(livreur.Id.Value), CancellationToken.None);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("driver.not_active");
        livreur.CanReceiveOffers.Should().BeFalse();
    }

    [Fact]
    public void Un_livreur_verifie_puis_en_service_est_dispatchable()
    {
        var livreur = UnLivreurEnService();

        livreur.AccountStatus.Should().Be(DriverAccountStatus.Active);
        livreur.Availability.Should().Be(DriverAvailability.Available);
        livreur.CanReceiveOffers.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. L'IDENTITÉ VIENT DU JETON
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Le_livreur_est_resolu_depuis_le_compte_du_jeton()
    {
        var livreur = UnLivreurEnService();
        var (handler, _, _) = Atelier(livreur);

        var resolu = await handler.Handle(new ResolveDriverQuery(livreur.UserId), CancellationToken.None);

        resolu.IsSuccess.Should().BeTrue();
        resolu.Value.Should().Be(livreur.Id.Value);
    }

    /// <summary>
    /// « INTROUVABLE » ET NON « INTERDIT » : distinguer les deux dirait à
    /// n'importe quel compte authentifié quels autres comptes sont des livreurs.
    /// </summary>
    [Fact]
    public async Task Un_compte_sans_dossier_livreur_n_obtient_aucun_identifiant()
    {
        var livreur = UnLivreurEnService();
        var (handler, _, _) = Atelier(livreur);

        var resolu = await handler.Handle(new ResolveDriverQuery(Guid.NewGuid()), CancellationToken.None);

        resolu.IsFailure.Should().BeTrue();
        resolu.Error.Code.Should().Be("driver.not_found");
    }

    [Fact]
    public async Task Un_jeton_sans_compte_est_refuse_avant_toute_lecture()
    {
        var livreur = UnLivreurEnService();
        var (handler, _, _) = Atelier(livreur);

        var resolu = await handler.Handle(new ResolveDriverQuery(Guid.Empty), CancellationToken.None);

        resolu.IsFailure.Should().BeTrue();
        resolu.Error.Code.Should().Be("driver.unauthenticated");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. UN LIVREUR NE FAIT PROGRESSER QUE SA COURSE
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_livreur_ne_peut_pas_faire_progresser_la_course_d_un_autre()
    {
        var titulaire = UnLivreurEnService();
        var intrus = UnLivreurEnService();

        var courses = new FauxDepotDeCourses();
        var livreurs = new FauxDepotDeLivreurs();
        livreurs.Ajouter(titulaire);
        livreurs.Ajouter(intrus);

        var course = UneCourse.ProposeeA(titulaire.Id);
        course.AcceptByDriver(titulaire.Id).IsSuccess.Should().BeTrue();
        titulaire.MarkBusy().IsSuccess.Should().BeTrue();
        courses.Ajouter(course);

        var handler = new DeliveryProgressCommandHandler(
            courses, livreurs, new FauxPartageDeRecette(), new FausseUniteDeTravail());

        var refuse = await handler.Handle(
            new MarkArrivedAtPickupCommand(course.Id.Value, intrus.Id.Value), CancellationToken.None);

        refuse.IsFailure.Should().BeTrue();
        refuse.Error.Code.Should().Be(
            "delivery.not_found",
            "un 403 confirmerait à l'intrus que la course existe et appartient à quelqu'un d'autre");
        course.Status.Should().Be(DeliveryStatus.DriverAccepted, "rien ne doit avoir bougé");

        var accepte = await handler.Handle(
            new MarkArrivedAtPickupCommand(course.Id.Value, titulaire.Id.Value), CancellationToken.None);

        accepte.IsSuccess.Should().BeTrue("le livreur affecté, lui, passe");
    }

    /// <summary>
    /// C'EST SUR LA REMISE QUE LA GARDE COMPTE LE PLUS : c'est cette transition
    /// qui déclenche le GAIN du livreur. Sans elle, tout livreur authentifié
    /// clôturait la course d'un autre et en encaissait la part.
    /// </summary>
    [Fact]
    public async Task Un_intrus_ne_peut_pas_cloturer_la_course_d_un_autre_ni_en_toucher_le_gain()
    {
        var titulaire = UnLivreurEnService();
        var intrus = UnLivreurEnService();

        var courses = new FauxDepotDeCourses();
        var livreurs = new FauxDepotDeLivreurs();
        livreurs.Ajouter(titulaire);
        livreurs.Ajouter(intrus);

        var course = UneCourse.ProposeeA(titulaire.Id);
        course.AcceptByDriver(titulaire.Id).IsSuccess.Should().BeTrue();
        titulaire.MarkBusy().IsSuccess.Should().BeTrue();
        courses.Ajouter(course);

        var handler = new DeliveryProgressCommandHandler(
            courses, livreurs, new FauxPartageDeRecette(), new FausseUniteDeTravail());

        var refuse = await handler.Handle(
            new MarkDeliveredCommand(course.Id.Value, null, intrus.Id.Value), CancellationToken.None);

        refuse.IsFailure.Should().BeTrue();
        refuse.Error.Code.Should().Be("delivery.not_found");
        course.DriverEarning.Should().BeNull("aucun gain ne doit avoir été figé");
    }

    /// <summary>
    /// L'acceptation touche DEUX agrégats : la course et le livreur. Oublier de
    /// marquer le livreur occupé le laisserait recevoir une seconde proposition
    /// pendant qu'il roule.
    /// </summary>
    [Fact]
    public async Task Accepter_une_proposition_engage_la_course_ET_le_livreur()
    {
        var livreur = UnLivreurEnService();

        var courses = new FauxDepotDeCourses();
        var livreurs = new FauxDepotDeLivreurs();
        livreurs.Ajouter(livreur);

        var course = UneCourse.ProposeeA(livreur.Id);
        courses.Ajouter(course);

        var handler = new DriverOfferCommandHandler(courses, livreurs, new FausseUniteDeTravail());

        var resultat = await handler.Handle(
            new AcceptDeliveryCommand(course.Id.Value, livreur.Id.Value), CancellationToken.None);

        resultat.IsSuccess.Should().BeTrue();
        course.Status.Should().Be(DeliveryStatus.DriverAccepted);
        course.AssignedDriverId.Should().Be(livreur.Id);
        livreur.Availability.Should().Be(DriverAvailability.Busy);
        livreur.CanReceiveOffers.Should().BeFalse();
    }

    [Fact]
    public async Task Un_livreur_a_qui_la_course_n_est_pas_proposee_ne_peut_pas_l_accepter()
    {
        var titulaire = UnLivreurEnService();
        var intrus = UnLivreurEnService();

        var courses = new FauxDepotDeCourses();
        var livreurs = new FauxDepotDeLivreurs();
        livreurs.Ajouter(titulaire);
        livreurs.Ajouter(intrus);

        var course = UneCourse.ProposeeA(titulaire.Id);
        courses.Ajouter(course);

        var handler = new DriverOfferCommandHandler(courses, livreurs, new FausseUniteDeTravail());

        var resultat = await handler.Handle(
            new AcceptDeliveryCommand(course.Id.Value, intrus.Id.Value), CancellationToken.None);

        resultat.IsFailure.Should().BeTrue();
        course.AssignedDriverId.Should().BeNull();
        intrus.Availability.Should().Be(DriverAvailability.Available, "l'intrus ne doit pas être engagé");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FABRIQUES
    // ─────────────────────────────────────────────────────────────────────────

    private static (DriverSessionCommandHandler Handler, FauxCacheDePositions Cache, FausseUniteDeTravail Unite)
        Atelier(Livreur livreur)
    {
        var livreurs = new FauxDepotDeLivreurs();
        livreurs.Ajouter(livreur);

        var cache = new FauxCacheDePositions();
        var unite = new FausseUniteDeTravail();

        var handler = new DriverSessionCommandHandler(
            livreurs, cache, unite, NullLogger<DriverSessionCommandHandler>.Instance);

        return (handler, cache, unite);
    }

    /// <summary>Inscrit, pièces non validées : `PendingVerification`.</summary>
    private static Livreur UnLivreurInscrit()
    {
        var livreur = Livreur.Register(Guid.NewGuid(), "Kossi Adjovi", "+2290197000042", VehicleType.Motorcycle);
        livreur.IsSuccess.Should().BeTrue("la fabrique de test doit produire un livreur valide");
        return livreur.Value;
    }

    /// <summary>Vérifié, mais hors ligne.</summary>
    private static Livreur UnLivreurVerifie()
    {
        var livreur = UnLivreurInscrit();
        livreur.Verify().IsSuccess.Should().BeTrue();
        return livreur;
    }

    /// <summary>Vérifié ET en service : dispatchable.</summary>
    private static Livreur UnLivreurEnService()
    {
        var livreur = UnLivreurVerifie();
        livreur.GoOnline().IsSuccess.Should().BeTrue();
        return livreur;
    }
}
