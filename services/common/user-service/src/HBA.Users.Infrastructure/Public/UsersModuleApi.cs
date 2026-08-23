using HBA.Users.Contracts;
using HBA.Users.Domain.Profiles;

namespace HBA.Users.Infrastructure.Public;

/// <summary>
/// Implémentation en processus de l'API publique du module User.
///
/// LECTURE SEULE, comme celle de Deliveries. Créer ou renommer un profil passe
/// par une commande MediatR : un autre module ne doit pas pouvoir modifier
/// l'identité affichée d'une personne par un simple appel de méthode.
/// </summary>
internal sealed class UsersModuleApi : IUsersModuleApi
{
    private readonly IUserProfileRepository _profiles;

    public UsersModuleApi(IUserProfileRepository profiles) => _profiles = profiles;

    public async Task<UserProfileSummary?> GetProfileAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetByUserIdAsync(userId, cancellationToken);

        return profile is null ? null : ToSummary(profile);
    }

    public async Task<IReadOnlyDictionary<Guid, UserProfileSummary>> GetProfilesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var profiles = await _profiles.ListByUserIdsAsync(userIds, cancellationToken);

        // Un DICTIONNAIRE et non une liste : l'appelant tient déjà ses identifiants
        // et veut retrouver chacun sans reparcourir. Rendre une liste l'obligerait à
        // construire ce dictionnaire lui-même, à chaque appel, dans chaque module.
        return profiles.ToDictionary(p => p.Id, ToSummary);
    }

    private static UserProfileSummary ToSummary(UserProfile profile)
        => new(profile.Id, profile.FirstName, profile.LastName, profile.DisplayName, profile.AvatarUrl);
}
