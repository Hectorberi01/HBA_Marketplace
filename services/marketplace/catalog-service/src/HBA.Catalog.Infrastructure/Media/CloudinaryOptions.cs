namespace HBA.Catalog.Infrastructure.Media;

/// <summary>
/// Configuration Cloudinary pour le TRAITEMENT des images produit (détourage IA +
/// fond blanc).
/// </summary>
public sealed class CloudinaryOptions
{
    public string CloudName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>Délai maximal d'attente (s) du rendu asynchrone avant abandon.</summary>
    public int MaxWaitSeconds { get; set; } = 25;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CloudName)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ApiSecret);
}
