using HBA.Shared.Application.Messaging;

namespace HBA.Orders.Application.Orders.Commands.PlaceOrder;

/// <summary>Place une commande à partir du panier actif de l'acheteur (déclenche le Saga).</summary>
/// <param name="DeliveryQuoteId">
/// Le devis de course qui FIXE les frais de livraison.
///
/// IL N'Y A PLUS DE `ShippingFee` ICI, ET C'EST LA CORRECTION ELLE-MÊME.
///
/// Cette commande en portait un, alimenté par le corps de la requête HTTP.
/// L'acheteur posait `ShippingFee = 0`, se faisait livrer gratuitement, et la
/// plateforme achetait pourtant la course au prix réel — perte sèche par
/// commande, que rien ne signalait ni ne mesurait.
///
/// Ajouter un contrôle « le montant doit correspondre au devis » aurait laissé le
/// champ en place, donc la possibilité de l'oublier au prochain appelant.
/// Supprimer le champ supprime le mensonge possible : il n'y a plus rien à
/// falsifier. Le gestionnaire RELIT le devis auprès de delivery-service et emploie
/// SON montant.
///
/// L'IDENTIFIANT, LUI, VIENT BIEN DE L'ACHETEUR — ET C'EST SOUHAITABLE.
///
/// Il désigne le prix qu'on lui a AFFICHÉ. Le lui redemander en interne
/// produirait un second devis, calculé sur la grille du moment, qui peut différer
/// de celui qu'il a accepté à l'écran : on lui facturerait un montant auquel il
/// n'a jamais consenti. Un identifiant n'est pas un montant — il désigne un
/// montant que seul le serveur peut lire.
/// </param>
public sealed record PlaceOrderCommand(
    Guid BuyerId,
    ShippingAddressInput? ShippingAddress = null,
    string? DeliveryQuoteId = null) : ICommand<Guid>;

/// <summary>Adresse de livraison choisie au checkout (figée sur la commande).</summary>
public sealed record ShippingAddressInput(
    string? Label, string? Recipient, string? Phone,
    string? CommuneCode, string? Quartier, string? Landmark, string? Line1, string? CountryCode,
    double? Latitude, double? Longitude);
