using HBA.Shared.Domain.Primitives;

namespace HBA.FoodOrders.Domain.Orders;

/// <summary>Une option retenue sur un plat commandé.</summary>
public sealed class MealOrderLineOption : Entity<Guid>
{
    private MealOrderLineOption()
    {
    }

    internal MealOrderLineOption(Guid id, Guid optionGroupId, Guid optionId)
        : base(id)
    {
        OptionGroupId = optionGroupId;
        OptionId = optionId;
    }

    /// <summary>Le groupe dont l'option provient.</summary>
    public Guid OptionGroupId { get; private set; }

    public Guid OptionId { get; private set; }
}
