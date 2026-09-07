using HBA.Shared.Infrastructure.Persistence;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Audit`.

namespace HBA.Drivers.Infrastructure.Auditing;



/// <summary>LE JOURNAL DE QUI A FAIT QUOI — UNE LIGNE PAR ENTITÉ MUTÉE, PAR REQUÊTE.</summary>
internal sealed class AuditEntry : IEntreeDeJournal
{
    public long Id { get; set; }

    /// <summary>Nom court de l'entité mutée, ex.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Clé primaire de la ligne, sous forme textuelle.</summary>
    public string EntityId { get; set; } = string.Empty;

    public AuditOperation Operation { get; set; }

    /// <summary>L'acteur, repris de <c>HbaRequestContext.Current.Actor</c>.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary><c>CUSTOMER</c>, <c>SELLER</c>, <c>ADMIN</c>, <c>SYSTEM</c>… (§19.1).</summary>
    public string ActorType { get; set; } = "SYSTEM";

    /// <summary>Le fil du geste, repris du contexte de requête.</summary>
    public string? CorrelationId { get; set; }

    public DateTime OccurredOnUtc { get; set; }
}
