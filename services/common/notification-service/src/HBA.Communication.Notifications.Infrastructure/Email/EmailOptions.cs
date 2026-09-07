namespace HBA.Communication.Notifications.Infrastructure.Email;

/// <summary>Configuration de l'envoi d'e-mails.</summary>
public sealed class EmailOptions
{
    /// <summary>Clé d'API Resend (« re_… »).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Expéditeur, ex. « HBA Express &lt;no-reply@hbaexpress.com&gt; ».</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Base publique des liens cliquables, ex.</summary>
    public string AppBaseUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(From)
        && !string.IsNullOrWhiteSpace(AppBaseUrl);

    /// <summary>
    /// Construit un lien absolu propre, quelle que soit la présence d'un « / »
    /// final.
    /// </summary>
    public string Link(string path)
        => $"{AppBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
}
