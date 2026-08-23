using System.Diagnostics;

namespace HBA.Shared.Infrastructure.Observability;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES SOURCES D'INSTRUMENTATION MAISON DE LA PLATEFORME.
///
/// UNE SOURCE NON DÉCLARÉE À OPENTELEMETRY PRODUIT DES `Activity` NULLES.
///
/// C'est le piège de cette API, et il ne ressemble pas à une panne : le code
/// appelle `StartActivity`, obtient `null`, et continue parfaitement. Aucune
/// exception, aucun journal, aucune dégradation — simplement des spans qui
/// n'existent pas. On le découvre des semaines plus tard, en cherchant dans
/// Grafana une latence qui n'a jamais été mesurée. La passerelle porte déjà
/// l'encadré équivalent sur `BffTelemetry`.
///
/// D'où ces constantes plutôt que des chaînes écrites sur place : la source
/// déclarée dans `TelemetryExtensions` et celle qui émet sont forcément la même.
/// Un `AddSource("Hba.Kafka")` recopié à la main en `"HBA.Kafka"` suffirait à
/// rendre tout l'asynchrone invisible, sans rien casser.
///
/// POURQUOI CE FICHIER EST DANS `Infrastructure` ET NON DANS `Hosting`.
///
/// Parce que c'est `Infrastructure` qui ÉMET : le publieur et le consommateur
/// Kafka vivent ici. `Hosting` référence `Infrastructure`, jamais l'inverse — le
/// placer côté Hosting rendait la source inatteignable depuis le code qui doit
/// s'en servir.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class HbaTelemetry
{
    /// <summary>Publication et consommation d'événements d'intégration (§19).</summary>
    public const string KafkaSourceName = "Hba.Kafka";

    /// <summary>
    /// Source d'activités de la messagerie, partagée par le publieur et le
    /// consommateur. Statique : une `ActivitySource` est faite pour vivre aussi
    /// longtemps que le processus, en créer une par message coûterait plus que ce
    /// qu'elle mesure.
    /// </summary>
    public static readonly ActivitySource Kafka = new(KafkaSourceName);
}
