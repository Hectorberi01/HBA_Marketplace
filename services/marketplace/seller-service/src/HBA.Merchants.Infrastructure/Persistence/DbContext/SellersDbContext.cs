using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Results;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Merchants.Application;
using HBA.Merchants.Application.Abstractions;
using HBA.Merchants.Domain.Members;
using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Domain.Stores;

namespace HBA.Merchants.Infrastructure.Persistence;

/// <summary>
/// DbContext du module Sellers (schéma « sellers »). Hérite de ModuleDbContext
/// pour l'Unit of Work (dispatch des domain events) et l'outbox.
/// </summary>
public sealed class SellersDbContext : ModuleDbContext, ISellerUnitOfWork
{
    public const string SchemaName = "sellers";

    private readonly ICacheService _cache;

    public SellersDbContext(
        DbContextOptions<SellersDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue,
        ICacheService cache)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
        _cache = cache;
    }

    public DbSet<Seller> Sellers => Set<Seller>();

    public DbSet<Store> Stores => Set<Store>();

    /// <summary>
    /// Traces de consommation Kafka (§19.5) et requêtes idempotentes (§25).
    ///
    /// DANS LE SCHÉMA DU SERVICE, PAS DANS UNE BASE COMMUNE.
    ///
    /// Le §9 interdit qu'un service lise la base d'un autre, et une inbox partagée
    /// serait exactement cela — avec en prime un point de panne unique posé sur le
    /// chemin de toutes les consommations de la plateforme.
    /// </summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    /// <summary>L'équipe d'un vendeur (§21 du cahier des membres).</summary>
    public DbSet<SellerMember> SellerMembers => Set<SellerMember>();

    /// <summary>
    /// Les rôles, système et personnalisés confondus.
    /// <para>
    /// `StoreMembership` N'A PAS DE `DbSet`, ET C'EST VOULU : on n'atteint une
    /// affectation qu'à travers son membre. Un point d'entrée séparé inviterait à
    /// la modifier sans passer par l'agrégat, donc sans acteur et sans garde.
    /// </para>
    /// </summary>
    public DbSet<SellerRole> SellerRoles => Set<SellerRole>();

    /// <summary>Les invitations en cours et closes (§7).</summary>
    public DbSet<SellerInvitation> SellerInvitations => Set<SellerInvitation>();

    protected override string Schema => SchemaName;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE JOURNAL D'AUDIT EST ACTIF ICI.
    ///
    /// CE COMMENTAIRE DISAIT « LE PLUS UTILE DES QUATRE » ET NOMMAIT order,
    /// inventory ET catalog COMME JOURNALISANT « ce qui arrive aux marchandises ».
    /// Aucun des trois n'a jamais surchargé `KeepsAuditTrail` : ils héritent de
    /// `false`, et aucun n'a de table `audit_entries`. Trois journaux inventés dans
    /// un fichier qu'on lit précisément pour savoir lesquels existent.
    ///
    /// Celui-ci journalise ce qui arrive à l'ÉQUIPE : qui a invité qui, qui a
    /// changé les rôles de qui, qui a repointé le compte de reversement. Ce sont
    /// les questions qu'on pose après un incident, et c'est le seul journal dont
    /// le VENDEUR est directement le lecteur — les autres répondent à
    /// l'exploitation.
    ///
    /// Activé DANS LE MÊME COMMIT que la migration qui crée `sellers.audit_entries`.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    protected override bool KeepsAuditTrail => true;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SellersDbContext).Assembly);

        // LES CONFIGS DU SOCLE VIVENT DANS UN AUTRE ASSEMBLAGE.
        //
        // Le balayage ci-dessus ne parcourt que celui de `SellersDbContext` : il ne
        // les trouve pas. Les oublier ne casse RIEN à la compilation — les deux
        // tables manquent simplement, et l'erreur ne surgit qu'au premier message
        // consommé ou à la première requête portant une `Idempotency-Key`,
        // c'est-à-dire en production.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyConfiguration());

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Mène l'opération sous verrou consultatif, dans une transaction que cette
    /// méthode ouvre et referme elle-même. Voir
    /// <see cref="ISellerUnitOfWork.ExecuteUnderSellerLockAsync"/> pour la course
    /// qu'elle ferme.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA TRANSACTION EST OUVERTE ICI, ET C'EST TOUTE LA CORRECTION.
    ///
    /// L'écriture précédente exposait `LockSellerAsync(Guid)`, appelée au milieu
    /// d'un handler. Elle ne verrouillait RIEN : `pg_advisory_xact_lock` se relâche
    /// à la fin de la transaction, et sans transaction ouverte PostgreSQL traite
    /// l'instruction comme la sienne — verrou pris, validé, relâché, avant même la
    /// première lecture du handler. EF n'ouvre sa transaction qu'au
    /// `SaveChangesAsync`.
    ///
    /// L'encadré d'alors invoquait « l'intercepteur de transaction du module ». Il
    /// n'existe pas. Le texte était juste sur tout le reste — la variante `_xact_`,
    /// la clé dérivée du GUID, la collision sans conséquence — et faux sur le seul
    /// point dont dépendait l'ensemble.
    ///
    /// `pg_advisory_xact_lock` PREND UN `bigint`, ET UN GUID N'EN EST PAS UN.
    ///
    /// La clé vient des huit premiers octets du GUID. Une collision — deux vendeurs
    /// partageant la même clé — reste possible et SANS CONSÉQUENCE de correction :
    /// au pire, deux commerçants sérialisent leurs mutations d'équipe l'un derrière
    /// l'autre pendant quelques millisecondes. Ce qui compte est garanti : la même
    /// entrée donne toujours la même clé.
    ///
    /// `_xact_` ET NON LA VARIANTE DE SESSION. PostgreSQL relâche celui-ci au
    /// `COMMIT` comme au `ROLLBACK`. La variante de session exigerait un
    /// déverrouillage explicite, donc un `finally`, donc un chemin d'exception
    /// capable de laisser le verrou posé — et de bloquer toute l'équipe de ce
    /// vendeur jusqu'au redémarrage du service.
    ///
    /// HORS PostgreSQL, ON EXÉCUTE SANS VERROU NI TRANSACTION — et c'est le seul
    /// endroit où ce contrat ment un peu. Les tests en base mémoire n'ont ni l'un ni
    /// l'autre ; la course qu'on ferme ici n'y existe pas non plus, puisqu'il n'y a
    /// qu'un fil. Les suites d'intégration, elles, tournent sur un vrai PostgreSQL
    /// (testcontainers) : c'est là que le verrou mord réellement.
    ///
    /// SI UNE TRANSACTION EST DÉJÀ OUVERTE, ON NE LA REPREND PAS. EF refuse une
    /// transaction imbriquée. On pose le verrou dans celle de l'appelant et on le
    /// laisse décider du sort de l'ensemble — c'est lui qui l'a ouverte.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Result> ExecuteUnderSellerLockAsync(
        Guid sellerId,
        Func<CancellationToken, Task<Result>> operation,
        CancellationToken cancellationToken = default)
    {
        if (!Database.IsNpgsql())
        {
            return await operation(cancellationToken);
        }

        var cle = BitConverter.ToInt64(sellerId.ToByteArray(), 0);

        if (Database.CurrentTransaction is not null)
        {
            await Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock({0});", [cle], cancellationToken);

            return await operation(cancellationToken);
        }

        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);

        await Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0});", [cle], cancellationToken);

        var resultat = await operation(cancellationToken);

        // UN ÉCHEC ANNULE. Les appelants rendent leur refus AVANT d'écrire, donc
        // l'annulation ne leur retire rien — mais elle rend le contrat net : une
        // opération refusée ne laisse aucune trace, et le verrou tombe avec elle.
        if (resultat.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return resultat;
        }

        await transaction.CommitAsync(cancellationToken);
        return resultat;
    }

    /// <summary>
    /// Invalidation du cache — point de passage unique, comme dans Catalog.
    ///
    /// Le vendeur est mis en cache sous DEUX clés : par identifiant de vendeur, et
    /// par identifiant d'utilisateur (la connexion vendeur passe par là). Les deux
    /// doivent tomber ensemble. N'en évincer qu'une, c'est laisser l'autre servir
    /// un nom de boutique périmé — un bug d'autant plus retors qu'il ne se
    /// manifeste que sur l'un des deux chemins de lecture.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var keysToEvict = await CollectCacheKeysToEvictAsync(cancellationToken);

        var affected = await base.SaveChangesAsync(cancellationToken);

        if (keysToEvict.Count > 0)
        {
            await _cache.RemoveManyAsync(keysToEvict, cancellationToken);
        }

        return affected;
    }

    /// <summary>
    /// ─────────────────────────────────────────────────────────────────────────────
    /// ON ÉVINCE PAR AGRÉGAT, PAS PAR ENTITÉ MODIFIÉE.
    ///
    /// La version précédente déduisait le vendeur à évincer de la FK ombre « SellerId »
    /// portée par la pièce KYB :
    ///
    ///     if ((p?.CurrentValue ?? p?.OriginalValue) is Guid id &amp;&amp; id != Guid.Empty)
    ///
    /// Ce test ne se déclenchait JAMAIS sur une SUPPRESSION. Quand un enfant est retiré
    /// de la collection du parent, EF rompt d'abord la relation : la FK ombre — de type
    /// `Guid`, donc NON nullable — retombe à `Guid.Empty`. Elle n'est pas `null`, si
    /// bien que le `??` ne consultait jamais `OriginalValue`, et le garde-fou
    /// `!= Guid.Empty` rejetait le vendeur. Aucune clé évincée.
    ///
    /// Le symptôme n'était pas « une pièce mal affichée ». Le vendeur supprimait une
    /// pièce, la base la supprimait bien, l'écran continuait de l'afficher (résumé en
    /// cache) — et le second clic sur la corbeille répondait « Pièce KYB introuvable
    /// pour cette boutique ». Un utilisateur devant une ligne fantôme qu'il ne peut ni
    /// faire disparaître ni supprimer.
    ///
    /// La correction ne consiste pas à ajouter un cas de plus. Elle consiste à cesser
    /// de raisonner sur l'état d'une ENTITÉ pour raisonner sur l'AGRÉGAT : le résumé
    /// mis en cache contient les pièces KYB, donc TOUT vendeur chargé dans ce contexte
    /// au moment d'une écriture est suspect. C'est plus large, c'est volontaire — et
    /// c'est de toute façon un seul agrégat par requête.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private async Task<List<string>> CollectCacheKeysToEvictAsync(CancellationToken cancellationToken)
    {
        // Rien à écrire : rien à évincer. Évite de purger le cache sur une simple
        // lecture, ce qui reviendrait à ne pas en avoir.
        if (!ChangeTracker.HasChanges())
        {
            return [];
        }

        var keys = new HashSet<string>();

        // Les vendeurs dont l'équipe doit voir son contexte d'autorisation recalculé.
        var sellersTouches = new HashSet<Guid>();

        // Tout vendeur SUIVI, quel que soit son état — y compris « Unchanged ».
        //
        // Ajouter ou retirer une pièce ne modifie pas l'entité Seller elle-même :
        // elle reste « Unchanged » alors que son résumé, lui, a changé. C'est
        // précisément le cas que l'ancienne version laissait passer.
        foreach (var entree in ChangeTracker.Entries<Seller>())
        {
            var seller = entree.Entity;

            keys.Add(SellersCacheKeys.Seller(seller.Id.Value));
            // Les deux clés tombent ensemble : n'en évincer qu'une laisserait l'autre
            // servir un résumé périmé, sur l'un seulement des deux chemins de lecture.
            keys.Add(SellersCacheKeys.SellerByUser(seller.UserId));

            // ═════════════════════════════════════════════════════════════════
            // ET LA CLÉ DE L'ANCIEN PROPRIÉTAIRE, QUAND `UserId` CHANGE.
            //
            // Cette boucle ne connaissait que la valeur COURANTE. Tant que `UserId`
            // n'était écrit qu'à l'inscription, cela suffisait. Depuis le transfert
            // de propriété (lot 7.2), il bouge — et `sellers:by-user:{ancien}`
            // resterait en cache dix minutes, à répondre que l'ancien propriétaire
            // administre encore ce dossier.
            //
            // Ce n'est pas une lenteur : c'est un droit révoqué qui continue de
            // s'exercer, sur exactement le chemin que toutes les routes vendeur
            // empruntent pour résoudre « quel dossier ce jeton administre-t-il ».
            //
            // `OriginalValues` n'est lisible que sur une entité SUIVIE et non
            // ajoutée — d'où le test d'état.
            // ═════════════════════════════════════════════════════════════════
            if (entree.State == EntityState.Modified)
            {
                var ancien = entree.OriginalValues.GetValue<Guid>(nameof(Seller.UserId));

                if (ancien != seller.UserId)
                {
                    keys.Add(SellersCacheKeys.SellerByUser(ancien));
                }
            }

            // ET L'ÉTAT OPÉRATIONNEL, QUI DÉCIDE SI L'ÉQUIPE PEUT ENCORE VENDRE.
            //
            // C'est la clé que `MerchantAccessApi.PeutOpererAsync` consulte à chaque
            // requête vendeur des cinq services appelants. Sans elle ici, suspendre
            // un vendeur mettrait dix minutes à mordre — le TTL du cache — alors que
            // la suspension est censée être immédiate.
            keys.Add(SellersCacheKeys.SellerCanOperate(seller.Id.Value));

            sellersTouches.Add(seller.Id.Value);
        }

        // ═════════════════════════════════════════════════════════════════════
        // TOUTE MUTATION D'APPARTENANCE ÉVINCE LA RÉSOLUTION PAR UTILISATEUR.
        //
        // `sellers:by-user:{userId}` a un TTL de dix minutes ET MÉMORISE LES
        // ABSENCES : un compte interrogé avant son rattachement laisse une entrée
        // NÉGATIVE en cache. Sans cette boucle, un membre ajouté puis aussitôt
        // utilisé serait résolu à « aucun vendeur » pendant dix minutes — un
        // « ça marche au bout d'un moment » que personne ne relie à un cache.
        //
        // Le symétrique compte autant : un membre révoqué garderait sa résolution
        // pendant le même quart d'heure, ce qui est le contraire d'une révocation.
        // ═════════════════════════════════════════════════════════════════════
        foreach (var entry in ChangeTracker.Entries<SellerMember>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            keys.Add(SellersCacheKeys.SellerByUser(entry.Entity.UserId));

            // ET LE CONTEXTE D'AUTORISATION, QUI EST LA CLÉ QUI COMPTE.
            //
            // C'est elle que les cinq services appelants consultent à chaque
            // requête vendeur. L'évincer ICI — dans le même enregistrement que la
            // mutation, donc globalement depuis que Redis est branché — est ce qui
            // rend vraie la promesse du §53 : la requête suivante, sur n'importe
            // quelle réplique, voit l'accès révoqué.
            //
            // Une éviction publiée par Kafka ne l'aurait pas donnée : dans un
            // groupe de consommateurs, une seule instance reçoit le message.
            keys.Add(SellersCacheKeys.MemberAccess(entry.Entity.UserId));
        }

        // Filet : une pièce touchée sans que son parent soit chargé (cas qui ne
        // devrait pas se produire — on passe toujours par l'agrégat — mais qui, s'il
        // survenait, laisserait un résumé périmé sans aucun signe).
        //
        // `OriginalValue` D'ABORD : sur une suppression, c'est la seule valeur qui
        // désigne encore le parent.
        foreach (var entry in ChangeTracker.Entries<KybDocument>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var fk = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "SellerId");
            if (fk is null)
            {
                continue;
            }

            foreach (var candidate in new[] { fk.OriginalValue, fk.CurrentValue })
            {
                if (candidate is Guid sellerId && sellerId != Guid.Empty)
                {
                    keys.Add(SellersCacheKeys.Seller(sellerId));
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // UN RÔLE MODIFIÉ PÉRIME LE CONTEXTE DE TOUS CEUX QUI LE PORTENT.
        //
        // Retirer une permission d'un RÔLE ne touchait aucune ligne `SellerMember` :
        // la boucle précédente n'évinçait donc rien, et la permission retirée
        // continuait d'autoriser pendant deux minutes — le TTL de `MemberAccess`.
        // Deux minutes pour une permission qu'on vient de retirer à un employé, sur
        // des routes qui touchent au stock et à l'argent.
        //
        // Le rôle ne connaît pas ses porteurs : il faut donc demander à la base qui
        // appartient à ce vendeur. C'est une requête de plus, sur une écriture rare
        // — un rôle ne se modifie pas à chaque commande.
        //
        // `SellerId` est nullable sur `SellerRole` : les rôles de plateforme n'ont
        // pas de vendeur. Ils ne sont portés par aucun membre d'équipe, il n'y a
        // donc rien à évincer pour eux.
        // ═════════════════════════════════════════════════════════════════════
        foreach (var entry in ChangeTracker.Entries<SellerRole>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var sellerId = entry.Entity.SellerId
                ?? entry.Property(nameof(SellerRole.SellerId)).OriginalValue as Guid?;

            if (sellerId is { } id && id != Guid.Empty)
            {
                sellersTouches.Add(id);
            }
        }

        if (sellersTouches.Count > 0)
        {
            // `AsNoTracking` : on ne veut que des identifiants, et charger des
            // membres dans le contexte pendant un enregistrement ajouterait des
            // entités suivies au moment le plus mal choisi.
            var membres = await SellerMembers
                .AsNoTracking()
                .Where(m => sellersTouches.Contains(m.SellerId))
                .Select(m => m.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var userId in membres)
            {
                keys.Add(SellersCacheKeys.MemberAccess(userId));
                keys.Add(SellersCacheKeys.SellerByUser(userId));
            }
        }

        return [.. keys];
    }
}
