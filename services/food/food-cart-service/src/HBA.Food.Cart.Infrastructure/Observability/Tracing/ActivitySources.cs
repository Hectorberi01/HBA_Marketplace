using System.Diagnostics;

namespace HBA.FoodCarts.Infrastructure.Observability.Tracing;

/// <summary>La source d'activite de ce service.</summary>
public static class SourcesDActivite
{
    public const string Nom = "HBA.FoodCarts.Infrastructure";

    public static readonly ActivitySource Source = new(Nom);
}
