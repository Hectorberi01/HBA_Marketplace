using HBA.Merchants.Domain.Members;
using HBA.Shared.Domain.Results;

namespace HBA.Merchants.Application.Members;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA RÉSOLUTION D'UN ACTEUR — UN SEUL ENDROIT, ET C'EST LA GARDE ELLE-MÊME.
///
/// CES ROUTES N'UTILISENT PAS `DenyUnlessOwnSellerAsync`, ET CE N'EST PAS UN OUBLI.
///
/// La garde historique répond « êtes-vous LE vendeur ? » en comparant le dossier
/// résolu depuis le jeton à celui de l'URL. Ici la question n'est plus celle-là :
/// c'est « appartenez-vous à ce vendeur, et avec quels droits ». Poser les deux
/// donnerait deux sources de vérité pour la même décision, et la plus permissive
/// finirait par l'emporter le jour où l'une des deux serait oubliée sur une route.
///
/// Conséquence assumée : un ADMINISTRATEUR de la plateforme n'a pas d'appartenance,
/// donc ne gère pas l'équipe d'un vendeur. C'est volontaire — composer l'équipe
/// d'un commerçant n'est pas un acte de gouvernance, et les routes d'administration
/// existantes (suspension, clôture) suffisent à ce que la plateforme doit pouvoir
/// faire.
///
/// ET ELLE NE DIT PAS SI LE VENDEUR EXISTE.
///
/// Un compte sans appartenance reçoit le même refus, que le dossier visé existe ou
/// non. Les identifiants de vendeurs sont publics — ils circulent dans les liens
/// de boutique : distinguer les deux cas permettrait d'énumérer qui est vendeur.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <remarks>
/// Public, contrairement aux handlers du module : la composition root vit dans
/// l'assemblage Infrastructure et doit pouvoir l'enregistrer nommément. Les
/// handlers, eux, sont trouvés par réflexion et restent internes.
/// </remarks>
public sealed class MemberAccessResolver
{
    private readonly ISellerMemberRepository _members;
    private readonly ISellerRoleRepository _roles;

    public MemberAccessResolver(ISellerMemberRepository members, ISellerRoleRepository roles)
    {
        _members = members;
        _roles = roles;
    }

    /// <summary>L'acteur, ou un refus. Jamais un acteur « vide ».</summary>
    public async Task<Result<MemberActor>> ResolveAsync(
        Guid sellerId, Guid userId, CancellationToken cancellationToken = default)
    {
        var membre = await _members.GetMembershipAsync(sellerId, userId, cancellationToken);

        if (membre is null)
        {
            return Error.Forbidden(
                "sellers.member.not_a_member", "Vous ne faites pas partie de l'équipe de ce vendeur.");
        }

        // LE REFUS D'UN MEMBRE SUSPENDU EST DIT ICI, PAS DEVINÉ PLUS LOIN.
        //
        // `MemberActor.Ensure` refuserait de toute façon, mais avec le motif de la
        // permission manquante. Un membre suspendu doit lire « votre accès n'est
        // pas actif » : c'est la seule information qui lui permet de comprendre
        // qu'il doit s'adresser à son employeur et non réessayer.
        if (!membre.CanAct)
        {
            return Error.Forbidden(
                "sellers.member.not_active", "Votre accès à ce vendeur n'est pas actif.");
        }

        var roles = await ChargerAsync(membre.ReferencedRoleIds, cancellationToken);

        return MemberAccess.For(membre, roles);
    }

    /// <summary>Le membre ET son acteur, quand la commande doit muter le membre appelant.</summary>
    public async Task<IReadOnlyList<SellerRole>> ChargerAsync(
        IReadOnlySet<SellerRoleId> ids, CancellationToken cancellationToken = default)
        => await _roles.ListByIdsAsync([.. ids], cancellationToken);
}
