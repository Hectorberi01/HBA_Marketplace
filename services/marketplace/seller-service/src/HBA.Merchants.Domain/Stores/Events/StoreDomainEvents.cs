using HBA.Shared.Domain.Events;

namespace HBA.Merchants.Domain.Stores.Events;

/// <summary>Une boutique vient d'être créée (état Draft : rien n'est encore en vente).</summary>
public sealed record StoreCreatedDomainEvent(Guid StoreId, Guid SellerId, string Name) : DomainEvent;

/// <summary>
/// Une boutique ouvre : ses offres redeviennent achetables.
///
/// Consommé côté Products pour relever les offres retirées PAR CETTE FERMETURE —
/// et par elle seule. Voir SellerCatalogSuspension : ce qu'un modérateur avait
/// suspendu reste suspendu.
/// </summary>
public sealed record StoreOpenedDomainEvent(Guid StoreId, Guid SellerId) : DomainEvent;

/// <summary>
/// Une boutique ferme — que ce soit par décision du vendeur ou de la plateforme.
///
/// UN SEUL ÉVÉNEMENT POUR LES DEUX, ET C'EST DÉLIBÉRÉ.
///
/// Le catalogue n'a pas à savoir POURQUOI la boutique ferme : dans les deux cas
/// ses offres quittent la vente. La distinction vit dans le STATUT — elle décide
/// qui a le droit de rouvrir — et le motif voyage pour rester lisible.
///
/// Deux événements auraient imposé deux handlers identiques, et le jour où l'un
/// aurait été modifié sans l'autre, une des deux fermetures serait devenue
/// silencieuse.
/// </summary>
public sealed record StoreClosedDomainEvent(Guid StoreId, Guid SellerId, string? Reason) : DomainEvent;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA BOUTIQUE EST SUSPENDUE PAR LA PLATEFORME.
///
/// UN ÉVÉNEMENT PROPRE, PARCE QUE `StoreClosed` NE DISAIT PAS LA MÊME CHOSE.
///
/// `Suspend` émettait `StoreClosedDomainEvent` — le MÊME type que la fermeture
/// volontaire du vendeur. Un consommateur qui recevait « boutique fermée » ne
/// pouvait pas savoir s'il s'agissait d'un vendeur parti en congés ou d'une
/// boutique écartée pour contrefaçon. Le motif était bien transporté, mais c'est
/// du texte libre : rien ne peut s'y brancher.
///
/// Ce que cela empêchait concrètement : afficher « temporairement fermée, de
/// retour bientôt » dans un cas et retirer la boutique des résultats dans
/// l'autre. C'est aussi la moitié manquante d'`outlet.status.changed` du §10.3.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record StoreSuspendedDomainEvent(
    Guid StoreId, Guid SellerId, string? Reason) : DomainEvent;

/// <summary>
/// La plateforme lève la sanction. La boutique repasse en `Closed`, PAS en `Open` :
/// c'est le vendeur qui rouvre, quand son stock et ses prix sont à jour.
///
/// LA LEVÉE N'ÉMETTAIT RIEN DU TOUT.
///
/// Rien d'urgent ne s'ensuit — la boutique reste hors vente — mais un service qui
/// a mémorisé « cette boutique est sanctionnée », pour l'exclure d'un classement
/// ou d'une mise en avant, ne l'apprenait jamais autrement qu'en relisant tout.
/// </summary>
public sealed record StoreSuspensionLiftedDomainEvent(Guid StoreId, Guid SellerId) : DomainEvent;
