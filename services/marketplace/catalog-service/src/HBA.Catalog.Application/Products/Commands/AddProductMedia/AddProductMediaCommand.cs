using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Products.Commands.AddProductMedia;

/// <summary>Rattache à un produit un média DÉJÀ DÉPOSÉ dans le service média.</summary>
/// <param name="RequestedByUserId">
/// Le compte qui rattache. Comparé au DÉPOSANT du média — voir l'encadré du
/// gestionnaire.
/// </param>
public sealed record AddProductMediaCommand(
    Guid ProductId,
    Guid MediaId,
    string Type = "Image",
    string? AltText = null,
    bool IsPrimary = false,
    Guid RequestedByUserId = default) : ICommand<Guid>;
