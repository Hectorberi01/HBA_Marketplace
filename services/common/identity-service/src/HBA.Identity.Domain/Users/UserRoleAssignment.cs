using HBA.Shared.Domain.Primitives;

namespace HBA.Identity.Domain.Users;

/// <summary>
/// Rattachement d'un rôle à un utilisateur (un user peut cumuler Buyer + Seller).
/// Référence le rôle par son identifiant — pas d'embarquement de l'agrégat Role.
/// Entité enfant de l'agrégat User.
/// </summary>
public sealed class UserRoleAssignment : Entity<Guid>
{
    private UserRoleAssignment()
    {
    }

    internal UserRoleAssignment(Guid id, Guid roleId)
        : base(id)
    {
        RoleId = roleId;
    }

    public Guid RoleId { get; private set; }
}
