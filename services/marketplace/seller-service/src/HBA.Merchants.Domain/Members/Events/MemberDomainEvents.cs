using HBA.Shared.Domain.Events;

namespace HBA.Merchants.Domain.Members.Events;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES ÉVÉNEMENTS DE L'APPARTENANCE — ET LE PLUS IMPORTANT DE TOUS.
///
/// `SellerMemberJoinedDomainEvent` EST CELUI QUI REND LE MEMBRE UTILISABLE.
///
/// Il devient un événement d'intégration consommé par identity-service, qui greffe
/// le rôle `Seller` sur le compte. Tant que ce chemin n'existe pas (lot B′), un
/// membre parfaitement écrit en base est refoulé par `MapSellerGroup` — qui ne
/// regarde que la claim de rôle du jeton — avant d'atteindre le moindre handler,
/// et donc avant que la moindre permission de ce module ne soit consultée.
///
/// C'est exactement le trou déjà documenté côté restaurant, dans
/// `GrantFoodPartnerRoleHandler` : « le personnel n'est pas couvert […] l'écran de
/// cuisine, qui est fait POUR eux, leur reste fermé ».
///
/// LES IDENTIFIANTS DE RÔLES VOYAGENT, PAS LES PERMISSIONS.
///
/// Un événement qui porterait la liste des permissions effectives serait périmé
/// dès qu'un rôle change, et il faudrait le rejouer pour tout le monde. Le
/// consommateur qui a besoin des permissions les redemande — c'est ce que fera
/// `CheckMerchantCapability`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record SellerMemberJoinedDomainEvent(
    Guid MemberId,
    Guid SellerId,
    Guid UserId,
    IReadOnlyList<Guid> SellerRoleIds,
    IReadOnlyList<Guid> StoreIds) : DomainEvent;

/// <summary>
/// Les rôles d'un membre ont changé.
/// <para>
/// INVALIDE LE CACHE D'AUTORISATION. Sans cela, un droit retiré reste
/// utilisable jusqu'à l'expiration du TTL — dix minutes pendant lesquelles un
/// membre rétrogradé garde ses anciens pouvoirs.
/// </para>
/// </summary>
public sealed record SellerMemberRolesChangedDomainEvent(
    Guid MemberId, Guid SellerId, Guid UserId, IReadOnlyList<Guid> SellerRoleIds) : DomainEvent;

public sealed record SellerMemberStoreAssignedDomainEvent(
    Guid MemberId, Guid SellerId, Guid UserId, Guid StoreId) : DomainEvent;

public sealed record SellerMemberStoreUnassignedDomainEvent(
    Guid MemberId, Guid SellerId, Guid UserId, Guid StoreId) : DomainEvent;

/// <summary>
/// L'accès est suspendu.
/// <para>
/// C'EST L'ÉVÉNEMENT DU SCÉNARIO §53, CELUI QUI DOIT MORDRE TOUT DE SUITE.
/// « Après suspension, toutes les répliques refusent l'utilisateur sans attendre
/// l'expiration du TTL » — ce qui suppose un cache réellement distribué (lot 0a).
/// </para>
/// </summary>
public sealed record SellerMemberSuspendedDomainEvent(
    Guid MemberId, Guid SellerId, Guid UserId) : DomainEvent;

