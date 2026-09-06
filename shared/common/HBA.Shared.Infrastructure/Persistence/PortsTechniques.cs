namespace HBA.Shared.Infrastructure.Persistence;

/// <summary>
/// Marque la table d'outbox d'un service.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI DES INTERFACES VIDES PLUTOT QUE LES ENTITES.
///
/// `OutboxMessage` et `ConsumerInboxEntry` ont quitte le socle : leurs tables
/// appartiennent aux services qui les creent. Mais `ModuleDbContext`, reste ici,
/// doit EXCLURE ces deux tables du journal d'audit — sans quoi journaliser produit
/// des lignes a journaliser, et le premier `SaveChanges` en production part en
/// boucle infinie.
///
/// Le socle ne peut donc plus ecrire `is not OutboxMessage`, et il ne doit surtout
/// pas filtrer sur un NOM DE CLASSE : un service qui renommerait son entite
/// retrouverait la boucle, sans qu'aucune compilation ne le signale. Deux
/// interfaces vides rendent l'exclusion verifiable par le compilateur.
///
/// C'est le meme raisonnement, et le meme fichier voisin, que `IEntreeDeJournal`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public interface IMessageDOutbox
{
}

/// <summary>Marque la table d'inbox d'un service. Voir <see cref="IMessageDOutbox"/>.</summary>
public interface IEntreeDInbox
{
}

/// <summary>
/// Marque la table d'idempotence d'un service. Voir <see cref="IMessageDOutbox"/>
/// pour le raisonnement : le socle doit l'exclure du journal d'audit, et il ne
/// peut plus nommer le type puisqu'il est descendu.
/// </summary>
public interface IEnregistrementDIdempotence
{
}
