using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HBA.Shared.Infrastructure.Outbox;

/// <summary>
/// Efface les messages d'outbox déjà traités, passé un délai de rétention.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LA TABLE D'OUTBOX N'ÉTAIT JAMAIS PURGÉE — ET ELLE CONTIENT DES SECRETS.
///
/// `ProcessedOnUtc` était renseigné, et la ligne restait. Indéfiniment. Sur les
/// 23 modules. Deux conséquences, dont une seule est évidente :
///
///   1. LA TABLE GROSSIT SANS FIN. Chaque commande, chaque paiement, chaque
///      notification y laisse une ligne de JSON. Les deux index partiels ne la
///      couvrent pas (ils filtrent sur `ProcessedOnUtc IS NULL`), donc le coût
///      est en écriture et en stockage, pas en lecture — c'est lent à devenir
///      visible, et pénible à rattraper.
///
///   2. LE JSON EST EN CLAIR, ET CERTAINS ÉVÉNEMENTS PORTENT UN SECRET.
///      `PasswordResetRequestedIntegrationEvent` et
///      `EmailVerificationRequestedIntegrationEvent` transportent le code
///      envoyé par e-mail. Tant que la ligne existe, ce code est lisible par
///      quiconque a un accès en LECTURE à `identity.outbox_messages` — une
///      sauvegarde, un export analytique, un compte de consultation. La lecture
///      d'une table paraît anodine ; ici elle vaut prise de compte.
///
///      La purge ne suffit pas à régler ce défaut — le vrai correctif est de ne
///      pas faire voyager le secret du tout (voir IMPLEMENTATION_DEFECTS
///      ISSUE-071). Mais elle ramène l'exposition de « pour toujours » à
///      « quelques jours », et c'est vrai dans toutes les hypothèses de
///      correction.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// <para>
/// <b>LES LETTRES MORTES NE SONT JAMAIS EFFACÉES.</b> Un message
/// <c>DeadLetteredOnUtc IS NOT NULL</c> est une perte métier qu'un exploitant doit
/// pouvoir lire, comprendre et rejouer. Le purger reviendrait à effacer la trace
/// d'une commande qui n'a jamais abouti — précisément ce que la mise en lettre
/// morte cherchait à rendre visible.
/// </para>
///
/// <para>
/// <b>UNE SEULE INSTANCE À LA FOIS</b>, comme le processeur. Deux purgeurs
/// concurrents ne se corrompent pas mutuellement — <c>DELETE</c> est idempotent —
/// mais ils doublent la charge pour rien. Le service suit donc le même
/// <c>OUTBOX_ENABLED</c>.
/// </para>
/// </summary>
public sealed class OutboxPurger<TDbContext> : BackgroundService
    where TDbContext : DbContext, IOutboxDbContext
{
    /// <summary>
    /// Cadence : une passe par heure. La purge n'est pas urgente — un message
    /// traité peut attendre — et une passe fréquente ne ferait qu'ajouter du
    /// bruit et des connexions. Le premier passage a lieu après le délai, pas au
    /// démarrage : au boot, l'hôte a mieux à faire que d'ouvrir une transaction
    /// de suppression.
    /// </summary>
    private static readonly TimeSpan Intervalle = TimeSpan.FromHours(1);

    /// <summary>
    /// Plafond par passe. Un <c>DELETE</c> non borné sur une table qui n'a jamais
    /// été purgée peut porter sur des millions de lignes : il tiendrait un verrou
    /// long, gonflerait le WAL, et retarderait le processeur d'outbox qui écrit
    /// dans la même table. On efface par tranches, et on repasse.
    /// </summary>
    private const int TaillePasse = 5_000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxPurger<TDbContext>> _logger;

    public OutboxPurger(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxPurger<TDbContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Durée de rétention des messages traités.
    ///
    /// Sept jours par défaut : assez pour qu'un exploitant puisse reconstituer un
    /// incident de la semaine écoulée, assez court pour qu'un secret qui aurait
    /// voyagé ne dorme pas des mois. Réglable par <c>OUTBOX_RETENTION_DAYS</c>.
    ///
    /// Une valeur illisible ou négative retombe sur la valeur par défaut plutôt
    /// que d'effacer plus que prévu. Se tromper dans le sens de la conservation se
    /// répare ; dans l'autre, non.
    /// </summary>
    internal static TimeSpan Retention
    {
        get
        {
            var brut = Environment.GetEnvironmentVariable("OUTBOX_RETENTION_DAYS");

            return int.TryParse(brut, out var jours) && jours > 0
                ? TimeSpan.FromDays(jours)
                : TimeSpan.FromDays(7);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var module = typeof(TDbContext).Name.Replace("DbContext", string.Empty);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Intervalle, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Arrêt normal de l'hôte : on sort sans bruit.
                return;
            }

            try
            {
                var effaces = await PurgerAsync(stoppingToken);

                if (effaces > 0)
                {
                    _logger.LogInformation(
                        "Outbox {Module} : {Effaces} message(s) traité(s) effacé(s) (rétention {Jours} j).",
                        module, effaces, Retention.TotalDays);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // ON N'INTERROMPT PAS LA BOUCLE. Une purge qui échoue est un
                // désagrément ; un service hôte qui s'arrête parce que sa purge a
                // échoué est une panne. On journalise et on repassera dans une heure.
                _logger.LogError(
                    ex, "Outbox {Module} : échec de la purge. Nouvelle tentative dans {Intervalle}.",
                    module, Intervalle);
            }
        }
    }

    private async Task<int> PurgerAsync(CancellationToken cancellationToken)
    {
        var limite = DateTime.UtcNow - Retention;
        var total = 0;

        // Par tranches, jusqu'à ce qu'il ne reste rien à effacer — ou que l'hôte
        // s'arrête.
        //
        // DEUX REQUÊTES PAR TRANCHE, ET C'EST VOLONTAIRE.
        //
        // On lit d'abord les identifiants, on efface ensuite par identifiant. Écrire
        // directement `.Take(n).ExecuteDeleteAsync()` serait plus court, mais
        // PostgreSQL n'a pas de `DELETE … LIMIT` : la traduction dépend du
        // fournisseur et peut lever à l'exécution. Ici, `Where(ids.Contains(…))`
        // est traduit partout, sans surprise. À une passe par heure, le second
        // aller-retour ne coûte rien.
        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

            var identifiants = await dbContext.OutboxMessages
                .Where(m => m.ProcessedOnUtc != null
                            && m.ProcessedOnUtc < limite
                            && m.DeadLetteredOnUtc == null)
                .OrderBy(m => m.OccurredOnUtc)
                .Select(m => m.Id)
                .Take(TaillePasse)
                .ToListAsync(cancellationToken);

            if (identifiants.Count == 0)
            {
                break;
            }

            var effaces = await dbContext.OutboxMessages
                .Where(m => identifiants.Contains(m.Id))
                .ExecuteDeleteAsync(cancellationToken);

            total += effaces;

            if (identifiants.Count < TaillePasse)
            {
                break;
            }
        }

        return total;
    }
}
