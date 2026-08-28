using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HBA.Shared.Infrastructure.Idempotency;

/// <summary>
/// Efface les réservations d'idempotence dont l'échéance est passée.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LA COLONNE, LE DÉFAUT ET L'INDEX EXISTAIENT. LA PURGE, NON (audit 1.8).
///
/// <c>IdempotencyRecord.ExpiresAtUtc</c> est déclarée dans l'entité, marquée
/// <c>IsRequired()</c> dans la configuration, initialisée à 24 h, et porte un
/// INDEX DÉDIÉ dans la migration de CHACUN des sept services qui utilisent ce
/// magasin. Tout était en place pour une durée de vie — et aucune ligne de code
/// ne lisait jamais cette colonne.
///
/// C'est ce qui rendait le défaut invisible : la table avait toutes les
/// apparences d'un mécanisme réglé. Un index dédié dit à qui relit « quelqu'un
/// interroge cette colonne » ; ici, personne.
///
/// DEUX CONSÉQUENCES, ET LA SECONDE EST LA VRAIE.
///
///   1. La table grossit sans fin, comme l'outbox avant son purgeur.
///
///   2. UNE RÉSERVATION INACHEVÉE BLOQUAIT LA CLÉ POUR TOUJOURS. Si le processus
///      meurt entre la réservation et la complétion — OOM, éviction de pod,
///      redéploiement — la ligne reste sans <c>CompletedAtUtc</c>, et toute
///      nouvelle tentative avec la même clé reçoit 409. Sans échéance lue, aucun
///      geste automatique ne débloquait le client. Le correctif principal est
///      dans <c>EfIdempotencyStore.TryBeginAsync</c>, qui reprend désormais une
///      réservation périmée ; ce purgeur en est le complément — il empêche que la
///      table conserve indéfiniment des lignes que plus personne ne consultera.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// <para>
/// <b>ON EFFACE LES DEUX SORTES DE LIGNES PÉRIMÉES</b>, achevées ou non. Une
/// réservation achevée passé son échéance ne peut plus être rejouée — c'est le
/// sens même de l'échéance — et une réservation inachevée périmée est reprise à
/// la prochaine tentative. Dans les deux cas la ligne ne sert plus à rien.
/// </para>
///
/// <para>
/// <b>CE QUE CE PURGEUR NE COUVRE PAS.</b> Il n'y a AUCUNE trace conservée des
/// réservations effacées. Contrairement à l'outbox, qui garde ses lettres mortes
/// parce qu'elles signalent une perte métier, une clé d'idempotence expirée ne
/// dit rien qu'un journal d'accès ne dise mieux. Une réservation morte parce que
/// son processus a été tué ne laissera donc aucune trace après la purge — si
/// l'on veut compter ces morts, c'est une métrique à poser dans
/// <c>TryBeginAsync</c>, pas une ligne à conserver ici.
/// </para>
/// </summary>
public sealed class IdempotencyPurger<TDbContext> : BackgroundService
    where TDbContext : DbContext
{
    /// <summary>
    /// Une passe par heure, comme l'outbox. La purge n'est pas urgente : la
    /// reprise d'une clé périmée ne dépend PAS de ce service — elle est faite à la
    /// demande par <c>TryBeginAsync</c>. Si ce purgeur ne tourne jamais, aucun
    /// client n'est bloqué ; seule la table grossit.
    /// </summary>
    private static readonly TimeSpan Intervalle = TimeSpan.FromHours(1);

    /// <summary>
    /// Plafond par passe. Même raisonnement que <c>OutboxPurger</c> : un
    /// <c>DELETE</c> non borné sur une table jamais purgée tiendrait un verrou
    /// long et gonflerait le WAL.
    /// </summary>
    private const int TaillePasse = 5_000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IdempotencyPurger<TDbContext>> _logger;

    public IdempotencyPurger(
        IServiceScopeFactory scopeFactory,
        ILogger<IdempotencyPurger<TDbContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var module = typeof(TDbContext).Name.Replace("DbContext", string.Empty);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Le premier passage a lieu APRÈS le délai, pas au démarrage : au
                // boot, l'hôte a mieux à faire que d'ouvrir une transaction de
                // suppression.
                await Task.Delay(Intervalle, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var effaces = await PurgerAsync(stoppingToken);

                if (effaces > 0)
                {
                    _logger.LogInformation(
                        "Idempotence {Module} : {Effaces} réservation(s) périmée(s) effacée(s).",
                        module, effaces);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // ON N'INTERROMPT PAS LA BOUCLE. Une purge qui échoue est une
                // table qui grossit, pas un service en panne : sortir ici
                // transformerait un incident de stockage en perte définitive du
                // ménage, jusqu'au prochain redémarrage.
                _logger.LogError(
                    ex, "Idempotence {Module} : échec de la purge. Nouvelle tentative dans {Intervalle}.",
                    module, Intervalle);
            }
        }
    }

    /// <remarks>
    /// PAR TRANCHES, SUR UN CURSEUR D'ÉCHÉANCE — ET PAS SUR DES IDENTIFIANTS.
    ///
    /// `OutboxPurger` lit des identifiants puis efface par identifiant. On ne peut
    /// pas faire pareil ici : `IdempotencyRecord` n'a PAS de clé simple, sa clé
    /// primaire est le triplet (Key, Scope, Endpoint). Un `Contains` sur des
    /// n-uplets ne se traduit pas de façon fiable.
    ///
    /// On lit donc l'échéance de la N-ième ligne périmée et on efface tout ce qui
    /// lui est antérieur ou égal. Chaque passe efface AU MOINS `TaillePasse`
    /// lignes — davantage en cas d'ex æquo sur l'horodatage, ce qui est borné par
    /// le nombre d'ex æquo — donc la boucle décroît strictement et se termine.
    ///
    /// `Skip` sans `OrderBy` serait indéfini ; l'`OrderBy` sur `ExpiresAtUtc` est
    /// servi par l'index `ix_idempotency_keys_expires_at`, qui existait déjà et
    /// qui, jusqu'ici, n'était utilisé par aucune requête.
    /// </remarks>
    private async Task<int> PurgerAsync(CancellationToken cancellationToken)
    {
        var total = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

            // MAINTENANT EST RELU À CHAQUE TRANCHE, et non figé avant la boucle :
            // une purge longue ne doit pas laisser derrière elle les lignes qui ont
            // expiré pendant qu'elle tournait.
            var limite = DateTime.UtcNow;

            var perimees = dbContext.Set<IdempotencyRecord>()
                .Where(r => r.ExpiresAtUtc < limite);

            var borne = await perimees
                .OrderBy(r => r.ExpiresAtUtc)
                .Select(r => r.ExpiresAtUtc)
                .Skip(TaillePasse - 1)
                .Take(1)
                .FirstOrDefaultAsync(cancellationToken);

            // Moins d'une tranche entière reste : on efface le reliquat et on sort.
            if (borne == default)
            {
                total += await perimees.ExecuteDeleteAsync(cancellationToken);
                return total;
            }

            var effaces = await dbContext.Set<IdempotencyRecord>()
                .Where(r => r.ExpiresAtUtc <= borne)
                .ExecuteDeleteAsync(cancellationToken);

            // CEINTURE. Si une passe n'efface rien alors qu'une borne a été
            // trouvée, c'est qu'une autre instance a vidé la tranche entre les deux
            // requêtes. Continuer relirait la même borne indéfiniment.
            if (effaces == 0)
            {
                return total;
            }

            total += effaces;
        }

        return total;
    }
}
