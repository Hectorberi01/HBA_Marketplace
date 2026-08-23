using HBA.Shared.Domain.Primitives;

namespace HBA.Commerce.Domain.Carts;

/// <summary>
/// Une option choisie sur un plat : la taille, l'accompagnement, le supplément.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUN PRIX ICI, ET C'EST DÉLIBÉRÉ.
///
/// Le supplément d'une option a bien un montant, mais il est déjà fondu dans
/// `CartItem.UnitBaseAmount` — le même instantané de prix que pour une offre.
/// Le recopier par option donnerait deux totaux à tenir d'accord, et le jour où
/// ils divergeraient, personne ne saurait lequel a raison.
///
/// Surtout, ce prix n'est PAS celui qui sera facturé : Food le recalcule à la
/// réception de la commande, à partir de sa propre carte. Le panier affiche une
/// estimation ; la cuisine fait foi. Stocker un prix ici lui donnerait une
/// autorité qu'il n'a pas.
///
/// NI LIBELLÉ NON PLUS. « Grande taille » se lit dans la carte du restaurant,
/// qui peut le corriger ; figé dans le panier, il continuerait d'afficher une
/// faute d'orthographe corrigée depuis des semaines.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>
    /// Le groupe dont l'option provient.
    ///
    /// REDONDANT EN APPARENCE — l'option connaît son groupe côté Food. Mais
    /// c'est ce qui permet à la cuisine de regrouper l'affichage sans interroger
    /// la carte, et de repérer qu'un groupe obligatoire n'a rien reçu.
    /// </summary>
    public Guid OptionGroupId { get; private set; }

    public Guid OptionId { get; private set; }
}
