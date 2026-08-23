using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Identity.Contracts;
using HBA.Identity.Domain.Roles;

namespace HBA.Identity.Application.Roles.Queries.ListRoles;

internal sealed class ListRolesQueryHandler : IQueryHandler<ListRolesQuery, IReadOnlyList<RoleSummary>>
{
    private readonly IRoleRepository _roleRepository;

    public ListRolesQueryHandler(IRoleRepository roleRepository)
        => _roleRepository = roleRepository;

    public async Task<Result<IReadOnlyList<RoleSummary>>> Handle(ListRolesQuery query, CancellationToken cancellationToken)
    {
        var roles = await _roleRepository.ListAsync(cancellationToken);

        IReadOnlyList<RoleSummary> summaries = roles
            .Select(r => new RoleSummary(r.Id.Value, r.Name, r.Description, r.IsSystem, r.Permissions.ToList()))
            .ToList();

        return Result.Success(summaries);
    }
}
