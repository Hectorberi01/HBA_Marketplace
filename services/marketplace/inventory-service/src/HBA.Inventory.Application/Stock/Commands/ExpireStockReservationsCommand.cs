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

/// <summary>
/// Libère les réservations `Active` dont l'échéance est dépassée.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE BALAYAGE N'EXISTAIT PAS, ET LE STOCK VENDABLE S'ÉRODAIT (ISSUE-031,
/// CRITICAL).
///
/// `ExpiresAtUtc` était calculée à chaque réservation, écrite en base depuis la
/// migration initiale… et relue par PERSONNE. Aucun `BackgroundService` n'existait
/// dans inventory. Toute réservation non confirmée immobilisait donc son stock
/// DÉFINITIVEMENT.
///
/// L'érosion est le mot juste : chaque panier abandonné retire quelques unités de
/// la vente, pour toujours, sans erreur ni alerte. Elle ne se remarque qu'au
/// moment où un article affiche « rupture » alors que l'entrepôt est plein — et à
/// ce moment-là, plus rien ne dit d'où viennent les unités manquantes.
///
/// IDEMPOTENT. Une réservation `Expired` n'est plus `Active` : ni la requête de
/// sélection ni l'agrégat ne la reprennent. Rejouer ce lot, ou l'interrompre au
/// milieu, ne produit ni double libération ni perte.
///
/// UNE RÉSERVATION `Confirmed` N'EST JAMAIS TOUCHÉE, MÊME LARGEMENT EXPIRÉE :
/// c'est du stock vendu, `OnHand` en a déjà été retiré. La garde est dans
/// l'agrégat (`InventoryItem.ExpireReservations`), doublée par le filtre de la
/// requête — un article peut avoir bougé entre la sélection et l'écriture.
///
/// L'HORLOGE EST LUE UNE SEULE FOIS PAR LOT.
///
/// Sinon deux articles du même lot seraient jugés à des instants différents, et
/// une même exécution poserait des `ExpiredAtUtc` qui ne se recoupent pas. Le
/// module n'a pas d'abstraction d'horloge — `ReserveStockCommandHandler` lit
/// `DateTime.UtcNow` directement — et en introduire une ici pour un seul appelant
/// ajouterait une pièce que rien d'autre n'utilise.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
        //
        // Le contexte dispatche les événements de domaine et draine l'outbox à
        // chaque sauvegarde : une sauvegarde à vide coûterait un aller-retour pour
        // zéro ligne, toutes les quelques minutes, à jamais.
        if (reservations > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new StockExpirySweepReport(articlesTouches, reservations, volume);
    }
}
