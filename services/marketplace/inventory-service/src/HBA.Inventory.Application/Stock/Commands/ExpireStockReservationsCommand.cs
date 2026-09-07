using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Inventory.Application.Abstractions;
using HBA.Inventory.Domain.Stock;

namespace HBA.Inventory.Application.Stock.Commands;

/// <summary>Ce qu'un tour de balayage a rendu à la vente.</summary>
/// <param name="Items">Articles touchés.</param>
/// <param name="Reservations">Réservations passées en `Expired`.</param>
/// <param name="Quantity">Unités rendues à la vente — le chiffre que l'audit réclame.</param>
public sealed record StockExpirySweepReport(int Items, int Reservations, int Quantity)
{
    public static readonly StockExpirySweepReport Empty = new(0, 0, 0);

    public bool IsEmpty => Reservations == 0;
}

/// <summary>Libère les réservations `Active` dont l'échéance est dépassée.</summary>
public sealed record ExpireStockReservationsCommand(int BatchSize = 100) : ICommand<StockExpirySweepReport>;

internal sealed class ExpireStockReservationsCommandHandler
    : ICommandHandler<ExpireStockReservationsCommand, StockExpirySweepReport>
{
    private readonly IInventoryItemRepository _repository;
    private readonly IInventoryUnitOfWork _unitOfWork;

    public ExpireStockReservationsCommandHandler(
        IInventoryItemRepository repository, IInventoryUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<StockExpirySweepReport>> Handle(
        ExpireStockReservationsCommand command, CancellationToken cancellationToken)
    {
        var maintenant = DateTime.UtcNow;

        var articles = await _repository.ListWithExpirableReservationsAsync(
            maintenant, command.BatchSize, cancellationToken);

        if (articles.Count == 0)
        {
            return StockExpirySweepReport.Empty;
        }

        var articlesTouches = 0;
        var reservations = 0;
        var volume = 0;

        foreach (var article in articles)
        {
            var bilan = article.ExpireReservations(maintenant);
            if (bilan.IsEmpty)
            {
                continue;
            }

            articlesTouches++;
            reservations += bilan.Count;
            volume += bilan.Quantity;
        }

        // UN SEUL `SaveChanges` POUR TOUT LE LOT, ET AUCUN SI RIEN N'A CHANGÉ.
        if (reservations > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new StockExpirySweepReport(articlesTouches, reservations, volume);
    }
}
