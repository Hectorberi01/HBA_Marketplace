namespace HBA.Financial.Payments.Infrastructure.Gateways;

/// <summary>Réglages Stripe (section de config « Payments:Stripe »).</summary>
public sealed class StripeOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Base de l'API Stripe (réel).</summary>
    public string BaseUrl { get; set; } = "https://api.stripe.com";

    /// <summary>URL de retour après paiement (Checkout) — succès / annulation.</summary>
    public string SuccessUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;

    /// <summary>Base d'URL simulant la page de paiement hébergée (stub).</summary>
    public string CheckoutBaseUrl { get; set; } = "https://checkout.stripe.com/pay";

    /// <summary>Vrai si la clé secrète est renseignée (sinon on reste en stub).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>Réglages PayPal (section de config « Payments:PayPal »).</summary>
public sealed class PayPalOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string WebhookId { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Base de l'API PayPal (réel ; sandbox par défaut).</summary>
    public string BaseUrl { get; set; } = "https://api-m.sandbox.paypal.com";

    /// <summary>URL de retour après approbation / annulation.</summary>
    public string ReturnUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;

    /// <summary>Base d'URL simulant la page d'approbation PayPal (stub).</summary>
    public string CheckoutBaseUrl { get; set; } = "https://www.sandbox.paypal.com/checkoutnow";

    /// <summary>Vrai si l'identifiant client et le secret sont renseignés (sinon stub).</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(Secret);
}

/// <summary>Réglages MTN Mobile Money (section « Payments:MtnMomo »).</summary>
public sealed class MtnMomoOptions
{
    public string SubscriptionKey { get; set; } = string.Empty;
    public string ApiUser { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Base de l'API Collection (sandbox : https://sandbox.momodeveloper.mtn.com).
    /// </summary>
    public string BaseUrl { get; set; } = "https://sandbox.momodeveloper.mtn.com";

    /// <summary>Environnement cible MoMo (« sandbox », ou « mtnbenin »… en prod).</summary>
    public string TargetEnvironment { get; set; } = "sandbox";

    /// <summary>URL de callback où MoMo notifie le statut (X-Callback-Url), optionnelle.</summary>
    public string CallbackUrl { get; set; } = string.Empty;

    /// <summary>Devise envoyée au PSP (UEMOA : XOF ; le sandbox MoMo accepte aussi EUR).</summary>
    public string Currency { get; set; } = "XOF";

    /// <summary>Vrai si les identifiants requis sont renseignés (sinon on reste en stub).</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SubscriptionKey)
        && !string.IsNullOrWhiteSpace(ApiUser)
        && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>Réglages FedaPay (section « Payments:FedaPay »).</summary>
public sealed class FedaPayOptions
{
    /// <summary>Clé secrète FedaPay (sk_sandbox_… / sk_live_…), envoyée en Bearer.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Secret de signature des webhooks FedaPay (vérif.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Base de l'API FedaPay (sandbox par défaut ; live :
    /// https://api.fedapay.com/v1).
    /// </summary>
    public string BaseUrl { get; set; } = "https://sandbox-api.fedapay.com/v1";

    /// <summary>URL de callback où FedaPay notifie / renvoie l'acheteur après paiement.</summary>
    public string CallbackUrl { get; set; } = string.Empty;

    /// <summary>Devise envoyée (UEMOA : XOF).</summary>
    public string Currency { get; set; } = "XOF";

    /// <summary>Active les VERSEMENTS (payouts vendeur) réels via FedaPay.</summary>
    public bool EnablePayouts { get; set; }

    /// <summary>Vrai si l'on parle à l'API bac à sable de FedaPay.</summary>
    public bool IsSandbox =>
        BaseUrl.Contains("sandbox", StringComparison.OrdinalIgnoreCase);

    /// <summary>Vrai si la clé et l'URL désignent le MÊME monde.</summary>
    public bool KeyMatchesEnvironment
    {
        get
        {
            var keyIsSandbox = ApiKey.StartsWith("sk_sandbox", StringComparison.OrdinalIgnoreCase);
            var keyIsLive = ApiKey.StartsWith("sk_live", StringComparison.OrdinalIgnoreCase);

            // Clé d'un format inconnu : on ne peut rien affirmer, on laisse passer.
            if (!keyIsSandbox && !keyIsLive) return true;

            return keyIsSandbox == IsSandbox;
        }
    }

    /// <summary>Les VERSEMENTS réels ne sont possibles qu'en LIVE.</summary>
    public bool CanPayout => IsConfigured && EnablePayouts && !IsSandbox && KeyMatchesEnvironment;

    /// <summary>Méthode de transfert FedaPay pour les payouts (champ « mode »).</summary>
    public string PayoutMode { get; set; } = "mtn_open";

    /// <summary>Vrai si la clé secrète est renseignée (sinon on reste en stub).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>Réglages Moov Money (section « Payments:Moov »).</summary>
public sealed class MoovOptions
{
    public string MerchantId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string Currency { get; set; } = "XOF";

    /// <summary>Base de l'API Moov (à aligner sur ton contrat / pays).</summary>
    public string BaseUrl { get; set; } = "https://api.moov-africa.com";

    /// <summary>Chemin du jeton OAuth (client_credentials), relatif à BaseUrl.</summary>
    public string TokenPath { get; set; } = "oauth/token";

    /// <summary>Chemin de création de paiement (RequestToPay), relatif à BaseUrl.</summary>
    public string PaymentPath { get; set; } = "v1/payments";

    /// <summary>URL de callback où Moov notifie le statut, optionnelle.</summary>
    public string CallbackUrl { get; set; } = string.Empty;

    /// <summary>Vrai si les identifiants requis sont renseignés (sinon on reste en stub).</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(MerchantId) && !string.IsNullOrWhiteSpace(ApiKey);
}
