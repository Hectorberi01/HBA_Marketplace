using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Users.Domain.Profiles;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE PROFIL D'UNE PERSONNE.
///
/// Le cahier d'architecture sépare deux questions : Identity répond à « qui peut
/// se connecter ? », User à « qui est la personne ? ». Le prénom et le nom
/// appartiennent à la seconde — ils ne participent à aucune décision d'accès.
///
/// L'IDENTIFIANT DU PROFIL EST LE UserId D'IDENTITY. PAS UN NOUVEAU GUID.
///
/// C'est la décision structurante de cet agrégat, et elle mérite d'être défendue.
///
/// Un identifiant propre obligerait à porter en plus un <c>UserId</c>, donc à
/// maintenir un index unique dessus, et surtout à répondre à « que faire s'il y
/// en a deux ? ». La réponse serait « c'est impossible » — autant le rendre
/// impossible PAR CONSTRUCTION : la clé primaire est le compte.
///
/// Cela rend aussi la lecture triviale depuis n'importe quel appelant qui tient
/// déjà un <c>UserId</c> — c'est-à-dire tous — sans jointure ni recherche.
///
/// CE MODULE NE VÉRIFIE PAS QUE LE COMPTE EXISTE.
///
/// Il ne connaît rien d'Identity, Contracts compris : c'est ce que vérifie
/// UsersBoundaryTests. Un profil rattaché à un compte inconnu est une donnée
/// orpheline sans danger ; le couplage qu'une vérification introduirait, lui,
/// coûterait la séparation qu'on vient de faire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
    /// ferait abandonner un acheteur sur un formulaire qu'il remplit debout dans
    /// un marché.
    /// </summary>
    public string? AvatarUrl { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? UpdatedOnUtc { get; private set; }

    /// <summary>
    /// Ce qu'on affiche et ce qu'on met dans un e-mail.
    ///
    /// Calculé, jamais stocké : un champ « nom complet » persisté diverge du jour
    /// où quelqu'un corrige son nom de famille sans que la concaténation soit
    /// refaite — et c'est le nom affiché au client qui devient faux.
    /// </summary>
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

        // Les deux champs sont affectés ENSEMBLE, après validation complète. Une
        // affectation au fil de l'eau laisserait un profil au prénom neuf et au
        // nom ancien si la seconde validation échouait.
        (FirstName, LastName) = names.Value;
        UpdatedOnUtc = DateTime.UtcNow;

        return Result.Success();
    }

    /// <summary>
    /// Change l'avatar. <c>null</c> le retire — c'est un droit, pas un oubli :
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
    ///
    /// Ils le sont déjà côté Identity : les rendre facultatifs ici produirait des
    /// profils vides pour des comptes qui, eux, portent un nom — et l'écart ne se
    /// verrait qu'au premier e-mail adressé à « Bonjour , ».
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

    /// <summary>
    /// Retire un profil. Appelé à la suppression du compte, et seulement là.
    ///
    /// C'est une vraie suppression, pas une anonymisation : rien ne référence un
    /// profil, contrairement au compte que les commandes désignent.
    /// </summary>
    void Remove(UserProfile profile);
}