public sealed record SellerMemberActivatedDomainEvent(
    Guid MemberId, Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>
/// L'accès est révoqué, ou le membre est parti de lui-même.
/// </summary>
/// <param name="HasOtherSellerMembership">
/// Le compte appartient-il encore à une AUTRE équipe vendeur ?
/// </param>
/// <remarks>
/// RETIRER LE RÔLE `Seller` N'EST PAS SYMÉTRIQUE DE L'AVOIR DONNÉ.
///
/// Un comptable révoqué chez un commerçant peut être vendeur lui-même, ou
/// comptable chez un confrère. Révoquer le rôle sans le savoir l'enfermerait
/// dehors de son PROPRE dossier — une panne dont ni lui ni le support ne
/// devineraient la cause, puisque rien n'aurait été fait sur ce dossier-là.
///
/// ET CE DRAPEAU EST CALCULÉ PAR L'APPELANT, JAMAIS ICI. VOICI POURQUOI.
///
/// `ModuleDbContext.SaveChangesAsync` dépêche les événements de domaine AVANT
/// `base.SaveChangesAsync` — c'est ce qui permet de drainer l'outbox dans la même
/// transaction. Un gestionnaire d'événement qui interrogerait le dépôt lirait donc
/// l'état d'AVANT la révocation : le membre y figurerait encore actif, le drapeau
/// vaudrait toujours « oui », et le rôle ne serait jamais retiré. Silencieusement,
/// et exactement à l'envers.
///
/// La couche Application compte AVANT de muter, comme elle le fait déjà pour le
/// dernier propriétaire. Le paramètre est obligatoire : aucun chemin ne peut
/// l'oublier.
/// </remarks>
public sealed record SellerMemberRevokedDomainEvent(
    Guid MemberId, Guid SellerId, Guid UserId, bool HasOtherSellerMembership) : DomainEvent;

// ═════════════════════════════════════════════════════════════════════════════
// IL N'Y A PAS D'ÉVÉNEMENT DE DOMAINE POUR L'INVITATION, ET C'EST DÉLIBÉRÉ.
//
// L'événement d'intégration correspondant doit porter le JETON EN CLAIR — sans
// quoi notification-service ne peut pas composer le lien, et l'invitation n'a
// aucun moyen d'atteindre son destinataire. Or l'agrégat ne connaît PAS le jeton :
// il ne reçoit que son empreinte, et c'est exactement ce que demande le §7.
//
// Faire remonter le secret par un événement de domaine obligerait à le loger dans
// l'agrégat sans le persister — un champ dont l'unique raison d'être serait de
// contourner sa propre conception. `MemberCommandHandler` publie donc directement,
// dans la même transaction : il est le seul endroit où le jeton existe.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA PROPRIÉTÉ DU DOSSIER A CHANGÉ DE PORTEUR.
///
/// ÉCRIT PARCE QUE TROIS GARDES RENVOYAIENT À UNE OPÉRATION INEXISTANTE
/// (ISSUE-040).
///
/// Le domaine refusait déjà, en trois endroits, tout ce qui aurait pu déplacer le
/// rôle OWNER — et chacun des trois messages disait au vendeur de faire un
/// transfert de propriété :
///
///   • « Le rôle de propriétaire se transfère, il ne se retire pas. »
///   • « Le rôle de propriétaire ne s'attribue que par un transfert de propriété. »
///   • « Le dernier propriétaire ne peut pas être retiré : transférez la propriété
///     d'abord. »
///
/// Le transfert n'existait nulle part : ni méthode, ni commande, ni route. La
/// permission `OWNERSHIP_TRANSFER` était déclarée, critique, réservée au
/// propriétaire — et ne gardait rien.
///
/// Conséquence : un dossier dont le propriétaire disparaissait — compte supprimé,
/// personne partie — devenait DÉFINITIVEMENT inadministrable. `SELLER_CLOSE`,
/// `PAYOUT_CONFIGURE` et `SELLER_REACTIVATE` ne sont portés par aucun autre rôle,
/// et `EnsureCanAdminister` interdit à un `SELLER_ADMIN` de toucher un
/// propriétaire.
///
/// IL PORTE LES DEUX COMPTES, PAS SEULEMENT LE NOUVEAU.
///
/// Le cédant doit être notifié : c'est le geste le plus irréversible du module, et
/// le seul signal qu'il en reste s'il n'en est pas l'auteur. Un événement qui ne
/// porterait que le bénéficiaire rendrait cette notification impossible sans une
/// lecture de plus.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record SellerOwnershipTransferredDomainEvent(
    Guid SellerId,
    Guid PreviousOwnerMemberId,
    Guid PreviousOwnerUserId,
    Guid NewOwnerMemberId,
    Guid NewOwnerUserId) : DomainEvent;
