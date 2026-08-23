using HBA.Shared.Domain.Events;

namespace HBA.Catalog.Domain.Brands.Events;

/// <summary>
/// Un vendeur demande une marque absente du référentiel (§19 :
/// <c>catalog.brand.requested</c>).
///
/// Le premier consommateur attendu est la notification d'administration : sans
/// événement, une demande dort dans une table que personne n'ouvre — et le vendeur
/// attend une réponse qui ne viendra pas.
/// </summary>
public sealed record BrandRequestedDomainEvent(
    Guid RequestId,
    Guid SellerId,
    string Name) : DomainEvent;

/// <summary>
/// La demande est approuvée (§19 : <c>catalog.brand.approved</c>).
///
/// `BrandId` PEUT DÉSIGNER UNE MARQUE QUI EXISTAIT DÉJÀ.
///
/// C'est le cas fréquent : l'administrateur rattache « samsumg » au « Samsung » du
/// catalogue plutôt que d'en créer un second. Un consommateur qui traiterait cet
/// événement comme « une marque vient d'être créée » en créerait un doublon chez
/// lui — exactement ce que le mécanisme de demande sert à éviter.
/// </summary>
public sealed record BrandRequestApprovedDomainEvent(
    Guid RequestId,
    Guid SellerId,
    Guid BrandId,
    string RequestedName) : DomainEvent;
