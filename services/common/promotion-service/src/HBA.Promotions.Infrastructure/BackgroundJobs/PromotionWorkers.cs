using HBA.Promotions.Application.Promotions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HBA.Promotions.Infrastructure.BackgroundJobs;

/// <summary>
/// Réglage du balayage des retenues expirées. Résolu par
/// <c>PromotionsModuleInstaller</c> à partir de la configuration.
///
/// UN ENREGISTREMENT EXPLICITE PLUTÔT QU'UNE CONSTANTE.
///
/// La période doit pouvoir être raccourcie en incident — après un rattrapage
/// massif, ou pour observer le balayeur travailler — sans reconstruire l'image.
/// </summary>
/// <param name="Interval">Délai entre deux tours.</param>
/// <param name="BatchSize">Coupons repris par tour.</param>
public sealed record CouponHoldSweepOptions(TimeSpan Interval, int BatchSize);

/// <summary>
/// Rend au budget des campagnes les retenues de coupon dont l'échéance est passée.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUN BALAYEUR N'EXISTAIT DANS PROMOTION (ISSUE-053).
///
/// `CouponReservation.ExpiresAtUtc` était écrite à chaque retenue, l'index partiel
/// `ix_coupon_usages_expiring` était posé « pour le ménage des retenues expirées »,
/// et ce ménage n'a jamais été écrit. `Coupon.HoldLifetime` valait donc trente
/// minutes sur le papier et l'infini en pratique.
///
/// L'encadré d'`IPromotionModuleApi` affirme que « la compensation ne dépend pas de
/// la bonne volonté — ni de la survie — de celui qui a demandé la retenue ». C'est
/// ce travailleur qui rend la phrase vraie. Sans lui, elle dépendait exactement de
/// cela : un checkout interrompu, un processus tué, un `ReleaseAsync` jamais
/// appelé, et l'enveloppe restait retenue pour toujours.
///
/// IL JOURNALISE LE VOLUME, PAS SEULEMENT LE FAIT DE TOURNER.
///
/// Le nombre de retenues ne dit pas combien d'ENVELOPPE dormait. Au premier
/// démarrage après correction, cette ligne chiffrera d'un coup tout ce que
/// l'absence de balayeur avait immobilisé depuis la mise en service — attendez-vous
/// à un nombre élevé, et à plusieurs tours de rattrapage avant qu'il ne retombe au
/// régime normal. Des campagnes affichées `Exhausted` redeviendront `Active` : ce
/// n'est pas un incident, c'est la mesure de ce que l'absence de balayeur coûtait.
///
/// SILENCIEUX QUAND IL N'Y A RIEN À FAIRE. Un tour à vide n'écrit pas : sinon le
/// journal serait noyé par une ligne toutes les cinq minutes, et personne ne verrait
/// passer celles qui comptent.
///
/// UNE SEULE INSTANCE — ET LA RAISON A CHANGÉ. Comme l'outbox et les balayages
/// d'inventory et de return-refund, ce tour ne pose pas de
/// `SELECT … FOR UPDATE SKIP LOCKED` : deux répliques liraient le même lot. Le
/// travail en double n'est PAS dangereux — `Coupon.ExpireHolds` ne voit que les
/// retenues encore `Held`, donc la seconde réplique ne trouve plus rien à libérer
/// et rend 0.
///
/// Cet encadré annonçait jusqu'ici une seconde raison, plus grave : « les deux
/// écritures concurrentes sur `Promotion.BudgetConsumed` ne sont pas protégées par
/// un jeton de concurrence dans ce module. Avant de mettre promotion-service à
/// l'échelle horizontale, il faut soit le verrou de ligne, soit un jeton de version
/// sur la campagne. » **C'est fait** : `promotions` porte un jeton de concurrence
/// depuis le lot 8.3 (`PromotionConfiguration.UsePostgresRowVersion`). Le perdant
/// d'une course reçoit 409 et rejoue sur l'état à jour.
///
/// CE QUI RESTE VRAI POUR AUTANT : ce tour n'est pas idempotent au sens du
/// débit — il ne fait que libérer des retenues expirées, et une seconde réplique
/// n'en trouve aucune. Le passage à l'échelle horizontale n'est plus bloqué par le
/// budget, il reste simplement inutile ici. Une file de travail partagée
/// (`FOR UPDATE SKIP LOCKED`) serait le geste si le volume l'exigeait un jour.
///
/// La structure — portée DI par tour, `PeriodicTimer`, annulation traitée comme un
/// arrêt normal, incident journalisé sans tuer le travailleur — est reprise telle
/// quelle d'`ExpireStockReservationsWorker` : deux balayeurs qui se ressemblent se
/// relisent, et celui-là a déjà été éprouvé.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class ExpireCouponHoldsWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CouponHoldSweepOptions _options;
    private readonly ILogger<ExpireCouponHoldsWorker> _logger;

    public ExpireCouponHoldsWorker(
        IServiceScopeFactory scopeFactory,
        CouponHoldSweepOptions options,
        ILogger<ExpireCouponHoldsWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // UNE PORTÉE PAR TOUR. Le `DbContext` est `Scoped` : le réutiliser
                // d'un tour sur l'autre garderait en suivi tous les coupons déjà
                // balayés, et un incident laisserait ses entités modifiées dans le
                // contexte du tour suivant, qui les committerait.
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                var resultat = await sender.Send(
                    new ExpireCouponHoldsCommand(_options.BatchSize), stoppingToken);

                if (resultat.IsFailure)
                {
                    _logger.LogError(
                        "Expiration des retenues de coupon : {Code} — {Message}",
                        resultat.Error.Code, resultat.Error.Message);
                }
                else if (!resultat.Value.IsEmpty)
                {
                    // Le VOLUME d'abord : c'est lui qui dit combien d'enveloppe
                    // dormait. Le nombre de retenues et de coupons situe l'ampleur.
                    _logger.LogInformation(
                        "Expiration des retenues de coupon : {Budget} unite(s) de budget rendue(s), "
                        + "{Reservations} retenue(s) expiree(s) sur {Coupons} coupon(s).",
                        resultat.Value.Budget, resultat.Value.Reservations, resultat.Value.Coupons);
                }
            }
            catch (OperationCanceledException)
            {
                break; // arrêt normal de l'application
            }
            catch (Exception ex)
            {
                // Un incident de base ne doit jamais tuer le travailleur : le tour
                // suivant reprendra les mêmes retenues, rien n'est perdu — c'est
                // précisément ce que l'idempotence du balayage garantit.
                _logger.LogError(ex, "Expiration des retenues de coupon : echec du cycle.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
