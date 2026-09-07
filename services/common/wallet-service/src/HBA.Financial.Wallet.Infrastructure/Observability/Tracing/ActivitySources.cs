using System.Diagnostics;

namespace HBA.Financial.Wallet.Infrastructure.Observability.Tracing;

/// <summary>La source d'activite de ce service.</summary>
public static class SourcesDActivite
{
    public const string Nom = "HBA.Financial.Wallet.Infrastructure";

    public static readonly ActivitySource Source = new(Nom);
}
