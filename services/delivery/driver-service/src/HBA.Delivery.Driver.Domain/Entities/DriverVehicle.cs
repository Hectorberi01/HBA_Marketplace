using HBA.Delivery.Driver.Domain.Enums;
using HBA.Shared.Domain.Primitives;

namespace HBA.Delivery.Driver.Domain.Entities;

/// <summary>Le véhicule DÉCLARÉ par le livreur dans son dossier.</summary>
public sealed class DriverVehicle : Entity<Guid>
{
    private DriverVehicle(
        Guid id, Guid driverId, DriverVehicleType type,
        string? make, string? model, string? plate, decimal? capacityKg)
        : base(id)
    {
        DriverId = driverId;
        Type = type;
        Make = make;
        Model = model;
        Plate = plate;
        CapacityKg = capacityKg;
        Active = true;
        DeclaredAtUtc = DateTime.UtcNow;
    }

    // Requis par EF Core.
    private DriverVehicle()
    {
    }

    public Guid DriverId { get; private set; }

    public DriverVehicleType Type { get; private set; }

    public string? Make { get; private set; }

    public string? Model { get; private set; }

    /// <summary>Nulle pour un vélo ou un livreur à pied — voir `DeclareVehicle`.</summary>
    public string? Plate { get; private set; }

    public decimal? CapacityKg { get; private set; }

    public bool Active { get; private set; }

    public DateTime DeclaredAtUtc { get; private set; }

    internal static DriverVehicle Declare(
        Guid driverId, DriverVehicleType type,
        string? make, string? model, string? plate, decimal? capacityKg)
        => new(Guid.NewGuid(), driverId, type,
            string.IsNullOrWhiteSpace(make) ? null : make.Trim(),
            string.IsNullOrWhiteSpace(model) ? null : model.Trim(),
            plate,
            capacityKg);

    internal void Deactivate() => Active = false;
}
