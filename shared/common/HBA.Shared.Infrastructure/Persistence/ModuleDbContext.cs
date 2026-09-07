using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Context;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Serialization;

using HBA.Shared.Infrastructure.Events;
namespace HBA.Shared.Infrastructure.Persistence;

/// <summary>Base de tous les DbContext de module.</summary>
public abstract class ModuleDbContext : DbContext, IUnitOfWork
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly IntegrationEventQueue _integrationEventQueue;

    protected ModuleDbContext(
        DbContextOptions options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options)
    {
        _domainEventDispatcher = domainEventDispatcher;
        _integrationEventQueue = integrationEventQueue;
    }

    /// <summary>Nom du schéma PostgreSQL propre au module (ex : « catalog »).</summary>
    protected abstract string Schema { get; }

    /// <summary>Ce module tient-il un journal d'audit (§37) ?</summary>
    protected virtual bool KeepsAuditTrail => false;

    /// <summary>Declare la table d'audit DU SERVICE. Vide par defaut.</summary>
    protected virtual void ConfigurerLeJournalDAudit(ModelBuilder modelBuilder)
    {
    }

    /// <summary>Ecrit UNE ligne de journal, avec l'entite du service.</summary>
    protected virtual void AjouterUneEntreeDAudit(
        string typeDEntite,
        string identifiant,
        AuditOperation operation,
        Guid? acteur,
        string typeDActeur,
        string? correlation,
        DateTime instantUtc)
    {
    }

    // LA TABLE D'OUTBOX A QUITTE CETTE CLASSE.
    protected virtual void ConfigurerLesTablesTechniques(ModelBuilder modelBuilder)
    {
    }

    /// <summary>Ajoute une ligne a l'outbox DU SERVICE. Vide par defaut.</summary>
    protected virtual void AjouterAuOutbox(
        string type, string contenu, DateTime survenuLeUtc, string? traceParent, string? correlation)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        ConfigurerLesTablesTechniques(modelBuilder);

        if (KeepsAuditTrail)
        {
            // LE SOCLE NE CONNAIT PLUS AUCUNE TABLE D'AUDIT.
            ConfigurerLeJournalDAudit(modelBuilder);
        }

        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 1. Dispatch des domain events : les handlers peuvent mettre des
        // integration events en file.
        await DispatchDomainEventsAsync(cancellationToken);

        // 2. Draine la file vers l'outbox LOCAL (ce DbContext, ce schéma), de sorte
        // que l'event et le changement d'état soient persistés ensemble.
        DrainIntegrationEventsToOutbox();

        // 3. Journalise QUI a muté QUOI, dans la même transaction.
        RecordAuditTrail();

        // 4. Estampille `UpdatedAtUtc` sur les entités qui la déclarent.
        HorodaterLesModifications();

        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Pose l'instant courant sur `UpdatedAtUtc`, pour les entités qui l'ont
    /// déclarée via <see cref="HorodatageExtensions.HorodateLesModifications{T}"/>
    /// .
    /// </summary>
    private void HorodaterLesModifications()
    {
        var maintenant = DateTime.UtcNow;

        foreach (var entree in ChangeTracker.Entries())
        {
            if (entree.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            if (entree.Metadata.FindProperty(HorodatageExtensions.ColonneModification) is null)
            {
                continue;
            }

            entree.Property(HorodatageExtensions.ColonneModification).CurrentValue = maintenant;
        }
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        var aggregates = ChangeTracker
            .Entries<IHasDomainEvents>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();

        var domainEvents = aggregates
            .SelectMany(aggregate => aggregate.DomainEvents)
            .ToList();

        aggregates.ForEach(aggregate => aggregate.ClearDomainEvents());

        await _domainEventDispatcher.DispatchAsync(domainEvents, cancellationToken);
    }

    private void DrainIntegrationEventsToOutbox()
    {
        foreach (var integrationEvent in _integrationEventQueue.DequeueAll())
        {
            AjouterAuOutbox(
                EventTypeName.Of(integrationEvent.GetType()),
                JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), SerializerOptions),
                integrationEvent.OccurredOnUtc,

                // LES DEUX CHEMINS D'ENFILEMENT DOIVENT LE FAIRE.
                System.Diagnostics.Activity.Current?.Id,

                // CAPTUREE ICI, PARCE QU'APRES IL EST TROP TARD. L'outbox est une
                // frontiere asynchrone : le message part plusieurs secondes plus
                // tard, dans un service d'arriere-plan qui n'a plus rien de la
                // requete d'origine.
                string.IsNullOrWhiteSpace(HbaRequestContext.Current.CorrelationId)
                    ? null
                    : HbaRequestContext.Current.CorrelationId);
        }
    }

    /// <summary>Écrit une ligne de journal par entité mutée.</summary>
    private void RecordAuditTrail()
    {
        if (!KeepsAuditTrail)
        {
            return;
        }

        // MATÉRIALISÉ AVANT LA BOUCLE, PAS ÉNUMÉRÉ PENDANT.
        var mutations = ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            // LE JOURNAL NE JOURNALISE PAS LA PLOMBERIE.
            .Where(entry => entry.Entity
                is not IEntreeDeJournal
                and not IMessageDOutbox
                and not IEntreeDInbox
                and not IEnregistrementDIdempotence)

            // LES TYPES POSSÉDÉS SONT REPORTÉS SUR LEUR PROPRIÉTAIRE, PAS FILTRÉS.
            .Select(Decrire)
            .Distinct()
            .ToList();

        if (mutations.Count == 0)
        {
            return;
        }

        var contexte = HbaRequestContext.Current;

        // L'ACTEUR N'EST PAS INVENTÉ QUAND IL N'Y EN A PAS.
        var acteur = Guid.TryParse(contexte.Actor?.Id, out var utilisateur) ? utilisateur : (Guid?)null;
        var typeActeur = contexte.Actor?.Type ?? "SYSTEM";

        // UN SEUL INSTANT POUR TOUTE LA TRANSACTION.
        var instant = DateTime.UtcNow;

        var correlation = string.IsNullOrWhiteSpace(contexte.CorrelationId) ? null : contexte.CorrelationId;

        foreach (var mutation in mutations)
        {
            // CE QUI RESTE ICI : LA COLLECTE. CE QUI PART : L'ECRITURE.
            AjouterUneEntreeDAudit(
                mutation.Type, mutation.Id, mutation.Operation,
                acteur, typeActeur, correlation, instant);
        }
    }

    /// <summary>
    /// La clé primaire d'une entrée, sous forme textuelle — les parties jointes par
    /// « | » pour une clé composite.
    /// </summary>
    /// <summary>Ce qu'une entrée du ChangeTracker devient dans le journal.</summary>
    private readonly record struct Mutation(string Type, string Id, AuditOperation Operation);

    /// <summary>
    /// Traduit une entrée du ChangeTracker en ligne de journal, en remontant les
    /// types possédés jusqu'à l'entité qui en répond.
    /// </summary>
    private static Mutation Decrire(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        if (!entry.Metadata.IsOwned())
        {
            return new Mutation(
                entry.Metadata.ClrType.Name,
                PrimaryKeyOf(entry),
                entry.State switch
                {
                    EntityState.Added => AuditOperation.Created,
                    EntityState.Deleted => AuditOperation.Deleted,
                    _ => AuditOperation.Updated
                });
        }

        // TOUJOURS `Updated` POUR UN TYPE POSSÉDÉ, MÊME AJOUTÉ OU SUPPRIMÉ.
        var possession = entry.Metadata.FindOwnership();

        if (possession is null)
        {
            return new Mutation(entry.Metadata.ClrType.Name, PrimaryKeyOf(entry), AuditOperation.Updated);
        }

        var proprietaire = possession.PrincipalEntityType;

        // La boucle couvre les possessions IMBRIQUÉES : s'arrêter au premier
        // propriétaire rendrait, pour une valeur à deux niveaux, un type qui
        // n'existe dans aucune table.
        while (proprietaire.IsOwned() && proprietaire.FindOwnership()?.PrincipalEntityType is { } dessus)
        {
            proprietaire = dessus;
        }

        var identifiant = string.Join(
            '|',
            possession.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? string.Empty));

        return new Mutation(proprietaire.ClrType.Name, identifiant, AuditOperation.Updated);
    }

    private static string PrimaryKeyOf(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        var cle = entry.Metadata.FindPrimaryKey();

        if (cle is null)
        {
            return string.Empty;
        }

        var valeurs = cle.Properties
            .Select(propriete => entry.Property(propriete.Name).CurrentValue?.ToString() ?? string.Empty);

        return string.Join('|', valeurs);
    }
}
