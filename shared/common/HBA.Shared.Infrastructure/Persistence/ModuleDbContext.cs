using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Context;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Audit;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Serialization;

namespace HBA.Shared.Infrastructure.Persistence;

/// <summary>
/// Base de tous les DbContext de module. Apporte :
///  - l'Unit of Work (SaveChanges qui dispatche les domain events) ;
///  - le drainage de la file d'events d'intégration vers l'outbox local ;
///  - la table outbox locale au schéma du module ;
///  - l'isolation par schéma (chaque module passe son nom de schéma).
///
/// Règle d'or : un module ne lit/écrit que dans son propre schéma. Pas de JOIN
/// ni de foreign key cross-schéma — c'est ce qui rend l'extraction mécanique.
/// </summary>
public abstract class ModuleDbContext : DbContext, IUnitOfWork, IOutboxDbContext
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

    /// <summary>
    /// Ce module tient-il un journal d'audit (§37) ?
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// FAUX PAR DÉFAUT, ET CE N'EST PAS UNE TIÉDEUR.
    ///
    /// Une entité ajoutée au modèle sans migration correspondante fait échouer tout
    /// démarrage à froid — c'est exactement ce que le contrôle `migrations`
    /// attrape, et il le ferait pour les dix-neuf contextes d'un coup si ce
    /// booléen valait vrai ici. L'activation se fait donc module par module, DANS
    /// LE MÊME COMMIT que sa migration.
    ///
    /// Ce n'est pas non plus un réglage d'exécution : c'est une propriété du
    /// MODÈLE. La rendre configurable ferait diverger le schéma attendu du schéma
    /// réel selon une variable d'environnement, ce qui est la manière la plus sûre
    /// de rendre une migration inapplicable en production et nulle part ailleurs.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    protected virtual bool KeepsAuditTrail => false;

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new OutboxConfiguration());

        if (KeepsAuditTrail)
        {
            modelBuilder.ApplyConfiguration(new AuditConfiguration());
        }

        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 1. Dispatch des domain events : les handlers peuvent mettre des
        //    integration events en file.
        await DispatchDomainEventsAsync(cancellationToken);

        // 2. Draine la file vers l'outbox LOCAL (ce DbContext, ce schéma), de
        //    sorte que l'event et le changement d'état soient persistés ensemble.
        DrainIntegrationEventsToOutbox();

        // 3. Journalise QUI a muté QUOI, dans la même transaction.
        //
        //    APRÈS les deux étapes précédentes, et ce n'est pas indifférent : un
        //    gestionnaire d'événement de domaine peut lui-même muter une entité, et
        //    journaliser avant lui manquerait ces lignes-là. Or ce sont précisément
        //    les mutations en cascade — celles qu'on ne voit pas dans le handler
        //    d'origine — qu'un journal sert à retrouver.
        RecordAuditTrail();

        // 4. Estampille `UpdatedAtUtc` sur les entités qui la déclarent.
        //
        //    L'ordre vis-à-vis de `RecordAuditTrail` est INDIFFÉRENT, et il faut
        //    savoir pourquoi pour ne pas s'en inquiéter : l'estampille n'écrit
        //    que sur des entrées DÉJÀ `Added` ou `Modified`, donc elle ne change
        //    l'état d'aucune entrée, donc elle ne peut ni créer ni requalifier
        //    une ligne de journal. Le journal, lui, ne retient que l'entité, sa
        //    clé et l'opération — jamais la liste des colonnes touchées.
        //
        //    CE FILTRE D'ÉTAT N'EST PAS UNE OPTIMISATION, C'EST LA GARDE.
        //
        //    Estampiller une entrée `Unchanged` la ferait passer en `Modified` :
        //    un UPDATE sur une ligne que personne n'a demandé à changer, et — si
        //    l'ordre venait à être inversé un jour — une ligne de journal
        //    « modifiée par » sur un geste qui n'a pas eu lieu.
        HorodaterLesModifications();

        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Pose l'instant courant sur `UpdatedAtUtc`, pour les entités qui l'ont
    /// déclarée via <see cref="HorodatageExtensions.HorodateLesModifications{T}"/>.
    /// </summary>
    /// <remarks>
    /// UN SEUL INSTANT POUR TOUTE LA TRANSACTION.
    ///
    /// `DateTime.UtcNow` lu dans la boucle donnerait des horodatages qui
    /// diffèrent de quelques microsecondes entre deux lignes écrites par la même
    /// commande — et un lecteur qui trie par `UpdatedAtUtc` en conclurait un
    /// ordre de causalité qui n'existe pas. C'est le même raisonnement que
    /// l'instant unique de `RecordAuditTrail`.
    ///
    /// `Added` AUTANT QUE `Modified`.
    ///
    /// Sans l'INSERT, `NULL` voudrait dire à la fois « ligne antérieure à la
    /// colonne » et « jamais modifiée depuis sa création » — deux situations que
    /// l'on cherche justement à distinguer en incident.
    /// </remarks>
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
            OutboxMessages.Add(new OutboxMessage
            {
                Type = EventTypeName.Of(integrationEvent.GetType()),
                Content = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), SerializerOptions),
                OccurredOnUtc = integrationEvent.OccurredOnUtc,

                // LES DEUX CHEMINS D'ENFILEMENT DOIVENT LE FAIRE.
                //
                // `OutboxIntegrationEventPublisher` et ce drain écrivent tous deux
                // dans `outbox_messages`. N'instrumenter que l'un rendrait la moitié
                // des événements traçable et l'autre non, selon un détail
                // d'implémentation invisible depuis le métier — le pire cas pour un
                // diagnostic, puisque le symptôme paraîtrait intermittent.
                TraceParent = System.Diagnostics.Activity.Current?.Id,

                // CAPTURÉE ICI, PARCE QU'APRÈS IL EST TROP TARD.
                //
                // L'outbox est une frontière asynchrone : le message part plusieurs
                // secondes plus tard, dans un service d'arrière-plan qui n'a plus
                // rien de la requête d'origine. Tout ce qui n'est pas écrit en base
                // à cet instant est perdu — c'est déjà la raison d'être de
                // `TraceParent` juste au-dessus.
                CorrelationId = string.IsNullOrWhiteSpace(HbaRequestContext.Current.CorrelationId)
                    ? null
                    : HbaRequestContext.Current.CorrelationId
            });
        }
    }

    /// <summary>
    /// Écrit une ligne de journal par entité mutée. Voir <see cref="AuditEntry"/>
    /// pour le raisonnement d'ensemble.
    /// </summary>
    private void RecordAuditTrail()
    {
        if (!KeepsAuditTrail)
        {
            return;
        }

        // MATÉRIALISÉ AVANT LA BOUCLE, PAS ÉNUMÉRÉ PENDANT.
        //
        // Chaque `Add` ci-dessous inscrit une entrée de plus dans le ChangeTracker.
        // Énumérer paresseusement ferait journaliser les lignes de journal, qui en
        // produiraient d'autres : une boucle infinie, découverte au premier
        // `SaveChanges` d'un service en production.
        var mutations = ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            // ═════════════════════════════════════════════════════════════════
            // LE JOURNAL NE JOURNALISE PAS LA PLOMBERIE.
            //
            // Cette liste ne portait que `AuditEntry` et `OutboxMessage` — les deux
            // sans lesquels la boucle serait infinie. `ConsumerInboxEntry` et
            // `IdempotencyRecord` sont pourtant mappés dans les MÊMES contextes, et
            // écrits à chaque message Kafka consommé, à chaque requête idempotente.
            //
            // Chacun produisait donc une ligne de journal à acteur NUL, type
            // `SYSTEM`, sans le moindre rapport avec un geste humain. Sur
            // seller-service, food-order-service et return-refund-service — les
            // trois seuls contextes qui journalisaient — ce bruit était déjà
            // majoritaire. Le rendre visible avant d'allumer le journal ailleurs
            // n'est pas de l'hygiène : c'est ce qui décide si la table reste
            // lisible ou devient un flux de messages Kafka déguisé.
            //
            // CE QU'ON PERD, ET POURQUOI C'EST LE BON CHOIX.
            //
            // On perd la trace d'« un message a été consommé ». Elle n'a jamais
            // appartenu à ce journal : `AuditEntry` répond à « qui a touché à quoi »,
            // et la réponse serait toujours « personne ». Le suivi d'un message se
            // fait par `CorrelationId` et `TraceParent`, qui traversent l'outbox
            // exactement pour cela.
            //
            // L'EFFET MÉTIER, LUI, RESTE TRACÉ. Un consommateur qui marque son
            // inbox ET confirme une commande écrit toujours la ligne de la commande.
            // Seule la ligne d'infrastructure disparaît.
            // ═════════════════════════════════════════════════════════════════
            .Where(entry => entry.Entity
                is not AuditEntry
                and not OutboxMessage
                and not ConsumerInboxEntry
                and not IdempotencyRecord)

            // LES TYPES POSSÉDÉS SONT REPORTÉS SUR LEUR PROPRIÉTAIRE, PAS FILTRÉS.
            //
            // Quand SEULE l'adresse d'un lieu change, EF marque `Address` comme
            // modifiée et laisse `FulfillmentLocation` intacte. Les filtrer perdrait
            // donc complètement le changement d'adresse d'un entrepôt — le geste
            // même que `STOCK_LOCATION_MANAGE` protège. Voir `Decrire`.
            //
            // `Distinct` évite la ligne en double quand le propriétaire ET une
            // valeur qu'il possède changent dans le même geste.
            .Select(Decrire)
            .Distinct()
            .ToList();

        if (mutations.Count == 0)
        {
            return;
        }

        var contexte = HbaRequestContext.Current;

        // L'ACTEUR N'EST PAS INVENTÉ QUAND IL N'Y EN A PAS.
        //
        // Un consommateur Kafka ou un appel gRPC interne mute sans personne
        // derrière. `Guid.Empty` ou l'identifiant du service dans la colonne
        // utilisateur ferait passer un traitement automatique pour un compte — et
        // le jour où l'on chercherait qui a annulé mille commandes, on trouverait
        // un utilisateur qui n'existe pas.
        var acteur = Guid.TryParse(contexte.Actor?.Id, out var utilisateur) ? utilisateur : (Guid?)null;
        var typeActeur = contexte.Actor?.Type ?? "SYSTEM";

        // UN SEUL INSTANT POUR TOUTE LA TRANSACTION.
        //
        // Appeler `DateTime.UtcNow` par ligne donnerait des horodatages qui
        // diffèrent de quelques microsecondes à l'intérieur d'un même geste, et
        // l'ordre de lecture du journal dépendrait alors de l'ordre d'énumération
        // du ChangeTracker — c'est-à-dire de rien de stable.
        var instant = DateTime.UtcNow;

        var correlation = string.IsNullOrWhiteSpace(contexte.CorrelationId) ? null : contexte.CorrelationId;

        foreach (var mutation in mutations)
        {
            Set<AuditEntry>().Add(new AuditEntry
            {
                EntityType = mutation.Type,
                EntityId = mutation.Id,
                Operation = mutation.Operation,
                ActorUserId = acteur,
                ActorType = typeActeur,
                CorrelationId = correlation,
                OccurredOnUtc = instant
            });
        }
    }

    /// <summary>
    /// La clé primaire d'une entrée, sous forme textuelle — les parties jointes par
    /// « | » pour une clé composite.
    /// </summary>
    /// <remarks>
    /// POUR UNE ENTITÉ AJOUTÉE, LA CLÉ PEUT ENCORE ÊTRE VIDE.
    ///
    /// Les clés générées par la base (`UseIdentityByDefaultColumn`) ne sont
    /// connues qu'APRÈS `base.SaveChangesAsync`. On écrit alors une chaîne vide
    /// plutôt que d'attendre : la ligne de journal garde le TYPE, l'acteur et
    /// l'instant, ce qui suffit à répondre « qui a créé quelque chose ici, et
    /// quand ». Différer l'écriture pour gagner l'identifiant obligerait à un
    /// second `SaveChanges`, donc à sortir de la transaction — on perdrait
    /// l'atomicité, qui vaut plus que l'identifiant.
    ///
    /// Les agrégats de ce dépôt portent des identités fortes assignées en mémoire
    /// (`ProductId.New()`), donc le cas est rare ; il n'est pas impossible.
    /// </remarks>
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

        // ═════════════════════════════════════════════════════════════════════
        // TOUJOURS `Updated` POUR UN TYPE POSSÉDÉ, MÊME AJOUTÉ OU SUPPRIMÉ.
        //
        // Ajouter une plage horaire à une boutique produit une entrée `Added` sur
        // le type possédé. Recopier cet état donnerait « Store CRÉÉE » dans le
        // journal — un événement qui n'a pas eu lieu, et le plus trompeur possible
        // le jour où l'on cherche quand une boutique a été ouverte. Du point de vue
        // de l'entité qui en répond, c'est une MODIFICATION, quelle que soit
        // l'opération faite sur la valeur possédée.
        //
        // ET L'IDENTIFIANT VIENT DE LA POSSESSION, PAS DE LA CLÉ COMPLÈTE.
        //
        // La clé primaire d'un élément de collection possédée est composite :
        // (identifiant du propriétaire, ordinal). La prendre entière produirait un
        // `EntityId` du genre « a1b2…|3 », qui ne correspond à aucune ligne
        // citable — et l'index par entité, qui sert précisément à retrouver
        // l'histoire d'UNE fiche, ne rendrait rien.
        // ═════════════════════════════════════════════════════════════════════
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
