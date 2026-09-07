using HBA.Financial.Payments.Application.Abstractions.Gateways;

namespace HBA.Financial.Payments.Infrastructure.Gateways.Simulation;

/// <summary>Stub de reversement : simule un dépôt sans toucher le réseau.</summary>
public sealed class SimulatedPayoutGateway : IPayoutGateway
{
    public Task<PayoutResult> SendAsync(PayoutInstruction instruction, CancellationToken cancellationToken = default)
        => Task.FromResult(PayoutResult.Accepted($"sim_payout_{Guid.NewGuid():N}"));

    /// <summary>En simulation, un dépôt demandé est toujours considéré comme arrivé.</summary>
    public Task<PayoutStatusResult> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default)
        => Task.FromResult(new PayoutStatusResult(PayoutStatus.Sent, null));

    /// <summary>
    /// Sans PSP réel, aucun webhook de dépôt n'arrive : tout payload est «
    /// non-dépôt » et repart vers le flux paiement.
    /// </summary>
    public PayoutWebhookEvent ParseWebhook(string rawBody, string? signatureHeader)
        => PayoutWebhookEvent.NotPayout;
}
