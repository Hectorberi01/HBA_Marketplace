using HBA.Delivery.Pricing.Domain.ValueObjects;

namespace HBA.Delivery.Pricing.Application.DTOs;

public sealed record CreateQuoteRequest(
    Guid? SellerId,
    Guid? StoreId,
    GeoPoint Pickup,
    GeoPoint Dropoff,
    int? DistanceMeters,
    int? DurationSeconds,
    string? VehicleType,
    string? ServiceLevel,
    long Discount,
    string? Currency);
