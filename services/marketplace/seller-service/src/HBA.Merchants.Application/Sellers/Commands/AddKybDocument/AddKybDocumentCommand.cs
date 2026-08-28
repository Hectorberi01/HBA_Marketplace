using HBA.Shared.Application.Messaging;

namespace HBA.Merchants.Application.Sellers.Commands.AddKybDocument;

/// <summary>Ajoute une pièce justificative KYB (passe la vérification en revue).</summary>
/// <summary>
/// Rattache une pièce KYB déjà téléversée dans le service média.
///
/// CE PARAMÈTRE ÉTAIT UNE URL VENUE DU CLIENT, ET C'ÉTAIT UNE FAILLE.
///
/// Un vendeur pouvait rattacher à son dossier l'adresse de n'importe quel objet
/// du bucket privé — dont la pièce d'identité d'un autre vendeur — puis en
/// demander l'URL présignée, que la route lui signait sans discuter.
///
/// L'identifiant seul ne suffit pas à fermer la porte : c'est l'APPELANT qui doit
/// vérifier que le média est de nature `SellerDocument` et appartient à ce
/// vendeur. Sellers ne connaît pas le service média.
/// </summary>
/// <param name="RequestedByUserId">
/// Le compte qui rattache. Comparé au DÉPOSANT du média — voir le gestionnaire.
///
/// Optionnel en fin d'enregistrement pour ne pas casser les appelants existants à
/// la compilation, mais le défaut est le cas FERMÉ : `Guid.Empty` est REFUSÉ. Un
/// appelant qui oublie de le passer n'obtient pas un contrôle désactivé.
/// </param>
public sealed record AddKybDocumentCommand(
    Guid SellerId, string Type, Guid MediaId, Guid RequestedByUserId = default) : ICommand<Guid>;
