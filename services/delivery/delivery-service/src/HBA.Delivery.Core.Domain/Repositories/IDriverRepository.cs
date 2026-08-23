// DÉPLACÉ DEPUIS `driver-service/src/HBA.Delivery.Driver.Domain/Repositories`
// (lot 5.4, ISSUE-069). Ce contrat de lecture n'a qu'un implémenteur — le
// `EfDriverRepository` de delivery-service — et qu'un jeu d'appelants, les
// gestionnaires de delivery-service. Il suivait l'agrégat qu'il charge. Voir
// l'encadré en tête de `Aggregates/Driver/DeliveryDriver.cs`.

using HBA.Deliveries.Domain.Deliveries;

namespace HBA.Deliveries.Domain.Drivers;

/// <summary>Accès aux livreurs. L'implémentation vit en Infrastructure.</summary>
public interface IDriverRepository
{
    Task<Driver?> GetByIdAsync(DriverId id, CancellationToken cancellationToken = default);

    /// <summary>Retrouve le livreur rattaché à un compte utilisateur.</summary>
    Task<Driver?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Charge plusieurs livreurs d'un coup, à partir des identifiants renvoyés par
    /// le cache de positions. Un appel par livreur transformerait chaque dispatch
    /// en dizaines d'allers-retours en base, sur le chemin le plus sensible à la
    /// latence de toute l'application.
    /// </summary>
    Task<IReadOnlyList<Driver>> ListByIdsAsync(IReadOnlyCollection<DriverId> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Livreurs d'un état de compte donné, les plus anciens d'abord.
    ///
    /// SANS CETTE LECTURE, LA VÉRIFICATION EST INAPPLICABLE.
    ///
    /// Un livreur s'inscrit et attend. Personne ne reçoit d'alerte, et la seule
    /// façon de le retrouver serait de connaître son identifiant — que lui seul
    /// possède. Une route « vérifier ce livreur » sans route « qui attend ? » est
    /// un bouton sans liste : l'exploitation ne s'en sert jamais.
    ///
    /// L'ordre par ancienneté n'est pas cosmétique : celui qui attend depuis
    /// trois jours passe avant celui qui vient de s'inscrire.
    /// </summary>
    Task<IReadOnlyList<Driver>> ListByAccountStatusAsync(
        DriverAccountStatus status, int take = 100, CancellationToken cancellationToken = default);

    Task AddAsync(Driver driver, CancellationToken cancellationToken = default);
}
