using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Results;

namespace HBA.Merchants.Application.Abstractions;

/// <summary>Unit of Work propre au module Sellers (évite la collision DI inter-modules).</summary>
public interface ISellerUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Sérialise les mutations d'équipe d'un même vendeur, pour la durée de la
    /// transaction en cours.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// IL EXISTE PARCE QUE LE VERROU OPTIMISTE NE POUVAIT PAS VOIR LA COURSE.
    ///
    /// L'invariant « un vendeur garde toujours au moins un propriétaire actif » est
    /// vérifié par une LECTURE (`CountActiveOwnersAsync`) suivie d'une ÉCRITURE sur
    /// une AUTRE ligne. Deux révocations simultanées touchent deux lignes
    /// différentes : `xmin`, qui est un jeton PAR LIGNE, ne détecte aucun conflit.
    /// Les deux réussissent, et le dossier tombe à zéro propriétaire — état dont il
    /// ne se relève pas, puisque toucher un propriétaire exige d'en être un.
    ///
    /// C'est la forme classique du « lire puis décider » : aucun verrou optimiste ne
    /// l'attrape, parce qu'il n'y a rien de commun à comparer.
    ///
    /// CONSULTATIF, ET PRIS SUR LE VENDEUR — PAS SUR UNE LIGNE.
    ///
    /// `SELECT … FOR UPDATE` supposerait une ligne partagée par les deux
    /// transactions ; il n'y en a pas. Le verrou porte donc sur un entier dérivé du
    /// `sellerId`, ce que PostgreSQL sait faire sans table dédiée. Deux commerçants
    /// différents ne s'attendent jamais.
    ///
    /// RELÂCHÉ PAR LA TRANSACTION, JAMAIS À LA MAIN.
    ///
    /// La variante `_xact_` est libérée au `COMMIT` comme au `ROLLBACK`. La variante
    /// de session exigerait un `pg_advisory_unlock` explicite — donc un `finally`,
    /// donc un chemin d'exception qui laisserait le verrou posé et bloquerait toute
    /// l'équipe de ce vendeur jusqu'au redémarrage du service.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// ET C'EST EXACTEMENT POUR CELA QUE LE VERROU N'EST PLUS EXPOSÉ SEUL.
    ///
    /// La signature précédente était `Task LockSellerAsync(Guid, …)`, appelée au
    /// milieu d'un handler. Elle N'A JAMAIS RIEN VERROUILLÉ.
    ///
    /// `pg_advisory_xact_lock` se relâche à la fin de la transaction. Sans
    /// transaction ouverte, PostgreSQL traite l'instruction comme sa propre
    /// transaction : il prend le verrou, valide, et le relâche — avant même que le
    /// handler ait lu quoi que ce soit. EF n'ouvre la sienne qu'au
    /// `SaveChangesAsync`, donc bien plus tard.
    ///
    /// L'ancien encadré affirmait « en production, l'intercepteur de transaction du
    /// module encadre la commande — c'est là que le verrou mord ». Cet intercepteur
    /// n'existe pas : il n'y a pas un seul `BeginTransactionAsync` dans le dépôt.
    /// Trois appelants s'appuyaient dessus, dont le transfert de propriété vendeur.
    ///
    /// Le verrou est donc PRIS ET RELÂCHÉ PAR CETTE MÉTHODE, autour de l'opération
    /// entière. Il devient impossible de le prendre hors transaction, parce qu'il
    /// n'est plus possible de le prendre séparément — c'est le seul remède durable
    /// à un piège qu'un appelant ne peut pas voir depuis son propre fichier.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    /// <param name="operation">
    /// Le travail à mener sous verrou : lectures, décision, et le
    /// <c>SaveChangesAsync</c> qui la persiste.
    /// </param>
    /// <returns>
    /// Le résultat de l'opération.
    ///
    /// UN ÉCHEC ANNULE LA TRANSACTION. Les trois appelants rendent leur échec
    /// AVANT d'écrire — l'annulation ne leur retire donc rien. Si un futur appelant
    /// veut persister quelque chose ET rendre un échec, il ne doit pas passer par
    /// ici : ce contrat dit « une opération refusée ne laisse aucune trace ».
    /// </returns>
    Task<Result> ExecuteUnderSellerLockAsync(
        Guid sellerId,
        Func<CancellationToken, Task<Result>> operation,
        CancellationToken cancellationToken = default);
}
