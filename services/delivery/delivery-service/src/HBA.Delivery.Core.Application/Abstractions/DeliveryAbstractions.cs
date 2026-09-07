using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using HBA.Shared.Application.Abstractions;

namespace HBA.Deliveries.Application.Abstractions;

/// <summary>Frontière transactionnelle du module Delivery.</summary>
public interface IDeliveryUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Persiste, et rend <c> false</c> au lieu de lever si la ligne a changé
    /// entre-temps.
    /// </summary>
    /// <returns>
    /// <c> true</c> si l'écriture a eu lieu ; <c> false</c> si un conflit de
    /// concurrence l'a empêchée.
    /// </returns>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>LA PART DU LIVREUR — RÉGLAGE, PAS CONSTANTE.</summary>
public interface IDeliveryPayoutSettings
{
    /// <summary>Part du prix revenant au livreur, entre 0 et 1.</summary>
    decimal DriverShareRate { get; }
}

/// <summary>Réglages du suivi de position.</summary>
public static class DriverLocation
{
    /// <summary>
    /// Au-delà, une position est considérée comme périmée et le livreur n'est plus
    /// proposé.
    /// </summary>
    public static readonly TimeSpan MaxPositionAge = TimeSpan.FromMinutes(2);
}

/// <summary>Un livreur en ligne, tel que le cache le connaît.</summary>
/// <param name="DriverId">Identifiant du livreur.</param>
/// <param name="Position">Dernière position transmise.</param>
/// <param name="ReportedAtUtc">Horodatage de cette position.</param>
public readonly record struct DriverPosition(DriverId DriverId, Coordinates Position, DateTime ReportedAtUtc);

/// <summary>LES POSITIONS DES LIVREURS NE VONT PAS EN BASE.</summary>
public interface IDriverLocationCache
{
    /// <summary>Enregistre la position courante d'un livreur.</summary>
    Task SetAsync(DriverId driverId, Coordinates position, CancellationToken cancellationToken = default);

    /// <summary>Retire un livreur du cache (passage hors ligne).</summary>
    Task RemoveAsync(DriverId driverId, CancellationToken cancellationToken = default);

    /// <summary>Position courante d'un livreur, si elle est fraîche.</summary>
    Task<DriverPosition?> GetAsync(DriverId driverId, CancellationToken cancellationToken = default);

    /// <summary>Livreurs présents dans un rayon, positions périmées exclues.</summary>
    Task<IReadOnlyList<DriverPosition>> FindNearbyAsync(
        Coordinates center,
        double radiusKm,
        int limit = 20,
        CancellationToken cancellationToken = default);
}
