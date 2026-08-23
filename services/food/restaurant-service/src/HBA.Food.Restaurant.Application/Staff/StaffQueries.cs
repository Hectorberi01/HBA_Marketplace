using HBA.Food.Contracts;
using HBA.Food.Domain.Staff;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Application.Staff;

/// <summary>
/// Le personnel d'un restaurant.
///
/// RÉSERVÉE À QUI PEUT GÉRER LE PERSONNEL, ET LE CONTRÔLE EST FAIT ICI.
///
/// Cette liste nomme des comptes, des rôles et des droits. Un cuisinier qui la
/// lirait saurait qui est caissier, qui est manager, et qui vient d'être
/// rétrogradé. Le cahier (§2) est explicite : « un KitchenStaff ne doit jamais
/// voir les données administratives du restaurant ».
/// </summary>
public sealed record ListStaffQuery(Guid RestaurantId, Guid ActorUserId)
    : IQuery<IReadOnlyList<StaffMemberSummary>>;

internal sealed class StaffQueryHandler : IQueryHandler<ListStaffQuery, IReadOnlyList<StaffMemberSummary>>
{
    private readonly IRestaurantStaffRepository _staff;

    public StaffQueryHandler(IRestaurantStaffRepository staff) => _staff = staff;

    public async Task<Result<IReadOnlyList<StaffMemberSummary>>> Handle(
        ListStaffQuery query, CancellationToken cancellationToken)
    {
        var acteur = await _staff.GetMembershipAsync(query.RestaurantId, query.ActorUserId, cancellationToken);

        if (acteur is null || !acteur.Has(FoodPermission.StaffManage))
        {
            // Même réponse dans les deux cas : dire « vous n'avez pas le droit »
            // à quelqu'un d'extérieur confirmerait que l'établissement existe.
            return Result.Failure<IReadOnlyList<StaffMemberSummary>>(Error.Forbidden(
                "food.staff.forbidden", "Vous n'êtes pas habilité à consulter le personnel."));
        }

        var membres = await _staff.ListByRestaurantAsync(query.RestaurantId, cancellationToken);

        IReadOnlyList<StaffMemberSummary> vues = membres
            // Les propriétaires d'abord, puis la cuisine : l'ordre du rôle est
            // déjà une hiérarchie, autant l'utiliser. Les partis en dernier.
            .OrderBy(m => m.IsActive ? 0 : 1)
            .ThenBy(m => m.Role)
            .ThenBy(m => m.CreatedOnUtc)
            .Select(Project)
            .ToList();

        return Result.Success(vues);
    }

    internal static StaffMemberSummary Project(RestaurantStaff membre)
        => new(
            membre.Id.Value,
            membre.UserId,
            membre.Role.ToString(),
            membre.IsActive,
            membre.IsFounder,
            membre.EffectivePermissions.Select(p => p.ToCode()).OrderBy(c => c, StringComparer.Ordinal).ToList(),
            membre.Overrides
                .Select(o => new StaffPermissionOverrideSummary(o.Permission.ToCode(), o.IsGranted))
                .OrderBy(o => o.Permission, StringComparer.Ordinal)
                .ToList(),
            membre.CreatedOnUtc);
}
