using HBA.Users.Application.Abstractions;
using HBA.Users.Domain.Addresses;
using HBA.Users.Domain.Devices;
using HBA.Users.Domain.Preferences;
using HBA.Users.Domain.Profiles;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HBA.Users.Infrastructure.Persistence;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE MODULE USER — « QUI EST LA PERSONNE ? »
///
/// Le cahier d'architecture sépare deux questions que le code réunissait :
///
///   • IDENTITY répond à « qui peut se connecter ? » — mot de passe, jetons,
///     rôles, vérifications. C'est de la sécurité d'accès.
///   • USER répond à « qui est la personne ? » — profil, avatar, adresses,
///     préférences. C'est du métier.
///
/// La distinction n'est pas cosmétique. Le carnet d'adresses n'a aucune raison
/// d'être verrouillé derrière les mêmes contraintes qu'un magasin de mots de
/// passe, et un magasin de mots de passe n'a aucune raison de grossir à chaque
/// fois qu'un acheteur ajoute une adresse de livraison.
///
/// LA RÉFÉRENCE VA DANS UN SEUL SENS.
///
/// User connaît le <c>UserId</c> émis par Identity ; Identity ne connaît pas User.
/// C'est ce qui permet de supprimer un profil sans toucher au compte, et de
/// vérifier un mot de passe sans charger un carnet d'adresses.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class UsersDbContext : ModuleDbContext, IUsersUnitOfWork
{
    public const string SchemaName = "users";

    public UsersDbContext(
        DbContextOptions<UsersDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Address> Addresses => Set<Address>();

    /// <summary>
    /// Les profils. Leur clé primaire est le UserId émis par Identity — voir
    /// l'encadré sur UserProfile : c'est ce qui rend « deux profils pour un
    /// compte » impossible par construction.
    /// </summary>
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    /// <summary>Préférences (§10.2). Une ligne par utilisateur, clé = UserId.</summary>
    public DbSet<UserPreferences> Preferences => Set<UserPreferences>();

    /// <summary>Appareils enregistrés pour les notifications push (§10.2).</summary>
    public DbSet<UserDevice> Devices => Set<UserDevice>();

    /// <summary>
    /// Traces de consommation Kafka (§19.5) et requêtes idempotentes (§5).
    ///
    /// Elles vivent dans le schéma du service et non dans une base commune : le §9
    /// interdit qu'un service lise la base d'un autre, et une inbox partagée serait
    /// exactement cela — avec en prime un point de panne unique sur le chemin de
    /// toutes les consommations.
    /// </summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UsersDbContext).Assembly);

        // Les configurations du socle vivent dans un AUTRE assembly : le balayage
        // ci-dessus ne les trouve pas. Les oublier ne casse rien à la compilation —
        // les tables manquent simplement, et l'erreur ne surgit qu'au premier
        // message consommé, en production.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
