using HBA.Shared.Domain.Primitives;

namespace HBA.Orders.Domain.Orders;

/// <summary>
/// Une option retenue sur un plat commandé.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// DES IDENTIFIANTS, PAS DES LIBELLÉS NI DES MONTANTS.
///
/// Première version : cette entité figeait aussi « Grande taille », « Poulet » et
/// le supplément, au motif qu'une commande est un contrat et qu'une carte peut
/// changer. L'argument est juste — mais il désignait le mauvais module.
///
/// C'est `FoodOrder`, dans Food, qui fige ces libellés et ces suppléments à la
/// réception de la commande, parce que c'est lui qui tient la carte et qui sert le
/// ticket de cuisine. Les recopier ici en donnerait une SECONDE version, écrite au
/// checkout, qui divergerait de la première dès qu'un prix change entre le
/// paiement et l'acceptation du restaurant. Deux montants figés pour la même
/// option, et personne pour dire lequel a été facturé.
///
/// Ordering porte donc ce qui lui appartient : ce que le client a DEMANDÉ. Ce qui
/// a été SERVI et à quel prix appartient à Food.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>
    /// Le groupe dont l'option provient.
    ///
    /// Redondant en apparence — Food connaît le groupe de chaque option. Mais
    /// c'est ce qui permet de relire une commande sans interroger la carte, y
    /// compris pour un plat retiré du menu depuis.
    /// </summary>
    public Guid OptionGroupId { get; private set; }

    public Guid OptionId { get; private set; }
}
