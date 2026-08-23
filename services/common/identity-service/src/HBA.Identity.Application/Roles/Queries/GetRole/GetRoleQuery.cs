using HBA.Shared.Application.Messaging;
using HBA.Identity.Contracts;

namespace HBA.Identity.Application.Roles.Queries.GetRole;

/// <summary>Récupère un rôle et ses permissions.</summary>
public sealed record GetRoleQuery(Guid RoleId) : IQuery<RoleSummary>;
