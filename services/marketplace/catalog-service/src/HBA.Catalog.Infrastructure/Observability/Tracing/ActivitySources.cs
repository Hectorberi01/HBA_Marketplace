using System.Diagnostics;

namespace HBA.Catalog.Infrastructure.Observability.Tracing;

/// <summary>La source d'activite de ce service.</summary>
public static class SourcesDActivite
{
    public const string Nom = "HBA.Catalog.Infrastructure";

    public static readonly ActivitySource Source = new(Nom);
}
