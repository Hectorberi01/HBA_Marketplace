using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Stations;

public readonly record struct PreparationStationId(Guid Value)
{
    public static PreparationStationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>UN POSTE DE PRÉPARATION (cahier des charges §9) : GRILL, PIZZA, DRINKS.</summary>
public sealed class PreparationStation : AggregateRoot<PreparationStationId>
{
    private PreparationStation()
    {
    }

    private PreparationStation(
        PreparationStationId id, Guid restaurantId, string name, string code, int displayOrder)
        : base(id)
    {
        RestaurantId = restaurantId;
        Name = name;
        Code = code;
        DisplayOrder = displayOrder;
        IsActive = true;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public Guid RestaurantId { get; private set; }

    /// <summary>Ce que lit un humain : « Grillades », « Boissons ».</summary>
    public string Name { get; private set; } = default!;

    /// <summary>Le code court affiché sur le ticket et l'écran : GRILL, PIZZA, DRINKS.</summary>
    public string Code { get; private set; } = default!;

    public int DisplayOrder { get; private set; }

    /// <summary>Poste fermé sans être supprimé — le grill du dimanche.</summary>
    public bool IsActive { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }

    public const int MaxCodeLength = 16;

    public static Result<PreparationStation> Create(
        Guid restaurantId, string name, string code, int displayOrder = 0)
    {
        if (restaurantId == Guid.Empty)
        {
            return Error.Validation("food.station.restaurant_required", "Le poste doit appartenir à un restaurant.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("food.station.name_required", "Le nom du poste est obligatoire.");
        }

        var normalise = Normalize(code);
        if (normalise is null)
        {
            return Error.Validation(
                "food.station.code_required",
                $"Le code du poste est obligatoire (lettres et chiffres, {MaxCodeLength} caractères au plus).");
        }

        return new PreparationStation(PreparationStationId.New(), restaurantId, name.Trim(), normalise, displayOrder);
    }

    public Result Rename(string name, string code)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("food.station.name_required", "Le nom du poste est obligatoire."));
        }

        var normalise = Normalize(code);
        if (normalise is null)
        {
            return Result.Failure(Error.Validation("food.station.code_required", "Le code du poste est obligatoire."));
        }

        Name = name.Trim();
        Code = normalise;
        Touch();
        return Result.Success();
    }

    public Result Reorder(int displayOrder)
    {
        DisplayOrder = displayOrder;
        Touch();
        return Result.Success();
    }

    public Result Activate()
    {
        IsActive = true;
        Touch();
        return Result.Success();
    }

    /// <summary>Ferme le poste.</summary>
    public Result Deactivate()
    {
        IsActive = false;
        Touch();
        return Result.Success();
    }

    /// <summary>Majuscules, sans espace ni ponctuation.</summary>
    private static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var propre = new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        return propre.Length is 0 or > MaxCodeLength ? null : propre;
    }

    private void Touch() => UpdatedOnUtc = DateTime.UtcNow;
}

/// <summary>Accès aux postes de préparation.</summary>
public interface IPreparationStationRepository
{
    Task<PreparationStation?> GetByIdAsync(
        PreparationStationId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PreparationStation>> ListByRestaurantAsync(
        Guid restaurantId, CancellationToken cancellationToken = default);

    /// <summary>Le code est unique PAR RESTAURANT : deux maquis ont chacun leur GRILL.</summary>
    Task<PreparationStation?> GetByCodeAsync(
        Guid restaurantId, string code, CancellationToken cancellationToken = default);

    /// <summary>Combien d'articles désignent ce poste ? Sert la garde de suppression.</summary>
    Task<int> CountItemsUsingAsync(
        Guid preparationStationId, CancellationToken cancellationToken = default);

    Task AddAsync(PreparationStation station, CancellationToken cancellationToken = default);

    void Remove(PreparationStation station);
}
