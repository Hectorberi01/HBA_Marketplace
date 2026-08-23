namespace HBA.Users.Contracts;

/// <summary>
/// Le profil, tel que les autres modules ont le droit de le voir.
/// </summary>
/// <param name="UserId">Identifiant du compte, émis par Identity.</param>
/// <param name="DisplayName">
/// Prénom et nom assemblés. Fourni PRÊT À AFFICHER pour que chaque appelant
/// n'écrive pas sa propre concaténation — c'est ainsi qu'on se retrouve avec
/// « Awa  Sagbo » ici et « Sagbo Awa » là.
/// </param>
public sealed record UserProfileSummary(
    Guid UserId,
    string FirstName,
    string LastName,
    string DisplayName,
    string? AvatarUrl);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// API EN PROCESSUS DU MODULE USER.
///
/// Elle existe parce que <c>UserSummary</c> d'Identity perd le prénom et le nom :
/// ils appartiennent au profil, pas au compte. Les appelants qui affichaient
/// <c>user.FirstName</c> passent désormais par ici.
///
/// LA LECTURE EN LOT N'EST PAS UN CONFORT.
///
/// Le chemin qui souffre le plus de cette séparation est celui des e-mails : il
/// lisait l'adresse et le prénom dans le même objet, et fait maintenant deux
/// appels. Pour un message, c'est indolore. Pour une liste de commandes affichant
/// le nom de chaque acheteur, un appel par ligne transforme une page en N+1.
///
/// <see cref="GetProfilesAsync"/> existe pour que ce cas ait une réponse dès le
/// premier jour, plutôt qu'après l'avoir constaté en production.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IUsersModuleApi
{
    Task<UserProfileSummary?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Plusieurs profils d'un coup. Les identifiants inconnus sont simplement
    /// absents du résultat — un profil manquant n'est pas une erreur, c'est un
    /// compte dont personne n'a encore rempli le nom.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, UserProfileSummary>> GetProfilesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);
}
