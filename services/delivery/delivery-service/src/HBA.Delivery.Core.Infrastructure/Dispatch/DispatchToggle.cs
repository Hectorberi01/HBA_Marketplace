using HBA.Deliveries.Infrastructure.Persistence.Outbox;
using HBA.Deliveries.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Deliveries.Infrastructure.Dispatch;

/// <summary>QUEL PROCESSUS A LE DROIT DE DISPATCHER.</summary>
public static class DispatchToggle
{
    public static bool Enabled
    {
        get
        {
            var flag = Environment.GetEnvironmentVariable("DISPATCH_ENABLED");

            return string.IsNullOrWhiteSpace(flag)
                ? DrainageDOutbox.Actif
                : !string.Equals(flag, "false", StringComparison.OrdinalIgnoreCase);
        }
    }
}
