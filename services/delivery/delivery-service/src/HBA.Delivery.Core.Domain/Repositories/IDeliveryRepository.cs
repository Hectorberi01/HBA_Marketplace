namespace HBA.Deliveries.Domain.Deliveries;

/// <summary>Accès aux courses. L'implémentation vit en Infrastructure.</summary>
public interface IDeliveryRepository
{
    Task<Delivery?> GetByIdAsync(DeliveryId id, CancellationToken cancellationToken = default);

    /// <summary>Retrouve une course par la référence du donneur d'ordre.</summary>
    Task<Delivery?> GetByReferenceAsync(string reference, DeliverySource source, CancellationToken cancellationToken = default);

    /// <summary>Courses en attente d'un livreur.</summary>
    Task<IReadOnlyList<Delivery>> ListAwaitingDriverAsync(int take = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Courses dont la proposition en cours n'a pas reçu de réponse depuis plus de
    /// <paramref name="offerTimeout"/> .
    /// </summary>
    Task<IReadOnlyList<Delivery>> ListStaleOffersAsync(
        TimeSpan offerTimeout, int take = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Courses PROGRAMMÉES dont la fenêtre de dispatch vient de s'ouvrir, et qui
    /// dorment encore en « Pending ».
    /// </summary>
    Task<IReadOnlyList<Delivery>> ListScheduledDueAsync(
        DateTime nowUtc, int take = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Le travail EN COURS d'un livreur : la proposition qui l'attend, et la course
    /// qu'il a acceptée mais pas encore remise.
    /// </summary>
    Task<IReadOnlyList<Delivery>> ListActiveForDriverAsync(
        DriverId driverId, CancellationToken cancellationToken = default);

    Task AddAsync(Delivery delivery, CancellationToken cancellationToken = default);
}
