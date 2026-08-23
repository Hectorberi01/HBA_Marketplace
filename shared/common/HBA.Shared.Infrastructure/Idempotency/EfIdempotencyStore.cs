using Microsoft.EntityFrameworkCore;

namespace HBA.Shared.Infrastructure.Idempotency;

/// <summary>
/// Implémentation EF de <see cref="IIdempotencyStore"/>, générique sur le DbContext
/// du service.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LA RÉSERVATION S'APPUIE SUR LA CONTRAINTE D'UNICITÉ, PAS SUR UNE LECTURE.
///
/// Écrire « SELECT, puis INSERT si absent » paraît naturel et ne protège de rien :
/// deux requêtes simultanées passent toutes les deux le SELECT, insèrent toutes les
/// deux, et l'idempotence échoue précisément dans le cas de concurrence qu'elle
/// existe pour couvrir. Un client mobile qui réémet sur un réseau instable produit
/// exactement ce cas.
///
/// On tente donc l'INSERT d'abord et on interprète l'échec d'unicité comme « la clé
/// existe déjà ». La base arbitre, pas l'application.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class EfIdempotencyStore<TContext> : IIdempotencyStore
    where TContext : DbContext
{
    private readonly TContext _context;

    public EfIdempotencyStore(TContext context) => _context = context;

    public async Task<IdempotencyReservation> TryBeginAsync(
        string key,
        string scope,
        string endpoint,
        string requestFingerprint,
        CancellationToken cancellationToken = default)
    {
        var record = new IdempotencyRecord
        {
            Key = key,
            Scope = scope,
            Endpoint = endpoint,
            RequestFingerprint = requestFingerprint
        };

        _context.Set<IdempotencyRecord>().Add(record);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return new IdempotencyReservation(IdempotencyOutcome.Proceed);
        }
        catch (DbUpdateException)
        {
            // La clé existait déjà. On détache l'entité refusée, sinon le DbContext
            // retenterait de l'insérer au prochain SaveChanges du handler métier et
            // ferait échouer une transaction qui n'a rien à voir.
            _context.Entry(record).State = EntityState.Detached;
        }

        var existing = await _context.Set<IdempotencyRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.Key == key && r.Scope == scope && r.Endpoint == endpoint,
                cancellationToken);

        if (existing is null)
        {
            // L'insertion a échoué mais rien n'est en base : l'échec ne venait pas de
            // l'unicité. Rejouer serait pire que refuser — on laisse remonter.
            throw new InvalidOperationException(
                $"Réservation d'idempotence impossible pour la clé « {key} » sur {endpoint}.");
        }

        if (existing.RequestFingerprint != requestFingerprint)
        {
            return new IdempotencyReservation(IdempotencyOutcome.Mismatch);
        }

        if (existing.CompletedAtUtc is null)
        {
            return new IdempotencyReservation(IdempotencyOutcome.InFlight);
        }

        return new IdempotencyReservation(
            IdempotencyOutcome.Replay,
            existing.StatusCode,
            existing.ResponseBody);
    }

    public async Task CompleteAsync(
        string key,
        string scope,
        string endpoint,
        int statusCode,
        string? responseBody,
        CancellationToken cancellationToken = default)
    {
        var record = await _context.Set<IdempotencyRecord>()
            .FirstOrDefaultAsync(
                r => r.Key == key && r.Scope == scope && r.Endpoint == endpoint,
                cancellationToken);

        if (record is null)
        {
            return;
        }

        record.StatusCode = statusCode;
        record.ResponseBody = responseBody;
        record.CompletedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AbandonAsync(
        string key,
        string scope,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        var record = await _context.Set<IdempotencyRecord>()
            .FirstOrDefaultAsync(
                r => r.Key == key && r.Scope == scope && r.Endpoint == endpoint,
                cancellationToken);

        if (record is null || record.CompletedAtUtc is not null)
        {
            // Terminée entre-temps : on ne libère pas une clé dont la réponse est
            // mémorisée, sinon le rejeu réexécuterait au lieu de rejouer.
            return;
        }

        _context.Set<IdempotencyRecord>().Remove(record);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
