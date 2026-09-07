using HBA.Shared.Domain.Primitives;

namespace HBA.Orders.Domain.Orders;

/// <summary>Une option retenue sur un plat commandé.</summary>
public sealed class OrderLineOption : Entity<Guid>
{
    private OrderLineOption()
    {
    }

    internal OrderLineOption(Guid id, Guid optionGroupId, Guid optionId)
        : base(id)
    {
        OptionGroupId = optionGroupId;
        OptionId = optionId;
    }

    /// <summary>Le groupe dont l'option provient.</summary>
    public Guid OptionGroupId { get; private set; }

    public Guid OptionId { get; private set; }
}
