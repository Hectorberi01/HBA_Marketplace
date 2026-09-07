using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Users.Domain.Profiles;

/// <summary>LE PROFIL D'UNE PERSONNE.</summary>
public sealed class UserProfile : AggregateRoot<Guid>
{
    public const int MaxName = 100;
    public const int MaxAvatarUrl = 500;

    private UserProfile(Guid userId, string firstName, string lastName): base(userId)
    {
        FirstName = firstName;
        LastName = lastName;
        CreatedOnUtc = DateTime.UtcNow;
    }

    // Requis par EF Core.
    private UserProfile()
    {
        FirstName = string.Empty;
        LastName = string.Empty;
    }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    /// <summary>
    /// Avatar. Facultatif — et il le restera : exiger une photo à l'inscription
    /// ferait abandonner un acheteur sur un formulaire qu'il remplit debout dans un
    /// marché.
    /// </summary>
    public string? AvatarUrl { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? UpdatedOnUtc { get; private set; }

    /// <summary>Ce qu'on affiche et ce qu'on met dans un e-mail.</summary>
    public string DisplayName => $"{FirstName} {LastName}".Trim();

    public static Result<UserProfile> Create(Guid userId, string? firstName, string? lastName)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<UserProfile>(
                Error.Validation("users.profile.user_required", "Un profil doit être rattaché à un compte."));
        }

        var names = ValidateNames(firstName, lastName);
        if (names.IsFailure)
        {
            return Result.Failure<UserProfile>(names.Error);
        }

        var (prenom, nom) = names.Value;

        return new UserProfile(userId, prenom, nom);
    }

    public Result Rename(string? firstName, string? lastName)
    {
        var names = ValidateNames(firstName, lastName);
        if (names.IsFailure)
        {
            return names;
        }

        // Les deux champs sont affectés ENSEMBLE, après validation complète.
        (FirstName, LastName) = names.Value;
        UpdatedOnUtc = DateTime.UtcNow;

        return Result.Success();
    }

    /// <summary>
    /// Change l'avatar. <c> null</c> le retire — c'est un droit, pas un oubli :
    /// quelqu'un qui veut effacer sa photo doit pouvoir le faire.
    /// </summary>
    public Result SetAvatar(string? avatarUrl)
    {
        var trimmed = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl.Trim();

        if (trimmed is not null && trimmed.Length > MaxAvatarUrl)
        {
            return Result.Failure(Error.Validation(
                "users.profile.avatar_too_long", "La référence de l'avatar est trop longue."));
        }

        AvatarUrl = trimmed;
        UpdatedOnUtc = DateTime.UtcNow;

        return Result.Success();
    }

    /// <summary>
    /// Prénom et nom sont tous deux OBLIGATOIRES, et tronqués plutôt que refusés
    /// s'ils dépassent.
    /// </summary>
    private static Result<(string FirstName, string LastName)> ValidateNames(string? firstName, string? lastName)
    {
        var prenom = Trim(firstName);
        if (prenom is null)
        {
            return Result.Failure<(string, string)>(
                Error.Validation("users.profile.first_name_required", "Le prénom est obligatoire."));
        }

        var nom = Trim(lastName);
        if (nom is null)
        {
            return Result.Failure<(string, string)>(
                Error.Validation("users.profile.last_name_required", "Le nom est obligatoire."));
        }

        return (Cap(prenom), Cap(nom));
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Cap(string value) => value.Length <= MaxName ? value : value[..MaxName];
}

/// <summary>Accès aux profils. L'identifiant est celui du compte.</summary>
public interface IUserProfileRepository
{
    Task<UserProfile?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserProfile>> ListByUserIdsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);

    Task AddAsync(UserProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Retire un profil. Appelé à la suppression du compte, et seulement là.</summary>
    void Remove(UserProfile profile);
}
