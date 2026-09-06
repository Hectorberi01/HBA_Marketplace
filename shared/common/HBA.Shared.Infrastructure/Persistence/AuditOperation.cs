namespace HBA.Shared.Infrastructure.Persistence;

// ═════════════════════════════════════════════════════════════════════════════
// L'ENUM RESTE AU SOCLE, L'ENTITE EST DESCENDUE DANS LES SERVICES.
//
// Ce n'est pas une exception a la regle, c'est la regle : ce type fait partie de
// la SIGNATURE du point d'extension `AjouterUneEntreeDAudit`. Le dupliquer par
// service donnerait quatorze enums distincts pour une meme colonne, et deux
// journaux ne se compareraient plus.
// ═════════════════════════════════════════════════════════════════════════════
/// <summary>Ce qui est arrivé à une ligne.</summary>
public enum AuditOperation
{
    Created = 0,
    Updated = 1,
    Deleted = 2
}
