using HBA.Shared.IntegrationEvents;

namespace HBA.Merchants.Contracts.IntegrationEvents;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE INVITATION EST PARTIE — ET CET ÉVÉNEMENT PORTE LE JETON EN CLAIR.
///
/// CE RECORD NE DOIT JAMAIS ÊTRE JOURNALISÉ. Pas de <c>ToString()</c>, jamais
/// passé à un logger, jamais recopié dans une autre table.
///
/// C'est le même compromis que <c>PasswordResetRequestedIntegrationEvent</c>, et
/// il est assumé pour les mêmes raisons : le jeton transite par la table d'outbox,
/// donc il est écrit en clair en base le temps de la livraison. Il expire en sept
/// jours, il est à usage unique, il ne vaut rien sans l'accès à la boîte mail, et
/// l'adresse du compte qui accepte doit correspondre à celle qui a été invitée.
///
/// LA SEULE ALTERNATIVE ÉTAIT PIRE, ET LE DÉPÔT EN PORTE LA CICATRICE.
///
/// Sans canal pour acheminer le secret, quelqu'un avait « résolu » le problème du
/// jeton de réinitialisation en le renvoyant dans la réponse HTTP d'un point de
/// terminaison anonyme. Une capacité manquante finit toujours par être contournée,
/// et le contournement est rarement sûr. Le jeton a ici un chemin légitime :
/// seller-service → outbox → notification-service → boîte mail de l'invité.
///
/// IL EST AUSSI PUBLIÉ AU RENVOI, avec un jeton NEUF — l'ancien cesse alors de
/// fonctionner. Il n'y a jamais deux liens vivants pour une même invitation.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record SellerMemberInvitedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid InvitationId { get; init; }

    public required string Email { get; init; }

    /// <summary>Le nom saisi par celui qui invite, pour personnaliser le message.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Le nom de la boutique — l'invité doit savoir QUI l'invite.</summary>
    public required string ShopName { get; init; }

    /// <summary>
    /// SECRET, ET DÉSORMAIS CHIFFRÉ (AES-GCM, `ISecretProtector`).
    ///
    /// Le jeton partait ici EN CLAIR. Il traversait donc l'outbox de seller-service —
    /// table que rien ne purgeait avant `OutboxPurger` — puis un topic Kafka retenu
    /// sept jours. Or ce jeton EST le justificatif : quiconque le lit entre dans la
    /// boutique avec le rôle prévu pour l'invité. Un accès en LECTURE à l'une ou
    /// l'autre suffisait.
    ///
    /// Seul notification-service le déchiffre, juste avant de composer le lien.
    /// La clé — `Security:SecretProtection:Key` — doit être IDENTIQUE côté
    /// seller-service et côté notification-service.
    /// </summary>
    public required string ProtectedInvitationToken { get; init; }

    public required DateTime ExpiresOnUtc { get; init; }
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN MEMBRE A REJOINT L'ÉQUIPE — L'ÉVÉNEMENT QUI REND LE MEMBRE UTILISABLE.
///
/// SANS SON CONSOMMATEUR CÔTÉ IDENTITY, TOUT LE MODULE RESTE INERTE.
///
/// `MapSellerGroup` filtre sur la claim de rôle du jeton, et `GrantSellerRoleHandler`
/// ne l'accorde qu'au propriétaire, à l'inscription. Un membre parfaitement écrit
/// en base est donc refoulé par le ROUTAGE, avant tout handler et avant que la
/// moindre permission de ce module ne soit consultée.
///
/// C'est exactement le trou documenté côté restaurant dans
/// `GrantFoodPartnerRoleHandler` : « le personnel n'est pas couvert […] l'écran de
/// cuisine, qui est fait POUR eux, leur reste fermé ». Le consommateur est le
/// lot B′ ; cet événement est ce qui le rend possible.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record SellerMemberJoinedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid MemberId { get; init; }

    public required Guid UserId { get; init; }

    /// <summary>
    /// Les rôles au niveau du vendeur.
    /// <para>
    /// DES IDENTIFIANTS DE RÔLES, PAS DES PERMISSIONS. Une liste de permissions
    /// serait périmée dès qu'un rôle change, et il faudrait rejouer l'événement
    /// pour toute l'équipe. Qui a besoin des permissions les redemande.
    /// </para>
    /// </summary>
    public required IReadOnlyList<Guid> SellerRoleIds { get; init; }

    public required IReadOnlyList<Guid> StoreIds { get; init; }
}

