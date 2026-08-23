using HBA.Shared.Domain.Primitives;

namespace HBA.FoodCarts.Domain.Carts;

/// <summary>
/// Une option retenue sur un plat : la taille, l'accompagnement, le supplément.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// NI PRIX NI LIBELLÉ, MÊME MAINTENANT QUE LE PANIER LIT LA CARTE.
///
/// C'est un choix qui mérite d'être défendu, parce qu'il a changé de raison.
///
/// Avant la séparation, le panier vivait dans cart-service et ne connaissait PAS
/// la carte : il n'aurait pas su quoi écrire. Aujourd'hui il la lit — il pourrait
/// donc figer « Grande taille, +500 ». Il ne le fait pas.
///
/// Le supplément est déjà fondu dans <c>FoodCartItem.UnitBaseAmount</c>. Le
/// recopier par option donnerait deux totaux à tenir d'accord, et le jour où ils
/// divergeraient personne ne saurait lequel fait foi.
///
/// Surtout, ce montant N'EST PAS celui qui sera facturé : la commande le
/// recalcule à partir de la carte au moment où elle est passée. Le panier affiche
/// une estimation ; la carte fait foi. Y figer un prix lui donnerait une autorité
/// qu'il n'a pas — et un panier ouvert depuis la veille afficherait le prix
/// d'hier comme s'il était garanti.
///
/// Le libellé, lui, se lit dans la carte, qui peut le corriger. Figé ici, il
/// continuerait d'afficher une faute d'orthographe rectifiée depuis des semaines.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>
    /// Le groupe dont l'option provient.
    ///
    /// REDONDANT EN APPARENCE — l'option connaît son groupe dans la carte. Mais
    /// c'est ce qui permet à la cuisine de regrouper l'affichage sans interroger
    /// la carte, et de repérer qu'un groupe obligatoire n'a rien reçu.
    /// </summary>
    public Guid OptionGroupId { get; private set; }

    public Guid OptionId { get; private set; }
}
