using HBA.Shared.Domain.Primitives;

namespace HBA.FoodCarts.Domain.Carts;

/// <summary>Une option retenue sur un plat : la taille, l'accompagnement, le supplément.</summary>
public sealed class FoodCartItemOption : Entity<Guid>
{
    private FoodCartItemOption()
    {
    }

    internal FoodCartItemOption(Guid id, Guid optionGroupId, Guid optionId)
        : base(id)
    {
        OptionGroupId = optionGroupId;
        OptionId = optionId;
    }

    /// <summary>Le groupe dont l'option provient.</summary>
    public Guid OptionGroupId { get; private set; }

    public Guid OptionId { get; private set; }
}
