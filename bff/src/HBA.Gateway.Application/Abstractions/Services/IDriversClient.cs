namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>
/// Client sortant vers driver-service (le DOSSIER du livreur).
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// QUATORZIÈME CLIENT, ET IL MANQUAIT ALORS QUE SON ADRESSE ÉTAIT DÉJÀ LÀ.
///
/// `ServicesOptions.Drivers`, `ServiceKeys.Drivers` et la grappe `Drivers` de
/// `ReverseProxy` existaient toutes les trois : le RELAIS vers driver-service
/// fonctionnait. Seul le client SORTANT manquait — donc aucune agrégation BFF ne
/// pouvait interroger driver-service, alors même que la configuration donnait
/// toutes les apparences du contraire.
///
/// C'est le piège des trois endroits à tenir d'accord que `ServiceKeys` signale
/// déjà pour la grappe « Promotion » : ici il y en avait un quatrième, et il
/// était vide.
///
/// AUCUNE MÉTHODE TYPÉE, ET C'EST DÉLIBÉRÉ.
///
/// Ce client ne sert aujourd'hui qu'à `GetAdminQueuesHandler`, qui lit un nombre
/// par `IServiceClient.GetJsonAsync`. Déclarer ici un `ListPendingDriversAsync`
/// figerait dans la passerelle un DTO de dossier livreur que rien n'utilise —
/// exactement ce que `ServiceResult` explique refuser. Le typage viendra avec le
/// premier écran qui en a besoin.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public interface IDriversClient : IServiceClient;
