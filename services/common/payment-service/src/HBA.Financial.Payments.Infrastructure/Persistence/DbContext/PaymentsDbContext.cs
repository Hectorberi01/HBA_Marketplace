using HBA.Shared.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Financial.Payments.Application.Abstractions;
using HBA.Financial.Payments.Domain.Payments;
using HBA.Financial.Payments.Domain.PaymentMethods;

using HBA.Financial.Payments.Infrastructure.Auditing;
using HBA.Financial.Payments.Infrastructure.Persistence.Outbox;
using HBA.Financial.Payments.Infrastructure.Persistence.Inbox;
using HBA.Financial.Payments.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Financial.Payments.Infrastructure.Persistence;

/// <summary>DbContext du module Payments (schéma « payments »).</summary>
public sealed class PaymentsDbContext : ModuleDbContext, IOutboxDbContext, IPaymentsUnitOfWork
{
    // ═════════════════════════════════════════════════════════════════════════
    // L'OUTBOX ET L'INBOX DE CE SERVICE — LEURS TABLES LUI APPARTIENNENT.
    //
    // Le socle draine la file d'evenements et exclut ces deux tables du journal
    // d'audit ; il ne connait plus ni l'une ni l'autre. Ces trois membres sont ce
    // qu'il appelle, et ils repondent avec les entites de `Persistence/`.
    // ═════════════════════════════════════════════════════════════════════════
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void ConfigurerLesTablesTechniques(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxConfiguration());
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());
    }

    protected override void AjouterAuOutbox(
        string type, string contenu, DateTime survenuLeUtc, string? traceParent, string? correlation)
        => OutboxMessages.Add(new OutboxMessage
        {
            Type = type,
            Content = contenu,
            OccurredOnUtc = survenuLeUtc,
            TraceParent = traceParent,
            CorrelationId = correlation
        });

    public const string SchemaName = "payments";

    public PaymentsDbContext(
        DbContextOptions<PaymentsDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>
    /// Les moyens de paiement enregistrés par les acheteurs.
    ///
    /// Ils vivaient dans Identity, qui répond à « qui peut se connecter ? ». Un
    /// numéro Mobile Money ne participe à aucune décision d'accès — et le cahier
    /// exclut explicitement User, dont le profil ne porte que des informations NON
    /// sensibles. Leur place est ici, auprès de l'intégration PSP qui les débitera.
    /// </summary>
    public DbSet<SavedPaymentMethod> SavedPaymentMethods => Set<SavedPaymentMethod>();

    /// <summary>Traces de consommation Kafka (§19.5) et requêtes idempotentes (§5).</summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    /// <summary>
    /// SUR CE SERVICE, L'IDEMPOTENCE N'EST PAS UN CONFORT.
    ///
    /// Une reprise réseau sur `POST /payments/intents` sans clé crée une SECONDE
    /// intention de paiement : le client est débité deux fois pour une commande,
    /// et rien ne relie les deux débits à la même intention.
    /// </summary>
    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    protected override string Schema => SchemaName;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE JOURNAL D'AUDIT EST ACTIF ICI (lot 7.1, ISSUE-042 / ISSUE-043).
    ///
    /// `KeepsAuditTrail` VALAIT `false` SUR VINGT ET UN CONTEXTES SUR VINGT-QUATRE.
    ///
    /// Ce qui n'y laissait AUCUNE trace : la capture d'un paiement et le
    /// remboursement — deux routes `RequireAdmin` qui déplacent de l'argent réel.
    ///
    /// `payment_refunds` et `payments` gardent l'ÉTAT ; elles ne disent pas QUI l'a
    /// changé. Un remboursement de 400 000 FCFA passé à tort ne laissait donc rien
    /// permettant de remonter à l'administrateur qui l'avait déclenché.
    ///
    /// Activé DANS LE MÊME COMMIT que la migration qui crée `payments.audit_entries` —
    /// l'inverse produirait une surcharge qui promet une table absente, et le défaut
    /// ne se verrait qu'au premier `SaveChanges` en production.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    protected override bool KeepsAuditTrail => true;

    // ═════════════════════════════════════════════════════════════════════════
    // LE JOURNAL D'AUDIT DE CE SERVICE — L'ENTITE ET SA TABLE LUI APPARTIENNENT.
    //
    // Le socle collecte les mutations, resout l'acteur et fixe l'instant unique de
    // la transaction ; il ne connait plus aucune table d'audit. Ces deux methodes
    // sont ce qu'il appelle, et elles repondent avec l'entite de `Auditing/`.
    // ═════════════════════════════════════════════════════════════════════════
    protected override void ConfigurerLeJournalDAudit(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfiguration(new AuditConfiguration());

    protected override void AjouterUneEntreeDAudit(
        string typeDEntite,
        string identifiant,
        AuditOperation operation,
        Guid? acteur,
        string typeDActeur,
        string? correlation,
        DateTime instantUtc)
        => Set<AuditEntry>().Add(new AuditEntry
        {
            EntityType = typeDEntite,
            EntityId = identifiant,
            Operation = operation,
            ActorUserId = acteur,
            ActorType = typeDActeur,
            CorrelationId = correlation,
            OccurredOnUtc = instantUtc
        });


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
        // Configurations du socle : autre assembly, le balayage ne les trouve pas.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyConfiguration());
        
        base.OnModelCreating(modelBuilder);
    }
}
