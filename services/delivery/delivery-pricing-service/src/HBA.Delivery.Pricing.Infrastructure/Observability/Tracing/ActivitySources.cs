using System.Diagnostics;

namespace HBA.Delivery.Pricing.Infrastructure.Observability.Tracing;

/// <summary>La source d'activite de ce service.</summary>
public static class SourcesDActivite
{
    public const string Nom = "HBA.Delivery.Pricing.Infrastructure";

    public static readonly ActivitySource Source = new(Nom);
}
