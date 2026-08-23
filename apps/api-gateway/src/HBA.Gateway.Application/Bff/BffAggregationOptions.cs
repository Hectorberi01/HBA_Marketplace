using System.ComponentModel.DataAnnotations;

namespace HBA.Gateway.Application.Bff;

/// <summary>
/// Description, en configuration, des sections composant un écran agrégé.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI LES SECTIONS SONT CONFIGURÉES ET NON CODÉES EN DUR.
///
/// Aucun des treize services n'existe encore. Écrire aujourd'hui
/// `catalog.GetFeaturedProductsAsync(limit: 10)` supposerait connaître un chemin,
/// un nom de paramètre et une forme de réponse qu'aucune équipe n'a arrêtés.
/// Ce serait exactement l'invention de contrat qu'il faut éviter.
///
/// La configuration déplace cette décision hors du code : l'opérateur déclare les
/// chemins RÉELS le jour où ils existent, sans recompilation. Quand un contrat est
/// stabilisé, sa section peut être remplacée par un agrégateur typé.
///
/// LE CHEMIN VIENT DE L'OPÉRATEUR, JAMAIS DU CLIENT HTTP.
///
/// Le distinguo est la seule chose qui sépare ce mécanisme d'un proxy ouvert vers
/// le réseau interne. Aucune valeur issue de la requête entrante n'alimente
/// `Path` — et aucune ne doit jamais le faire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class BffAggregationOptions
{
    public const string SectionName = "Bff";

    /// <summary>Écrans agrégés, indexés par identifiant (« client.express.home »).</summary>
    public Dictionary<string, List<BffSectionDefinition>> Screens { get; init; } = new();

    /// <summary>
    /// Délai maximal accordé à l'agrégation COMPLÈTE d'un écran.
    /// </summary>
    /// <remarks>
    /// Les sections partent en parallèle : ce délai n'est donc pas la somme des
    /// délais unitaires. Passé ce cap, les sections encore en vol sont abandonnées
    /// et rendues indisponibles — le client reçoit une réponse partielle plutôt
    /// qu'une attente que son propre délai finira de toute façon par couper.
    /// </remarks>
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
