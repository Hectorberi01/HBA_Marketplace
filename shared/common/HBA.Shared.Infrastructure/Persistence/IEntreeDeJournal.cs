namespace HBA.Shared.Infrastructure.Persistence;

/// <summary>
/// Marque l'entite de journal d'audit d'un service.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI UNE INTERFACE VIDE PLUTOT QU'UN TYPE PARTAGE.
///
/// `AuditEntry` a quitte le socle : sa table appartient au service qui la cree.
/// Mais la COLLECTE des mutations, restee ici, doit exclure les lignes de journal
/// elles-memes — sans quoi journaliser produirait des lignes a journaliser, et le
/// premier `SaveChanges` en production partirait en boucle infinie.
///
/// Le socle ne peut donc plus ecrire `is not AuditEntry`, et il ne doit pas
/// filtrer sur un NOM DE CLASSE : un service qui renommerait son entite
/// retrouverait la boucle, sans qu'aucune compilation ne le signale. Une interface
/// vide, implementee par chaque entite locale, rend l'exclusion verifiable par le
/// compilateur.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public interface IEntreeDeJournal
{
}
