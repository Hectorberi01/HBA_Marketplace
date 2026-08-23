using HBA.Users.Domain.Profiles;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Users.Application.Profiles;

/// <summary>
/// Le profil tel qu'un client le voit.
///
/// <c>DisplayName</c> est SERVI, pas laissé à calculer : sans lui, l'application
/// mobile, le site et la console d'administration écriraient chacun leur
/// concaténation — et l'une d'elles mettrait le nom avant le prénom.
/// </summary>
public sealed record UserProfileDto(
    Guid UserId,
    string FirstName,
    string LastName,
    string DisplayName,
    string? AvatarUrl,
    DateTime CreatedOnUtc,
    DateTime? UpdatedOnUtc);

/// <summary>Lit le profil d'un compte.</summary>
public sealed record GetUserProfileQuery(Guid UserId) : IQuery<UserProfileDto>;

internal sealed class GetUserProfileQueryHandler : IQueryHandler<GetUserProfileQuery, UserProfileDto>
{
    private readonly IUserProfileRepository _profiles;

    public GetUserProfileQueryHandler(IUserProfileRepository profiles) => _profiles = profiles;

    public async Task<Result<UserProfileDto>> Handle(GetUserProfileQuery query, CancellationToken ct)
    {
        var profile = await _profiles.GetByUserIdAsync(query.UserId, ct);

        if (profile is null)
        {
            // ON NE FABRIQUE PAS UN PROFIL VIDE.
            //
            // Renvoyer un DTO aux champs nuls serait plus commode pour l'appelant,
            // et cacherait exactement ce qu'on veut voir : un compte sans profil est
            // une anomalie de reprise, pas un profil « pas encore rempli ». La
            // migration les a tous créés ; s'il en manque un, il faut le savoir.
            return Result.Failure<UserProfileDto>(
                Error.NotFound("users.profile.not_found", "Profil introuvable."));
        }

        return new UserProfileDto(
            profile.Id,
            profile.FirstName,
            profile.LastName,
            profile.DisplayName,
            profile.AvatarUrl,
            profile.CreatedOnUtc,
            profile.UpdatedOnUtc);
    }
}
