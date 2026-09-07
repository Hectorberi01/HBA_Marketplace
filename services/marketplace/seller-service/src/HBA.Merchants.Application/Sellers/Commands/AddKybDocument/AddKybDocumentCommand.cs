using HBA.Shared.Application.Messaging;

namespace HBA.Merchants.Application.Sellers.Commands.AddKybDocument;

/// <summary>Ajoute une pièce justificative KYB (passe la vérification en revue).</summary>
/// <summary>Rattache une pièce KYB déjà téléversée dans le service média.</summary>
/// <param name="RequestedByUserId">
/// Le compte qui rattache. Comparé au DÉPOSANT du média — voir le gestionnaire.
/// </param>
public sealed record AddKybDocumentCommand(
    Guid SellerId, string Type, Guid MediaId, Guid RequestedByUserId = default) : ICommand<Guid>;
