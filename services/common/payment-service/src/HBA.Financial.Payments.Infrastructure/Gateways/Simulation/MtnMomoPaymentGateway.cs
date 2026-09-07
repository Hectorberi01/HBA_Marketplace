namespace HBA.Financial.Payments.Infrastructure.Gateways.Simulation;

/// <summary>Adaptateur MTN Mobile Money (Collection API, stub sandbox).</summary>
public sealed class MtnMomoPaymentGateway : MobileMoneyPaymentGateway
{
    private readonly MtnMomoOptions _options;

    public MtnMomoPaymentGateway(MtnMomoOptions options) => _options = options;

    public override string Provider => "MtnMomo";

    protected override string ReferencePrefix => "MTN";
    protected override string WebhookSecret => _options.WebhookSecret;
}
