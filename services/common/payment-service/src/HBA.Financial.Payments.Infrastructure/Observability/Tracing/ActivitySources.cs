using System.Diagnostics;

namespace HBA.Financial.Payments.Infrastructure.Observability.Tracing;

/// <summary>La source d'activite de ce service.</summary>
public static class SourcesDActivite
{
    public const string Nom = "HBA.Financial.Payments.Infrastructure";

    public static readonly ActivitySource Source = new(Nom);
}
