using System.ComponentModel.DataAnnotations;

namespace HBA.Gateway.Application.Bff;

/// <summary>Description, en configuration, des sections composant un écran agrégé.</summary>
public sealed class BffAggregationOptions
{
    public const string SectionName = "Bff";

    /// <summary>Écrans agrégés, indexés par identifiant (« client.express.home »).</summary>
    public Dictionary<string, List<BffSectionDefinition>> Screens { get; init; } = new();

    /// <summary>Délai maximal accordé à l'agrégation COMPLÈTE d'un écran.</summary>
    [Range(typeof(TimeSpan), "00:00:00.500", "00:01:00")]
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(8);
}

/// <summary>Une section d'écran agrégé.</summary>
public sealed class BffSectionDefinition
{
    /// <summary>Clé rendue au client. Stable même si le service cible change.</summary>
    [Required]
    public string Key { get; init; } = string.Empty;

    /// <summary>Clé du service à interroger — doit exister dans le registre.</summary>
    [Required]
    public string Service { get; init; } = string.Empty;

    /// <summary>Chemin relatif appelé sur ce service, décidé par l'opérateur.</summary>
    [Required]
    public string Path { get; init; } = string.Empty;
}
