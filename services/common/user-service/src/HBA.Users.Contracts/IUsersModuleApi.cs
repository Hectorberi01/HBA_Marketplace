namespace HBA.Users.Contracts;

/// <summary>Le profil, tel que les autres modules ont le droit de le voir.</summary>
/// <param name="UserId">Identifiant du compte, émis par Identity.</param>
/// <param name="DisplayName">
/// Prénom et nom assemblés. Fourni PRÊT À AFFICHER pour que chaque appelant
/// n'écrive pas sa propre concaténation — c'est ainsi qu'on se retrouve avec « Awa
/// Sagbo » ici et « Sagbo Awa » là.
/// </param>
public sealed record UserProfileSummary(
    Guid UserId,
    string FirstName,
    string LastName,
    string DisplayName,
    string? AvatarUrl);

/// <summary>API EN PROCESSUS DU MODULE USER.</summary>
public interface IUsersModuleApi
{
    Task<UserProfileSummary?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Plusieurs profils d'un coup.</summary>
    Task<IReadOnlyDictionary<Guid, UserProfileSummary>> GetProfilesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);
}
