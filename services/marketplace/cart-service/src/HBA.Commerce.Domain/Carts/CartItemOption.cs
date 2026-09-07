using HBA.Shared.Domain.Primitives;

namespace HBA.Commerce.Domain.Carts;

/// <summary>Une option choisie sur un plat : la taille, l'accompagnement, le supplément.</summary>
public sealed class CartItemOption : Entity<Guid>
{
    private CartItemOption()
    {
    }

    internal CartItemOption(Guid id, Guid optionGroupId, Guid optionId)
        : base(id)
    {
        OptionGroupId = optionGroupId;
        OptionId = optionId;
    }

    /// <summary>Le groupe dont l'option provient.</summary>
    public Guid OptionGroupId { get; private set; }

    public Guid OptionId { get; private set; }
}
