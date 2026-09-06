using HBA.Inventory.Application.Stock.Commands;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HBA.Inventory.Infrastructure.BackgroundJobs;

/// <summary>
/// Réglage du balayage d'expiration. Résolu par
/// <c>InventoryModuleInstaller</c> à partir de la configuration.
///
/// UN ENREGISTREMENT EXPLICITE PLUTÔT QU'UNE CONSTANTE.
///
/// La période doit pouvoir être raccourcie en incident — après un rattrapage
/// massif, ou pour observer le balayeur travailler — sans reconstruire l'image.
/// </summary>
/// <param name="Interval">Délai entre deux tours.</param>
/// <param name="BatchSize">Articles repris par tour.</param>
public sealed record StockReservationSweepOptions(TimeSpan Interval, int BatchSize);

/// <summary>
/// Réglage de la PURGE des réservations terminées.
/// </summary>
/// <remarks>
/// SÉPARÉ DU BALAYAGE D'EXPIRATION, ET LES DEUX CADENCES N'ONT RIEN À VOIR.
///
/// L'expiration rend du stock à la vente : elle doit tourner toutes les quelques
/// MINUTES, sinon de la marchandise dort. La purge n'a aucun effet métier : une
/// fois par JOUR suffit, et plus souvent ne ferait que relire une table pour n'y
/// rien trouver.
/// </remarks>
/// <param name="Interval">Délai entre deux tours.</param>
/// <param name="Retention">Âge minimum d'une ligne terminée pour être effacée.</param>
/// <param name="BatchSize">Lignes effacées par tour.</param>
public sealed record StockReservationPurgeOptions(
    TimeSpan Interval, TimeSpan Retention, int BatchSize);

