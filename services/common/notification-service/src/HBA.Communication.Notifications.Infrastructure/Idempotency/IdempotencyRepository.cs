using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Communication.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HBA.Communication.Notifications.Infrastructure.Persistence.Outbox;
using HBA.Communication.Notifications.Infrastructure.Persistence.Inbox;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Retry;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Processors;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Idempotency`.

namespace HBA.Communication.Notifications.Infrastructure.Idempotency;

/// <summary>
/// Implémentation EF de <see cref="IIdempotencyStore"/> , générique sur le
/// DbContext du service.
/// </summary>
public sealed class EfIdempotencyStore : IIdempotencyStore
{
    private readonly NotificationsDbContext _context;

    public EfIdempotencyStore(NotificationsDbContext context) => _context = context;

    public Task<IdempotencyReservation> TryBeginAsync(
        string key,
        string scope,
        string endpoint,
        string requestFingerprint,
        CancellationToken cancellationToken = default)
        => TryBeginAsync(key, scope, endpoint, requestFingerprint, repriseAutorisee: true, cancellationToken);

    private async Task<IdempotencyReservation> TryBeginAsync(
        string key,
        string scope,
        string endpoint,
        string requestFingerprint,
        bool repriseAutorisee,
        CancellationToken cancellationToken)
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
            // L'insertion a échoué mais rien n'est en base : l'échec ne venait pas
            // de l'unicité.
            throw new InvalidOperationException(
                $"Réservation d'idempotence impossible pour la clé « {key} » sur {endpoint}.");
        }

        if (existing.RequestFingerprint != requestFingerprint)
        {
            return new IdempotencyReservation(IdempotencyOutcome.Mismatch);
        }

        if (existing.CompletedAtUtc is null)
        {
            // UNE RÉSERVATION INACHEVÉE ET PÉRIMÉE SE REPREND (audit 1.8).
            if (existing.ExpiresAtUtc > DateTime.UtcNow || !repriseAutorisee)
            {
                return new IdempotencyReservation(IdempotencyOutcome.InFlight);
            }

            // La ligne périmée est retirée, puis on repart du début : c'est la
            // CONTRAINTE D'UNICITÉ qui doit à nouveau arbitrer, comme au premier
            // passage.
            await _context.Set<IdempotencyRecord>()
                .Where(r => r.Key == key && r.Scope == scope && r.Endpoint == endpoint
                            && r.CompletedAtUtc == null
                            && r.ExpiresAtUtc <= DateTime.UtcNow)
                .ExecuteDeleteAsync(cancellationToken);

            return await TryBeginAsync(
                key, scope, endpoint, requestFingerprint, repriseAutorisee: false, cancellationToken);
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
