using HBA.Delivery.Pricing.Domain.ValueObjects;

namespace HBA.Delivery.Pricing.Application.DTOs;

public sealed record ServiceabilityRequest(GeoPoint Pickup, GeoPoint Dropoff);

public sealed record Serviceability(bool Serviceable, int DistanceMeters, string? Reason);
