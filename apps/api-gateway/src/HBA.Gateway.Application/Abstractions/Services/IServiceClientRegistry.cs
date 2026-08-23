namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>
/// Résout un client sortant à partir de sa clé logique (« Catalog », « Food »…).
/// </summary>
/// <remarks>
/// CE N'EST PAS UN SERVICE LOCATOR, ET LA NUANCE EST TESTABLE.
///
/// Un service locator résout N'IMPORTE QUEL type depuis le conteneur, ce qui rend
/// les dépendances d'une classe invisibles à la lecture comme au test. Ce
/// registre ne résout qu'un ensemble FERMÉ de treize clients, il est injecté
/// explicitement, et il est substituable par un dictionnaire en mémoire dans un
/// test — sans conteneur.
///
/// Il existe parce que les sections d'agrégation BFF sont déclarées en
/// configuration : l'opérateur écrit `"service": "Catalog"`, et il faut bien
/// passer de cette chaîne à un client. L'alternative — un `switch` sur treize
/// cas recopié dans chaque agrégateur — déplacerait le problème sans le résoudre.
/// </remarks>
public interface IServiceClientRegistry
{
    /// <summary>
    /// Retourne le client correspondant, ou <c>null</c> si la clé est inconnue —
    /// cas d'une configuration erronée, qui doit produire une section en échec
    /// et non une exception au milieu d'une agrégation.
    /// </summary>
    IServiceClient? Find(string serviceKey);

    /// <summary>Clés connues, pour la validation de configuration au démarrage.</summary>
    IReadOnlyCollection<string> KnownKeys { get; }
}