/// <summary>
/// Les rôles d'un membre ont changé.
/// <para>
/// C'EST L'ÉVÉNEMENT D'INVALIDATION DU CACHE D'AUTORISATION (§31, §50).
/// Sans lui, un droit retiré reste utilisable jusqu'à l'expiration du TTL — des
/// minutes pendant lesquelles un membre rétrogradé garde ses anciens pouvoirs.
/// </para>
/// </summary>
public sealed record SellerMemberRolesUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid MemberId { get; init; }

    public required Guid UserId { get; init; }

    public required IReadOnlyList<Guid> SellerRoleIds { get; init; }
}

/// <summary>Un membre a été affecté à une boutique.</summary>
public sealed record SellerMemberStoreAssignedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid MemberId { get; init; }

    public required Guid UserId { get; init; }

    public required Guid StoreId { get; init; }
}

/// <summary>Un membre a été retiré d'une boutique.</summary>
public sealed record SellerMemberStoreUnassignedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid MemberId { get; init; }

    public required Guid UserId { get; init; }

    public required Guid StoreId { get; init; }
}

/// <summary>
/// L'accès d'un membre est suspendu.
/// <para>
/// C'EST L'ÉVÉNEMENT DU SCÉNARIO §53 : « après suspension, toutes les répliques
/// refusent l'utilisateur sans attendre l'expiration du TTL ». Il ne tiendra cette
/// promesse qu'avec un cache réellement distribué — aujourd'hui le cache est en
/// MÉMOIRE, et dans un groupe de consommateurs une seule réplique reçoit le
/// message. C'est l'objet du lot 0a, et c'est dit ici pour que personne ne croie
/// la garantie acquise du seul fait que cet événement existe.
/// </para>
/// </summary>
public sealed record SellerMemberSuspendedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid MemberId { get; init; }

    public required Guid UserId { get; init; }
}

/// <summary>L'accès d'un membre suspendu est rouvert.</summary>
public sealed record SellerMemberActivatedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid MemberId { get; init; }

    public required Guid UserId { get; init; }
}

/// <summary>
/// Un membre est sorti de l'équipe — révoqué ou parti de lui-même.
/// <para>
/// LE CONSOMMATEUR D'IDENTITY NE DOIT PAS RETIRER LE RÔLE `Seller` AVEUGLÉMENT.
/// </para>
/// <para>
/// Le compte peut être propriétaire d'un AUTRE dossier vendeur — un comptable chez
/// un confrère qui est par ailleurs vendeur lui-même. Révoquer sans vérifier
/// enfermerait dehors quelqu'un qui détenait ce rôle pour une autre raison. La
/// révocation n'est pas le symétrique de l'octroi.
/// </para>
/// </summary>
public sealed record SellerMemberRevokedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid MemberId { get; init; }

    public required Guid UserId { get; init; }

    /// <summary>
    /// Le compte appartient-il encore à une AUTRE équipe vendeur ?
    /// </summary>
    /// <remarks>
    /// C'EST LE CHAMP QUI EMPÊCHE D'ENFERMER QUELQU'UN DEHORS.
    ///
    /// Vrai, le rôle `Seller` doit rester. Faux, il peut être retiré. Le calcul
    /// appartient à seller-service, seul détenteur de la réponse : identity ne
    /// connaît pas les appartenances, et l'interroger dans l'autre sens créerait
    /// un appel de service circulaire — merchant dépend déjà d'identity.
    /// </remarks>
    public required bool HasOtherSellerMembership { get; init; }
}

/// <summary>
/// La propriété du dossier a changé de porteur.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// IL PORTE LES DEUX COMPTES, ET LES DEUX DOIVENT ÊTRE PRÉVENUS.
///
/// Le bénéficiaire hérite de six permissions critiques d'un coup — dont la
/// fermeture du dossier et le compte de reversement. Le cédant, lui, les perd
/// toutes, et ce message est le seul signal qu'il en reçoit s'il n'est pas
/// l'auteur du geste.
///
/// C'est aussi le seul événement du module dont l'effet est IRRÉVERSIBLE sans
/// l'accord de l'autre partie : reprendre la propriété demande que le nouveau
/// propriétaire la retransfère. Une notification manquée n'est donc pas un
/// désagrément, c'est la disparition du seul avertissement.
///
/// AUCUN CONSOMMATEUR CÔTÉ IDENTITY, ET C'EST CORRECT.
///
/// Le rôle plateforme `Seller` dépend de l'APPARTENANCE, pas du rôle vendeur
/// porté. Un transfert ne fait entrer ni sortir personne de l'équipe : les deux
/// comptes restent membres, les deux gardent le rôle `Seller`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record SellerOwnershipTransferredIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid PreviousOwnerMemberId { get; init; }

    public required Guid PreviousOwnerUserId { get; init; }

    public required Guid NewOwnerMemberId { get; init; }

    public required Guid NewOwnerUserId { get; init; }
}
