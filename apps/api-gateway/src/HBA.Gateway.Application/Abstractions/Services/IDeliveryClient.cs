using HBA.Gateway.Application.Contracts.Delivery;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>delivery-service</c>.</summary>
public interface IDeliveryClient : IServiceClient
{
    /// <summary>
    /// <c>GET /api/deliveries/drivers/me</c> — AUTHENTIFIÉ, résout le compte
    /// livreur depuis le jeton.
    /// </summary>
    /// <remarks>
    /// C'EST LA PIÈCE QUI REND LE DASHBOARD LIVREUR POSSIBLE.
    ///
    /// Le jeton porte un <c>userId</c> ; financial-service exige un
    /// <c>driverId</c>. Sans cette route, l'écran « gains » n'avait aucun moyen
    /// d'exister. Elle a été ajoutée à delivery-service pour ce BFF.
    /// </remarks>
    Task<ServiceResult<DriverAccount>> GetMyDriverAccountAsync(CancellationToken cancellationToken);

    /// <summary><c>GET /api/deliveries/drivers/me/missions</c> — AUTHENTIFIÉ.</summary>
    /// <remarks>
    /// AUCUNE PAGINATION NI FILTRE DE STATUT CÔTÉ SERVICE.
    ///
    /// La route rend toutes les missions du livreur. Le tri et la sélection se
    /// font donc dans la passerelle, sur une liste dont la taille croît avec
    /// l'ancienneté du compte — c'est-à-dire chez les livreurs les plus fidèles.
    ///
    /// Manque à combler : <c>?status=&amp;page=</c>.
    /// </remarks>
    Task<ServiceResult<IReadOnlyList<DriverMission>>> ListMyMissionsAsync(
        CancellationToken cancellationToken);
}
