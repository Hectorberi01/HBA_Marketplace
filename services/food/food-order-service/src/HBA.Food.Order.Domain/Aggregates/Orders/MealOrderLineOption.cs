using HBA.Shared.Domain.Primitives;

namespace HBA.FoodOrders.Domain.Orders;

/// <summary>
/// Une option retenue sur un plat commandé.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// DES IDENTIFIANTS, PAS DES LIBELLÉS NI DES MONTANTS.
///
/// C'est `FoodOrder`, dans restaurant-service, qui fige « Grande taille », le
/// supplément et le prix servi, parce qu'il tient la carte et qu'il sert le
/// ticket de cuisine. Les recopier ici en donnerait une SECONDE version, écrite
/// au paiement, qui divergerait de la première dès qu'un prix change entre le
/// paiement et l'acceptation du restaurant. Deux montants figés pour la même
/// option, et personne pour dire lequel a été facturé.
///
/// Cette commande porte donc ce qui lui appartient : ce que le client a DEMANDÉ.
/// Ce qui a été SERVI et à quel prix appartient à la cuisine.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>
    /// Le groupe dont l'option provient.
    ///
    /// Redondant en apparence — la carte connaît le groupe de chaque option. Mais
    /// c'est ce qui permet de relire une commande sans interroger la carte, y
    /// compris pour un plat retiré du menu depuis.
    /// </summary>
    public Guid OptionGroupId { get; private set; }

    public Guid OptionId { get; private set; }
}
