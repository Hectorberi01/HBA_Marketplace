namespace HBA.Gateway.Application.Bff.Admin;

/// <summary>Les files d'attente d'administration, en un seul appel.</summary>
/// <param name="Files">Une entrée par file, dans l'ordre où l'écran les présente.</param>
public sealed record AdminQueuesDto(IReadOnlyList<AdminQueueDto> Files);

/// <summary>Une file d'attente d'administration.</summary>
/// <param name="Cle">
/// Identifiant STABLE et fermé — `kyb`, `produits`, `marques`, `restaurants`,
/// `livreurs`, `commandes-arbitrage`, `commandes-echec`, `comptes`, `factures`,
/// `stock`.
/// </param>
/// <param name="Libelle">Ce que l'administrateur lit.</param>
/// <param name="Total">
/// Nombre d'éléments en attente, ou <c> null</c> si le service n'a pas répondu.
/// </param>
/// <param name="Approximatif">Le total est-il plafonné plutôt qu'exact ?</param>
public sealed record AdminQueueDto(
    string Cle,
    string Libelle,
    int? Total,
    bool Approximatif);
