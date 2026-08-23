using HBA.Shared.Application.Messaging;
using HBA.Identity.Contracts;

namespace HBA.Identity.Application.Roles.Queries.ListRoles;

/// <summary>Liste tous les rôles (back-office d'administration).</summary>
public sealed record ListRolesQuery : IQuery<IReadOnlyList<RoleSummary>>;
