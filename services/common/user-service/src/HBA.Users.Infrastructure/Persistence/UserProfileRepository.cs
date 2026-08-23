using HBA.Users.Domain.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Users.Infrastructure.Persistence;

internal sealed class UserProfileRepository : IUserProfileRepository
{
    private readonly UsersDbContext _dbContext;

    public UserProfileRepository(UsersDbContext dbContext) => _dbContext = dbContext;

    public async Task<UserProfile?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.UserProfiles.FirstOrDefaultAsync(p => p.Id == userId, cancellationToken);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA LECTURE EN LOT — ELLE EXISTE POUR UNE RAISON PRÉCISE.
    ///
    /// Depuis que le nom a quitté <c>UserSummary</c>, toute liste qui affiche un
    /// nom par ligne — commandes d'un vendeur, utilisateurs de la console — ferait
    /// un appel par ligne. C'est le N+1 classique, et il n'apparaît qu'en
    /// production, quand la liste dépasse dix éléments.
    ///
    /// <c>AsNoTracking</c> : ces profils sont lus pour être affichés, jamais
    /// modifiés. Les suivre ferait grossir le change tracker à chaque page.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<IReadOnlyList<UserProfile>> ListByUserIdsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            // Sans cette sortie, EF produit « WHERE id IN () », que PostgreSQL
            // refuse. Le cas arrive dès qu'une page n'a aucune ligne.
            return [];
        }

        return await _dbContext.UserProfiles
            .AsNoTracking()
            .Where(p => userIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(UserProfile profile, CancellationToken cancellationToken = default)
        => await _dbContext.UserProfiles.AddAsync(profile, cancellationToken);

    public void Remove(UserProfile profile) => _dbContext.UserProfiles.Remove(profile);
}

internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profiles");

        // LA CLÉ PRIMAIRE EST LE UserId D'IDENTITY, PAS UN IDENTIFIANT PROPRE.
        //
        // C'est ce qui rend « deux profils pour un compte » impossible sans index
        // unique supplémentaire ni règle à faire respecter. ValueGeneratedNever :
        // la valeur vient d'Identity, la base ne doit surtout pas en inventer une.
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.FirstName).HasMaxLength(UserProfile.MaxName).IsRequired();
        builder.Property(p => p.LastName).HasMaxLength(UserProfile.MaxName).IsRequired();
        builder.Property(p => p.AvatarUrl).HasMaxLength(UserProfile.MaxAvatarUrl);

        builder.Property(p => p.CreatedOnUtc).IsRequired();
        builder.Property(p => p.UpdatedOnUtc);

        // DisplayName est CALCULÉ, jamais persisté. Une colonne « nom complet »
        // diverge dès qu'un nom de famille est corrigé sans que la concaténation
        // soit refaite — et c'est le nom affiché au client qui devient faux.
        builder.Ignore(p => p.DisplayName);
    }
}
