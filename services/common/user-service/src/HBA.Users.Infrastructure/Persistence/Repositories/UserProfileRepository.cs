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

    /// <summary>LA LECTURE EN LOT — ELLE EXISTE POUR UNE RAISON PRÉCISE.</summary>
    public async Task<IReadOnlyList<UserProfile>> ListByUserIdsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            // Sans cette sortie, EF produit « WHERE id IN () », que PostgreSQL
            // refuse.
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
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.FirstName).HasMaxLength(UserProfile.MaxName).IsRequired();
        builder.Property(p => p.LastName).HasMaxLength(UserProfile.MaxName).IsRequired();
        builder.Property(p => p.AvatarUrl).HasMaxLength(UserProfile.MaxAvatarUrl);

        builder.Property(p => p.CreatedOnUtc).IsRequired();
        builder.Property(p => p.UpdatedOnUtc);

        // DisplayName est CALCULÉ, jamais persisté.
        builder.Ignore(p => p.DisplayName);
    }
}
