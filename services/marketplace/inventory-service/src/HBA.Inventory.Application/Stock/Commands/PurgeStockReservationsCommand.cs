using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Inventory.Domain.Stock;

namespace HBA.Inventory.Application.Stock.Commands;

/// <summary>
/// Efface les réservations TERMINÉES trop anciennes pour servir encore.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// `stock_reservations` NE DÉCROISSAIT JAMAIS, ET C'ÉTAIT ÉCRIT DEPUIS LE
/// LOT 3.5 SANS ÊTRE TRAITÉ.
///
/// L'encadré de `StockReservation` le dit lui-même : « la table ne décroît plus
/// […] les repositories chargent `Include(i => i.Reservations)` en entier pour
/// pouvoir calculer `Reserved`. Sur un article très vendu, la collection finira
/// par peser. » Six `Include` du service se dégradent donc avec le NOMBRE DE
/// VENTES de l'article — y compris ceux que le lot 8.4 a bornés, puisque la borne
/// porte sur les articles et non sur leurs enfants.
///
/// C'est la forme de dégradation la plus désagréable : elle croît avec le succès.
///
/// POURQUOI UNE PURGE ET NON UN `Include` FILTRÉ.
///
/// Filtrer sur `IsActive` serait le réflexe — `Reserved` ne somme que les actives
/// et `Reserve` ne cherche que les actives. Mais `ConfirmReservation` teste
/// `Any(r => r.OrderId == orderId && r.Status == Confirmed)` pour être
/// IDEMPOTENT : il lit une ligne TERMINALE. Un `Include` filtré la lui cacherait,
/// et une confirmation rejouée décrémenterait le stock une seconde fois.
///
/// D'OÙ LA CONTRAINTE SUR LA RÉTENTION : ELLE DOIT DÉPASSER TOUTE FENÊTRE DE
/// REJEU.
///
/// Effacer une ligne `Confirmed` encore atteignable par un rejeu Kafka ferait
/// tomber cette idempotence. Le défaut est de <b>quatre-vingt-dix jours</b> —
/// très au-delà de la rétention d'un topic (jours) et des reprises d'outbox
/// (minutes). Le raccourcir sous une semaine est une décision à prendre en
/// connaissance de cette ligne-là.
///
/// SEULES LES TERMINÉES. Une réservation `Active` dont l'échéance est passée
/// immobilise toujours du stock : c'est au balayeur d'expiration de la rendre à
/// la vente, en l'écrivant. La purge qui l'effacerait rendrait ce stock
/// disponible SANS TRACE — le contraire exact de ce qu'ISSUE-031 a corrigé.
///
/// CE QUE CETTE PURGE NE COUVRE PAS. Elle borne la table, pas la collection
/// d'un article très vendu SUR LA PÉRIODE DE RÉTENTION. Un SKU à mille ventes par
/// jour portera encore quatre-vingt-dix mille lignes. Si cela devient le
/// problème, le remède suivant n'est pas une rétention plus courte — c'est de
/// sortir `Reserved` de l'agrégat et de le calculer en SQL.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="Retention">Âge minimum d'une ligne terminée pour être effacée.</param>
/// <param name="BatchSize">Nombre maximum de lignes effacées en un tour.</param>
public sealed record PurgeStockReservationsCommand(TimeSpan Retention, int BatchSize = 500)
    : ICommand<int>;

internal sealed class PurgeStockReservationsCommandHandler
    : ICommandHandler<PurgeStockReservationsCommand, int>
{
    private readonly IInventoryItemRepository _repository;

    public PurgeStockReservationsCommandHandler(IInventoryItemRepository repository)
        => _repository = repository;

    /// <remarks>
    /// AUCUN `SaveChangesAsync` : le dépôt efface par `ExecuteDeleteAsync`, qui
    /// écrit immédiatement et court-circuite l'unité de travail. C'est voulu — une
    /// purge n'est pas un geste métier et n'a rien à publier — et c'est écrit là
    /// où quelqu'un chercherait le `SaveChanges` manquant.
    /// </remarks>
    public async Task<Result<int>> Handle(
        PurgeStockReservationsCommand command, CancellationToken cancellationToken)
    {
        if (command.Retention <= TimeSpan.Zero || command.BatchSize <= 0)
        {
            // ON REFUSE PLUTÔT QUE DE RETOMBER SUR UN DÉFAUT. Une rétention nulle
            // effacerait l'historique du jour même, y compris des lignes qu'un rejeu
            // peut encore réclamer. Mieux vaut un balayeur qui échoue bruyamment
            // qu'un balayeur qui efface trop, en silence.
            return Result.Failure<int>(Error.Validation(
                "inventory.purge.invalid_settings",
                "La rétention et la taille de lot doivent être strictement positives."));
        }

        var avant = DateTime.UtcNow - command.Retention;

        return await _repository.PurgeTerminalReservationsAsync(
            avant, command.BatchSize, cancellationToken);
    }
}