/// <summary>
/// Rend à la vente les réservations dont l'échéance est dépassée.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUN BALAYEUR N'EXISTAIT DANS INVENTORY (ISSUE-031, CRITICAL).
///
/// `ExpiresAtUtc` était écrite à chaque réservation et lue par personne. Chaque
/// panier abandonné, chaque paiement laissé en plan, chaque commande tombée entre
/// deux étapes retirait donc quelques unités de la vente — DÉFINITIVEMENT. Rien
/// ne le signalait : la marchandise est en entrepôt, la base la dit réservée, et
/// aucune commande ne la porte.
///
/// IL JOURNALISE LE VOLUME, PAS SEULEMENT LE FAIT DE TOURNER.
///
/// L'audit l'exige nommément, et c'est la seule mesure utile : le nombre de
/// lignes ne dit pas combien de MARCHANDISE dormait. Au premier démarrage après
/// correction, cette ligne de journal chiffrera d'un coup tout ce que l'absence
/// de balayeur avait immobilisé depuis la mise en service — attendez-vous à un
/// nombre élevé, et à plusieurs tours de rattrapage avant qu'il ne retombe au
/// régime normal.
///
/// SILENCIEUX QUAND IL N'Y A RIEN À FAIRE. Un tour à vide n'écrit pas : sinon
/// le journal serait noyé par une ligne toutes les cinq minutes, et personne ne
/// verrait passer celles qui comptent.
///
/// UNE SEULE INSTANCE. Comme l'outbox (`OutboxRegistration`) et les balayages
/// de return-refund, ce tour ne pose pas de `SELECT … FOR UPDATE SKIP LOCKED` :
/// deux répliques liraient le même lot. Le jeton de concurrence de l'agrégat
/// (`xmin`, via `StockVersion`) empêche la double écriture — la seconde échoue en
/// `DbUpdateConcurrencyException`, journalisée et reprise au tour suivant — mais
/// pas le travail en double. Avant de mettre inventory-service à l'échelle
/// horizontale, il faut le verrou de ligne. C'est une contrainte de déploiement,
/// pas une opinion.
///
/// La structure — portée DI par tour, `PeriodicTimer`, annulation traitée comme
/// un arrêt normal, incident journalisé sans tuer le travailleur — est reprise
/// telle quelle d'`ExpireReturnsWorker` : deux balayeurs qui se ressemblent se
/// relisent, et celui-là a déjà été éprouvé.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class ExpireStockReservationsWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StockReservationSweepOptions _options;
    private readonly ILogger<ExpireStockReservationsWorker> _logger;

    public ExpireStockReservationsWorker(
        IServiceScopeFactory scopeFactory,
        StockReservationSweepOptions options,
        ILogger<ExpireStockReservationsWorker> logger)
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
                // d'un tour sur l'autre garderait en suivi tous les articles déjà
                // balayés, et un incident laisserait ses entités modifiées dans le
                // contexte du tour suivant, qui les committerait.
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                var resultat = await sender.Send(
                    new ExpireStockReservationsCommand(_options.BatchSize), stoppingToken);

                if (resultat.IsFailure)
                {
                    _logger.LogError(
                        "Expiration des reservations de stock : {Code} — {Message}",
                        resultat.Error.Code, resultat.Error.Message);
                }
                else if (!resultat.Value.IsEmpty)
                {
                    // Le VOLUME d'abord : c'est lui qui dit combien de marchandise
                    // dormait. Le nombre de lignes et d'articles situe l'ampleur.
                    _logger.LogInformation(
                        "Expiration des reservations de stock : {Quantity} unite(s) rendue(s) a la vente, "
                        + "{Reservations} reservation(s) expiree(s) sur {Items} article(s).",
                        resultat.Value.Quantity, resultat.Value.Reservations, resultat.Value.Items);
                }
            }
            catch (OperationCanceledException)
            {
                break; // arrêt normal de l'application
            }
            catch (Exception ex)
            {
                // Un incident de base ne doit jamais tuer le travailleur : le tour
                // suivant reprendra les mêmes réservations, rien n'est perdu — c'est
                // précisément ce que l'idempotence du balayage garantit.
                _logger.LogError(ex, "Expiration des reservations de stock : echec du cycle.");
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

/// <summary>
/// Efface les réservations terminées trop anciennes pour servir encore.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// IL MANQUAIT DEPUIS LE LOT 3.5, ET SON ABSENCE ÉTAIT DOCUMENTÉE.
///
/// `StockReservation` annonçait « un travail de purge daté » — resté à écrire
/// pendant six lots, pendant que `stock_reservations` croissait de façon
/// monotone et que six `Include(i => i.Reservations)` se dégradaient avec.
///
/// Le raisonnement complet — pourquoi une purge et non un `Include` filtré,
/// pourquoi la rétention doit dépasser toute fenêtre de rejeu — est dans
/// `PurgeStockReservationsCommand`. Il n'est pas recopié ici.
///
/// CE TRAVAILLEUR EFFACE DES LIGNES. C'est le seul du service dans ce cas, et
/// la seule protection contre un réglage absurde est le refus explicite du
/// gestionnaire de commande — rétention nulle ou négative rejetée. Ne pas lui
/// ajouter de repli sur un défaut : un balayeur qui efface trop, en silence, ne
/// se remarque qu'une fois l'historique parti.
///
/// UNE SEULE INSTANCE, comme son voisin : deux répliques liraient le même lot.
/// Ici la conséquence est bénigne — la seconde suppression ne trouverait plus
/// rien — mais c'est la même contrainte de déploiement, et elle vaut d'être dite
/// au même endroit.
///
/// La structure est reprise telle quelle d'`ExpireStockReservationsWorker` :
/// portée DI par tour, `PeriodicTimer`, annulation traitée comme un arrêt normal,
/// incident journalisé sans tuer le travailleur.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class PurgeStockReservationsWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StockReservationPurgeOptions _options;
    private readonly ILogger<PurgeStockReservationsWorker> _logger;

    public PurgeStockReservationsWorker(
        IServiceScopeFactory scopeFactory,
        StockReservationPurgeOptions options,
        ILogger<PurgeStockReservationsWorker> logger)
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
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                var resultat = await sender.Send(
                    new PurgeStockReservationsCommand(_options.Retention, _options.BatchSize),
                    stoppingToken);

                if (resultat.IsFailure)
                {
                    _logger.LogError(
                        "Purge des reservations de stock : {Code} — {Message}",
                        resultat.Error.Code, resultat.Error.Message);
                }
                else if (resultat.Value > 0)
                {
                    // SILENCIEUX QUAND IL N'Y A RIEN À FAIRE, comme son voisin :
                    // au régime normal ce tour ne trouve rien pendant des mois, et
                    // une ligne quotidienne « 0 effacée » ferait perdre celles qui
                    // comptent.
                    //
                    // Le premier passage sur une base ancienne, lui, effacera
                    // beaucoup et le dira — sur plusieurs tours, la borne de lot
                    // étant volontairement basse.
                    _logger.LogInformation(
                        "Purge des reservations de stock : {Count} ligne(s) terminee(s) effacee(s) "
                        + "(retention {Days} jour(s)).",
                        resultat.Value, (int)_options.Retention.TotalDays);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Purge des reservations de stock : echec du cycle.");
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
