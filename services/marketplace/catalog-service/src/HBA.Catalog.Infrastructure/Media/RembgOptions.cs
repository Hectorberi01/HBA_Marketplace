namespace HBA.Catalog.Infrastructure.Media;

/// <summary>Configuration du détourage LOCAL, via un service rembg auto-hébergé.</summary>
public sealed class RembgOptions
{
    /// <summary>URL de base du service, réseau interne uniquement (ex.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Modèle de segmentation. Voir l'avertissement de licence ci-dessus.</summary>
    public string Model { get; set; } = "u2net";

    /// <summary>Délai maximal d'un détourage.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Qualité de réencodage JPEG (1-100).</summary>
    public int JpegQuality { get; set; } = 88;

    /// <summary>Modèles AUTORISÉS — liste blanche, pas liste noire.</summary>
    private static readonly string[] AllowedModels =
    [
        "u2net",
        "u2netp",
        "u2net_human_seg",
        "silueta",
    ];

    /// <summary>Modèle réellement envoyé au service.</summary>
    public string EffectiveModel
    {
        get
        {
            var candidate = (Model ?? string.Empty).Trim().ToLowerInvariant();
            return AllowedModels.Contains(candidate) ? candidate : "u2net";
        }
    }

    /// <summary>Vrai si un service de détourage local est adressable.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
