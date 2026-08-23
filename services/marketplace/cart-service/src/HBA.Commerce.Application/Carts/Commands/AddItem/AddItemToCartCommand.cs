using HBA.Shared.Application.Messaging;

namespace HBA.Commerce.Application.Carts.Commands.AddItem;

/// <summary>Ajoute une offre au panier actif de l'acheteur (créé si besoin).</summary>
public sealed record AddItemToCartCommand(Guid BuyerId, Guid OfferId, int Quantity) : ICommand<Guid>;
