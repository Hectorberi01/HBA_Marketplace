namespace HBA.Deliveries.Domain.Deliveries;

/// <summary>Accès aux courses. L'implémentation vit en Infrastructure.</summary>
public interface IDeliveryRepository
{
    Task<Delivery?> GetByIdAsync(DeliveryId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrouve une course par la référence du donneur d'ordre.
    ///
    /// Sert à l'IDEMPOTENCE de l'API partenaire : un site marchand qui rejoue sa
    /// requête après un timeout ne doit pas créer une seconde course pour la même
    /// commande — le livreur se déplacerait deux fois, et le partenaire serait
    /// facturé deux fois.
    /// </summary>
    Task<Delivery?> GetByReferenceAsync(string reference, DeliverySource source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Courses en attente d'un livreur. Alimente la boucle de dispatch.
    /// Inclut <see cref="DeliveryStatus.NoDriverAvailable"/> : ces courses restent
    /// reprenables et doivent être réessayées, pas oubliées.
    /// </summary>
    Task<IReadOnlyList<Delivery>> ListAwaitingDriverAsync(int take = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Courses dont la proposition en cours n'a pas reçu de réponse depuis plus de
    /// <paramref name="offerTimeout"/>.
    ///
    /// Sans ce relevé, une course proposée à un livreur qui a posé son téléphone
    /// resterait bloquée pour toujours : l'agrégat n'a pas d'horloge, et personne
    /// d'autre ne viendrait constater le silence.
    /// </summary>
    Task<IReadOnlyList<Delivery>> ListStaleOffersAsync(
        TimeSpan offerTimeout, int take = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Courses PROGRAMMÉES dont la fenêtre de dispatch vient de s'ouvrir, et qui
    /// dorment encore en « Pending ».
    ///
    /// Sans ce relevé, une course programmée resterait « Pending » pour toujours :
    /// la création ne la met plus en recherche — c'est tout l'intérêt d'un créneau —
    /// et rien d'autre ne viendrait constater que l'heure est venue.
    /// </summary>
    Task<IReadOnlyList<Delivery>> ListScheduledDueAsync(
        DateTime nowUtc, int take = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Le travail EN COURS d'un livreur : la proposition qui l'attend, et la
    /// course qu'il a acceptée mais pas encore remise.
    ///
    /// SANS CETTE LECTURE, LE LIVREUR NE SAIT PAS QU'ON LUI PROPOSE UNE COURSE.
    ///
    /// Le dispatch attribuait, attendait 45 secondes, expirait, recommençait cinq
    /// fois, puis déclarait « aucun livreur disponible » — pendant que des livreurs
    /// en ligne regardaient un écran vide. Aucune route ne leur montrait quoi que
    /// ce soit : <c>/api/deliveries/mine</c> ne contenait que des POST.
    ///
    /// La notification poussée reste le chemin rapide, mais elle peut se perdre —
    /// téléphone en veille, jeton expiré, réseau coupé. Cette lecture est le
    /// chemin FIABLE : l'application interroge, et voit.
    ///
    /// L'historique n'y figure pas : c'est un écran de travail, pas un relevé.
    /// </summary>
    Task<IReadOnlyList<Delivery>> ListActiveForDriverAsync(
        DriverId driverId, CancellationToken cancellationToken = default);

    Task AddAsync(Delivery delivery, CancellationToken cancellationToken = default);
}
