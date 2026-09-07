namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Contrat commun à tous les clients HTTP sortants de la passerelle.</summary>
public interface IServiceClient
{
    /// <summary>Clé logique du service, telle qu'utilisée en configuration.</summary>
    string ServiceKey { get; }

    /// <summary>
    /// Exécute un GET et désérialise le corps en JSON. N'émet aucune exception pour
    /// un échec attendu (timeout, 5xx, circuit ouvert) : le résultat le porte.
    /// </summary>
    Task<ServiceResult> GetJsonAsync(string relativePath, CancellationToken cancellationToken);
}
