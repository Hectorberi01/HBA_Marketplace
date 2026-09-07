namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>
/// Contrat commun à tous les clients HTTP sortants de la passerelle.
/// </summary>
/// <remarks>
/// LES CHEMINS PASSÉS ICI NE DOIVENT JAMAIS VENIR DU CLIENT HTTP.
///
/// `relativePath` est destiné à des chemins décidés par le code ou par la
/// configuration de l'opérateur. Y brancher une valeur issue de la requête
/// entrante transformerait la passerelle en proxy ouvert vers le réseau interne :
/// n'importe qui pourrait alors atteindre `/actuator`, `/metrics` ou une route
/// d'administration d'un service que rien n'expose publiquement.
/// </remarks>
public interface IServiceClient
{
    /// <summary>Clé logique du service, telle qu'utilisée en configuration.</summary>
    string ServiceKey { get; }

    /// <summary>
    /// Exécute un GET et désérialise le corps en JSON. N'émet aucune exception
    /// pour un échec attendu (timeout, 5xx, circuit ouvert) : le résultat le porte.
    /// </summary>
    Task<ServiceResult> GetJsonAsync(string relativePath, CancellationToken cancellationToken);
}
