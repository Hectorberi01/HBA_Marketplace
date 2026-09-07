using System.Text.Json;
using HBA.Financial.Payments.Application.Abstractions.Gateways;

namespace HBA.Financial.Payments.Infrastructure.Gateways.Simulation;

/// <summary>Base des adaptateurs Mobile Money (MTN MoMo, Moov) en mode stub sandbox.</summary>
public abstract class MobileMoneyPaymentGateway : SimulatedPaymentGateway
{
    /// <summary>Le Mobile Money exige le numéro du payeur (MSISDN).</summary>
    public override bool RequiresPayerPhone => true;

    /// <summary>Préfixe de la référence RequestToPay.</summary>
    protected abstract string ReferencePrefix { get; }

    // Les notions « checkout hébergé » / base d'URL ne s'appliquent pas au Mobile
    // Money.
    protected override string CheckoutPrefix => ReferencePrefix;
    protected override string IntentPrefix => ReferencePrefix;
    protected override string CheckoutBaseUrl => string.Empty;

    // Le payload de callback Mobile Money porte le statut dans « status ».
    protected override string EventTypeField => "status";

    public override Task<GatewaySession> CreateCheckoutAsync(GatewayChargeContext context, CancellationToken cancellationToken = default)
        => RequestToPayAsync(context);

    public override Task<GatewaySession> CreatePaymentIntentAsync(GatewayChargeContext context, CancellationToken cancellationToken = default)
        => RequestToPayAsync(context);

    private Task<GatewaySession> RequestToPayAsync(GatewayChargeContext context)
    {
        // Token

        // TODO réel : POST RequestToPay (montant, devise, MSISDN) ; la référence
        // est l'X-Reference-Id (MoMo) / la transaction id (Moov).
        var reference = $"{ReferencePrefix}-{Guid.NewGuid():N}";
        // Ni redirection ni client secret : l'acheteur approuve sur son téléphone,
        // le client interroge ensuite /return/{id} ou attend le webhook.
        return Task.FromResult(new GatewaySession(reference, RedirectUrl: null, ClientSecret: null));
    }

    // `GetAccessTokenAsync` A ÉTÉ RETIRÉE D'ICI. ELLE ÉTAIT MORTE, ET FAUSSE.

    public override Task<GatewayEvent> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default)
        // TODO réel : GET .../requesttopay/{referenceId} et lire « status ».
        => Task.FromResult(new GatewayEvent(Verified: true, GatewayOutcome.Captured, providerReference, null));

    protected override GatewayOutcome MapOutcome(string eventType) => eventType.ToUpperInvariant() switch
    {
        "SUCCESSFUL" or "SUCCESS" or "COMPLETED" => GatewayOutcome.Captured,
        "FAILED" or "REJECTED" or "TIMEOUT" or "EXPIRED" or "CANCELLED" => GatewayOutcome.Failed,
        "REFUNDED" => GatewayOutcome.Refunded,
        "PENDING" or "ONGOING" => GatewayOutcome.Pending,
        _ => GatewayOutcome.Ignored
    };

    protected override string? ExtractReference(JsonElement root)
    {
        // Payload de test / natif : referenceId (MoMo) ou externalId, sinon le
        // champ générique.
        if (root.TryGetProperty("referenceId", out var refId))
        {
            return refId.GetString();
        }

        if (root.TryGetProperty("externalId", out var extId))
        {
            return extId.GetString();
        }

        return root.TryGetProperty("providerReference", out var direct) ? direct.GetString() : null;
    }
}


