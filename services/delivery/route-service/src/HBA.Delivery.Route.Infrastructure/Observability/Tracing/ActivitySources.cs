using System.Diagnostics;

namespace HBA.Routes.Infrastructure.Observability.Tracing;

/// <summary>La source d'activite de ce service.</summary>
public static class SourcesDActivite
{
    public const string Nom = "HBA.Routes.Infrastructure";

    public static readonly ActivitySource Source = new(Nom);
}
