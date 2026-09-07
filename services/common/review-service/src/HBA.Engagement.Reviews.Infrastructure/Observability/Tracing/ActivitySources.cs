using System.Diagnostics;

namespace HBA.Engagement.Reviews.Infrastructure.Observability.Tracing;

/// <summary>La source d'activite de ce service.</summary>
public static class SourcesDActivite
{
    public const string Nom = "HBA.Engagement.Reviews.Infrastructure";

    public static readonly ActivitySource Source = new(Nom);
}
