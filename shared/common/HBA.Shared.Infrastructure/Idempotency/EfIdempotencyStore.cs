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

    public Task<IdempotencyReservation> TryBeginAsync(
        string key,
        string scope,
        string endpoint,
        string requestFingerprint,
        CancellationToken cancellationToken = default)
        => TryBeginAsync(key, scope, endpoint, requestFingerprint, repriseAutorisee: true, cancellationToken);

    /// <remarks>
    /// UNE SEULE REPRISE, ET LE PARAMÈTRE EST LÀ POUR LE GARANTIR.
    ///
    /// La reprise d'une réservation périmée supprime la ligne puis recommence.
    /// Écrire cela par appel récursif marcherait presque toujours et n'aurait
    /// aucune borne : il suffirait d'un entrelacement où chaque tour retrouve une
    /// ligne périmée pour boucler sans fin, sous verrou de base, dans une requête
    /// HTTP. Le drapeau rend la terminaison évidente à la lecture — le second
    /// passage ne peut pas en déclencher un troisième.
    /// </remarks>
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
            // ═════════════════════════════════════════════════════════════════
            // UNE RÉSERVATION INACHEVÉE ET PÉRIMÉE SE REPREND (audit 1.8).
            //
            // CE QUI ÉTAIT CASSÉ. Cette branche rendait `InFlight` — donc 409 —
            // sans jamais regarder `ExpiresAtUtc`. La ligne n'est complétée que
            // si le gestionnaire rend la main, normalement ou par une exception
            // ATTRAPÉE. Si le processus meurt entre la réservation et la
            // complétion — OOM, `kill`, éviction de pod, redéploiement — la ligne
            // reste inachevée POUR TOUJOURS : ni durée de vie, ni purge, ni
            // reprise. Le client qui réémet sa clé reçoit 409 indéfiniment, et
            // aucun geste automatique ne le débloque. En plein paiement, c'est
            // une commande qu'il ne peut ni finir ni recommencer.
            //
            // ET LE MÉCANISME EXISTAIT DÉJÀ, ÉTEINT. `ExpiresAtUtc` est déclarée
            // dans l'entité, `IsRequired()` dans la configuration, et porte un
            // INDEX DÉDIÉ dans la migration de CHACUN des services. Une colonne,
            // un défaut de 24 h et un index décrivaient une durée de vie que
            // personne n'appliquait. C'est ce qui rend le défaut invisible : tout
            // a l'air prévu.
            //
            // POURQUOI `Proceed` ET PAS UNE ERREUR PLUS DOUCE. Passé l'échéance,
            // la première exécution n'a laissé aucune réponse à rejouer et n'en
            // laissera jamais. Il n'y a que deux issues : refuser à vie, ou
            // réexécuter. On réexécute.
            //
            // CE QUE ÇA NE COUVRE PAS, ET C'EST À SAVOIR : si la première
            // exécution avait déjà produit son effet métier AVANT de mourir —
            // paiement parti, message envoyé — le rejeu après 24 h le produira
            // une SECONDE FOIS. L'idempotence de la couche HTTP ne remplace pas
            // celle du domaine ; les opérations qui déplacent de l'argent ont la
            // leur (`Refund.IdempotencyKey`, l'inbox des consommateurs). Vingt-
            // quatre heures est le compromis : assez long pour que toute reprise
            // réseau normale retrouve sa réponse, assez court pour qu'un client
            // bloqué ne le reste pas plus d'une journée.
            // ═════════════════════════════════════════════════════════════════
            if (existing.ExpiresAtUtc > DateTime.UtcNow || !repriseAutorisee)
            {
                return new IdempotencyReservation(IdempotencyOutcome.InFlight);
            }

            // La ligne périmée est retirée, puis on repart du début : c'est la
            // CONTRAINTE D'UNICITÉ qui doit à nouveau arbitrer, comme au premier
            // passage. Rendre `Proceed` sans supprimer laisserait une ligne
            // inachevée que `CompleteAsync` retrouverait — et l'appelant croirait
            // avoir réservé ce qu'il n'a pas réservé.
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
