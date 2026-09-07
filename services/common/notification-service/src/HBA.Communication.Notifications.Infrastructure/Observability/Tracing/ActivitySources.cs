using System.Diagnostics;

namespace HBA.Communication.Notifications.Infrastructure.Observability.Tracing;

/// <summary>La source d'activite de ce service.</summary>
public static class SourcesDActivite
{
    public const string Nom = "HBA.Communication.Notifications.Infrastructure";

    public static readonly ActivitySource Source = new(Nom);
}
