namespace HBA.Shared.Infrastructure.Persistence;

// L'ENUM RESTE AU SOCLE, L'ENTITE EST DESCENDUE DANS LES SERVICES.
/// <summary>Ce qui est arrivé à une ligne.</summary>
public enum AuditOperation
{
    Created = 0,
    Updated = 1,
    Deleted = 2
}
