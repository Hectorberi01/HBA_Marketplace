namespace HBA.Communication.Notifications.Infrastructure.Sms;

/// <summary>Configuration de l'envoi de SMS. Section : « Notifications:Sms ».</summary>
public sealed class SmsOptions
{
    /// <summary>Clé ou jeton d'API du fournisseur.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Identité d'expéditeur affichée sur le téléphone, ex.</summary>
    public string SenderId { get; set; } = string.Empty;

    /// <summary>Adresse de base de l'API du fournisseur retenu.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(SenderId)
        && !string.IsNullOrWhiteSpace(BaseUrl);
}
