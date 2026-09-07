using HBA.Food.Application.Abstractions;
using HBA.Food.Contracts;
using HBA.Food.Domain.Menus;
using HBA.Food.Domain.Stations;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Application.Stations;

// ── Postes de préparation (cahier §9) ───────────────────────────────────────

public sealed record CreateStationCommand(
    Guid RestaurantId, string Name, string Code, int DisplayOrder) : ICommand<Guid>;

public sealed record RenameStationCommand(
    Guid RestaurantId, Guid StationId, string Name, string Code) : ICommand;

public sealed record SetStationActiveCommand(Guid RestaurantId, Guid StationId, bool Active) : ICommand;

public sealed record ReorderStationCommand(Guid RestaurantId, Guid StationId, int DisplayOrder) : ICommand;

/// <summary>Supprime un poste.</summary>
public sealed record DeleteStationCommand(Guid RestaurantId, Guid StationId) : ICommand;

/// <summary>Fixe le temps et le poste de préparation d'un article (§6, §9).</summary>
public sealed record SetItemPreparationCommand(
    Guid RestaurantId, Guid ItemId, int? Minutes, Guid? StationId) : ICommand;

public sealed record ListStationsQuery(Guid RestaurantId) : IQuery<IReadOnlyList<PreparationStationView>>;

internal sealed class StationCommandHandler
    : ICommandHandler<CreateStationCommand, Guid>,
      ICommandHandler<RenameStationCommand>,
      ICommandHandler<SetStationActiveCommand>,
      ICommandHandler<ReorderStationCommand>,
      ICommandHandler<DeleteStationCommand>,
      ICommandHandler<SetItemPreparationCommand>,
      IQueryHandler<ListStationsQuery, IReadOnlyList<PreparationStationView>>
{
    private readonly IPreparationStationRepository _stations;
    private readonly IMenuItemRepository _items;
    private readonly IFoodUnitOfWork _unitOfWork;

    public StationCommandHandler(
        IPreparationStationRepository stations, IMenuItemRepository items, IFoodUnitOfWork unitOfWork)
    {
        _stations = stations;
        _items = items;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateStationCommand command, CancellationToken cancellationToken)
    {
        var poste = PreparationStation.Create(
            command.RestaurantId, command.Name, command.Code, command.DisplayOrder);

        if (poste.IsFailure)
        {
            return Result.Failure<Guid>(poste.Error);
        }

        // LE CODE EST UNIQUE PAR RESTAURANT.
        var existant = await _stations.GetByCodeAsync(
            command.RestaurantId, poste.Value.Code, cancellationToken);

        if (existant is not null)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "food.station.code_taken", $"Le poste « {poste.Value.Code} » existe déjà."));
        }

        await _stations.AddAsync(poste.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return poste.Value.Id.Value;
    }

    public Task<Result> Handle(RenameStationCommand command, CancellationToken cancellationToken)
        => OnStationAsync(command.StationId, command.RestaurantId, cancellationToken,
            s => s.Rename(command.Name, command.Code));

    public Task<Result> Handle(SetStationActiveCommand command, CancellationToken cancellationToken)
        => OnStationAsync(command.StationId, command.RestaurantId, cancellationToken,
            s => command.Active ? s.Activate() : s.Deactivate());

    public Task<Result> Handle(ReorderStationCommand command, CancellationToken cancellationToken)
        => OnStationAsync(command.StationId, command.RestaurantId, cancellationToken,
            s => s.Reorder(command.DisplayOrder));

    public async Task<Result> Handle(DeleteStationCommand command, CancellationToken cancellationToken)
    {
        var poste = await LoadAsync(command.StationId, command.RestaurantId, cancellationToken);
        if (poste is null)
        {
            return Result.Failure(Introuvable);
        }

        var utilisations = await _stations.CountItemsUsingAsync(command.StationId, cancellationToken);
        if (utilisations > 0)
        {
            return Result.Failure(Error.Conflict(
                "food.station.in_use",
                utilisations == 1
                    ? "1 article est encore rattaché à ce poste. Réaffectez-le, ou fermez le poste au lieu de le supprimer."
                    : $"{utilisations} articles sont encore rattachés à ce poste. Réaffectez-les, ou fermez le poste au lieu de le supprimer."));
        }

        _stations.Remove(poste);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> Handle(SetItemPreparationCommand command, CancellationToken cancellationToken)
    {
        // LE POSTE DOIT EXISTER ET APPARTENIR À CE RESTAURANT.
        if (command.StationId is { } stationId && stationId != Guid.Empty)
        {
            var poste = await LoadAsync(stationId, command.RestaurantId, cancellationToken);
            if (poste is null)
            {
                return Result.Failure(Introuvable);
            }
        }

        var article = await _items.GetByIdAsync(new MenuItemId(command.ItemId), cancellationToken);
        if (article is null || article.RestaurantId != command.RestaurantId)
        {
            return Result.Failure(Error.NotFound("food.item.not_found", "Article introuvable."));
        }

        var result = article.SetPreparation(command.Minutes, command.StationId);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<PreparationStationView>>> Handle(
        ListStationsQuery query, CancellationToken cancellationToken)
    {
        var postes = await _stations.ListByRestaurantAsync(query.RestaurantId, cancellationToken);

        IReadOnlyList<PreparationStationView> vues = postes
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.Code, StringComparer.Ordinal)
            .Select(s => new PreparationStationView(s.Id.Value, s.Name, s.Code, s.IsActive, s.DisplayOrder))
            .ToList();

        return Result.Success(vues);
    }

    private static readonly Error Introuvable =
        Error.NotFound("food.station.not_found", "Poste de préparation introuvable.");

    private async Task<PreparationStation?> LoadAsync(
        Guid stationId, Guid restaurantId, CancellationToken cancellationToken)
    {
        var poste = await _stations.GetByIdAsync(new PreparationStationId(stationId), cancellationToken);
        return poste is null || poste.RestaurantId != restaurantId ? null : poste;
    }

    private async Task<Result> OnStationAsync(
        Guid stationId, Guid restaurantId, CancellationToken cancellationToken,
        Func<PreparationStation, Result> action)
    {
        var poste = await LoadAsync(stationId, restaurantId, cancellationToken);
        if (poste is null)
        {
            return Result.Failure(Introuvable);
        }

        var result = action(poste);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
