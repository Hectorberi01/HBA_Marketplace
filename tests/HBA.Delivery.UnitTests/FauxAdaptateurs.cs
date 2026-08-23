using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using Course = HBA.Deliveries.Domain.Deliveries.Delivery;
using Livreur = HBA.Deliveries.Domain.Drivers.Driver;

namespace HBA.Delivery.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES ADAPTATEURS DE TEST DU LOT 5.2.
///
/// ÉCRITS À LA MAIN, PARCE QUE CE DÉPÔT N'A NI Moq NI NSubstitute.
///
/// Les vérifier n'était pas le sujet : ces quatre-là ne simulent rien de subtil —
/// un dictionnaire, un compteur d'appels. Ce qui est éprouvé, c'est le
/// gestionnaire, pas eux.
///
/// CE QU'ILS NE REPRODUISENT PAS, ET IL FAUT LE SAVOIR : la BASE. Pas de
/// transaction, pas de jeton `xmin`, pas d'index unique partiel. Un test qui
/// passe ici n'établit donc rien sur la concurrence réelle — c'est la limite déjà
/// annoncée par `AcceptationUniqueTests`, et elle vaut mot pour mot pour ce
/// lot-ci.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class FauxDepotDeLivreurs : IDriverRepository
{
    private readonly Dictionary<DriverId, Livreur> _parId = new();

    public void Ajouter(Livreur livreur) => _parId[livreur.Id] = livreur;

    public Task<Livreur?> GetByIdAsync(DriverId id, CancellationToken cancellationToken = default)
        => Task.FromResult(_parId.GetValueOrDefault(id));

    public Task<Livreur?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult(_parId.Values.FirstOrDefault(livreur => livreur.UserId == userId));

    public Task<IReadOnlyList<Livreur>> ListByIdsAsync(
        IReadOnlyCollection<DriverId> ids, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Livreur>>(
            ids.Where(_parId.ContainsKey).Select(id => _parId[id]).ToList());

    public Task<IReadOnlyList<Livreur>> ListByAccountStatusAsync(
        DriverAccountStatus status, int take = 100, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Livreur>>(
            _parId.Values.Where(livreur => livreur.AccountStatus == status).Take(take).ToList());

    public Task AddAsync(Livreur driver, CancellationToken cancellationToken = default)
    {
        _parId[driver.Id] = driver;
        return Task.CompletedTask;
    }
}

internal sealed class FauxDepotDeCourses : IDeliveryRepository
{
    private readonly Dictionary<DeliveryId, Course> _parId = new();

    public void Ajouter(Course course) => _parId[course.Id] = course;

    public Task<Course?> GetByIdAsync(DeliveryId id, CancellationToken cancellationToken = default)
        => Task.FromResult(_parId.GetValueOrDefault(id));

    public Task<Course?> GetByReferenceAsync(
        string reference, DeliverySource source, CancellationToken cancellationToken = default)
        => Task.FromResult(_parId.Values.FirstOrDefault(
            course => course.Reference == reference && course.Source == source));

    public Task<IReadOnlyList<Course>> ListAwaitingDriverAsync(
        int take = 50, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Course>>([]);

    public Task<IReadOnlyList<Course>> ListStaleOffersAsync(
        TimeSpan offerTimeout, int take = 50, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Course>>([]);

    public Task<IReadOnlyList<Course>> ListScheduledDueAsync(
        DateTime nowUtc, int take = 50, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Course>>([]);

    public Task<IReadOnlyList<Course>> ListActiveForDriverAsync(
        DriverId driverId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Course>>(
            _parId.Values.Where(course => course.AssignedDriverId == driverId).ToList());

    public Task AddAsync(Course delivery, CancellationToken cancellationToken = default)
    {
        _parId[delivery.Id] = delivery;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Le cache de positions, en mémoire. On y compte les écritures : c'est le seul
/// moyen d'éprouver qu'un appelant existe enfin pour `SetAsync` — le manque exact
/// que nomme la décision D30.
/// </summary>
internal sealed class FauxCacheDePositions : IDriverLocationCache
{
    private readonly Dictionary<DriverId, DriverPosition> _positions = new();

    public int Ecritures { get; private set; }

    public int Retraits { get; private set; }

    public Task SetAsync(DriverId driverId, Coordinates position, CancellationToken cancellationToken = default)
    {
        Ecritures++;
        _positions[driverId] = new DriverPosition(driverId, position, DateTime.UtcNow);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(DriverId driverId, CancellationToken cancellationToken = default)
    {
        Retraits++;
        _positions.Remove(driverId);
        return Task.CompletedTask;
    }

    public Task<DriverPosition?> GetAsync(DriverId driverId, CancellationToken cancellationToken = default)
        => Task.FromResult(_positions.TryGetValue(driverId, out var position) ? position : (DriverPosition?)null);

    public Task<IReadOnlyList<DriverPosition>> FindNearbyAsync(
        Coordinates center, double radiusKm, int limit = 20, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DriverPosition>>(_positions.Values.Take(limit).ToList());
}

internal sealed class FausseUniteDeTravail : IDeliveryUnitOfWork
{
    public int Enregistrements { get; private set; }

    /// <summary>
    /// PILOTABLE : le prochain `TrySaveChangesAsync` échouera comme s'il avait
    /// rencontré un conflit de concurrence.
    ///
    /// Sans ce levier, la tolérance posée sur la recopie de position ne serait
    /// éprouvable par aucun test — et une tolérance qu'on ne peut pas déclencher
    /// est une branche que personne n'exécute jamais.
    /// </summary>
    public bool ProchainEnregistrementEnConflit { get; set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Enregistrements++;
        return Task.FromResult(1);
    }

    /// <summary>
    /// N'INCRÉMENTE PAS `Enregistrements` QUAND ELLE REND `false`, parce que
    /// rien n'a été écrit. Un compteur qui monterait quand même ferait passer un
    /// test qui vérifie « la position a bien été recopiée ».
    /// </summary>
    public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (ProchainEnregistrementEnConflit)
        {
            ProchainEnregistrementEnConflit = false;
            return Task.FromResult(false);
        }

        Enregistrements++;
        return Task.FromResult(true);
    }
}

internal sealed class FauxPartageDeRecette : IDeliveryPayoutSettings
{
    public decimal DriverShareRate => 0.8m;
}
