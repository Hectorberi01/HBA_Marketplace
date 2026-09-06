using HBA.Identity.Domain.Mfa;
using Microsoft.EntityFrameworkCore;

namespace HBA.Identity.Infrastructure.Persistence;

internal sealed class MfaChallengeRepository : IMfaChallengeRepository
{
    private readonly IdentityDbContext _context;

    public MfaChallengeRepository(IdentityDbContext context) => _context = context;

    public async Task AddAsync(MfaChallenge challenge, CancellationToken cancellationToken = default)
        => await _context.MfaChallenges.AddAsync(challenge, cancellationToken);

    public Task<MfaChallenge?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.MfaChallenges.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <summary>
    /// Invalide les défis encore vivants en les faisant expirer immédiatement.
    ///
    /// ON N'EFFACE PAS LES LIGNES.
    ///
    /// Un défi supprimé disparaît de l'audit : impossible ensuite de savoir combien
    /// de codes ont été demandés pour un compte, ce qui est exactement le signal
    /// d'une tentative de harcèlement par SMS. La ligne reste, avec son compteur de
    /// tentatives, et la purge par date s'en charge plus tard.
    /// </summary>
    public async Task<int> ConsumeActiveAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var actifs = await _context.MfaChallenges
            .Where(c => c.UserId == userId && c.ConsumedAtUtc == null && c.ExpiresAtUtc > now)
            .ToListAsync(cancellationToken);

        foreach (var challenge in actifs)
        {
            challenge.Expire();
        }

        return actifs.Count;
    }
}
