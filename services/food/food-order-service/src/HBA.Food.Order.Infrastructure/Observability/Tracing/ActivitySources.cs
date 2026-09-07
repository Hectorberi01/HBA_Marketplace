using System.Diagnostics;

namespace HBA.FoodOrders.Infrastructure.Observability.Tracing;

/// <summary>La source d'activite de ce service.</summary>
public static class SourcesDActivite
{
    public const string Nom = "HBA.FoodOrders.Infrastructure";

    public static readonly ActivitySource Source = new(Nom);
}
