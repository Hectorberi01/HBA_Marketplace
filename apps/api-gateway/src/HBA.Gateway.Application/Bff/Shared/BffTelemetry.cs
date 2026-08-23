using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HBA.Gateway.Application.Bff.Shared;

/// <summary>
/// Source de traces et compteurs de la couche d'agrégation (§34, §43).
/// </summary>
/// <remarks>
/// DANS `Application`, ET SANS DÉPENDANCE À OpenTelemetry.
///
/// `ActivitySource` et `Meter` viennent de la bibliothèque de base : les émettre
/// n'engage aucun exportateur. L'abonnement se fait dans `OpenTelemetryExtensions`
/// via <see cref="Name"/> — si personne n'écoute, le coût est celui d'un test de
/// nullité.
///
/// C'est ce qui permet à Application de rester ignorante de l'infrastructure tout
/// en étant observable.
/// </remarks>
public static class BffTelemetry
{
    public const string Name = "HBA.Gateway.Bff";

    public static readonly ActivitySource Source = new(Name);

    private static readonly Meter Meter = new(Name);

    /// <summary>Durée d'une agrégation complète, par écran.</summary>
    public static readonly Histogram<double> RequestDuration =
        Meter.CreateHistogram<double>("bff_request_duration", "ms");

    /// <summary>Durée d'un appel de dépendance, par écran et par service.</summary>
    public static readonly Histogram<double> DependencyDuration =
        Meter.CreateHistogram<double>("bff_dependency_duration", "ms");

    /// <summary>
    /// Réponses rendues incomplètes.
    /// </summary>
    /// <remarks>
    /// Compteur SÉPARÉ de celui des échecs : une réponse partielle est un succès
    /// du point de vue du client — l'écran s'affiche. Les fondre masquerait le
    /// fait que la dégradation a bien joué son rôle.
    /// </remarks>
    public static readonly Counter<long> PartialResponses =
        Meter.CreateCounter<long>("bff_partial_response_total");

    /// <summary>Échecs de dépendance, quelle qu'en soit la criticité.</summary>
    public static readonly Counter<long> DependencyFailures =
        Meter.CreateCounter<long>("bff_dependency_failure_total");
}
