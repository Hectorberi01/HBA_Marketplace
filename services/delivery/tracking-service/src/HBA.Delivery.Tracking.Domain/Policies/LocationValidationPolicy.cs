using HBA.Delivery.Tracking.Domain.Entities;

namespace HBA.Delivery.Tracking.Domain.Policies;

public static class LocationValidationPolicy
{
    public static bool IsPlausible(LocationPoint point, DateTimeOffset now) =>
        point.AccuracyMeters is null or <= 150
        && point.SpeedMps is null or <= 45
        && point.CapturedAt <= now.AddMinutes(2);
}
