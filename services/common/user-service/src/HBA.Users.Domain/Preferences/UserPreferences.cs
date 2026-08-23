using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Users.Domain.Preferences;

/// <summary>
/// Préférences d'un utilisateur : langue, devise et consentements (§10.2, table
/// <c>preferences</c>).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LA CLÉ PRIMAIRE EST LE UserId, COMME POUR LE PROFIL.
///
/// Un utilisateur a exactement un jeu de préférences. Une clé technique distincte
/// aurait rendu représentable l'état « deux jeux pour un compte » — et il aurait
/// suffi d'un double appel concurrent à la première connexion pour l'atteindre.
/// Avec le UserId en clé, la base refuse le second.
///
/// `MarketingOptIn` EST FAUX PAR DÉFAUT, ET CE N'EST PAS UN DÉTAIL TECHNIQUE.
///
/// Un consentement marketing doit être donné, jamais supposé. Créer les
/// préférences avec `true` transformerait chaque inscription en consentement
/// implicite. `PushEnabled` suit la logique inverse et vaut `true` : il gouverne
/// les notifications TRANSACTIONNELLES — « votre commande est acceptée » — que
/// l'utilisateur attend, et qui ne relèvent pas du consentement commercial.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class UserPreferences : AggregateRoot<Guid>
{
    /// <summary>Devise par défaut : le franc CFA, seule devise servie aujourd'hui.</summary>
    public const string DefaultCurrency = "XOF";

    /// <summary>Locale par défaut, cohérente avec `HbaRequestContext`.</summary>
    public const string DefaultLanguage = "fr-BJ";

    private static readonly string[] SupportedLanguages = ["fr-BJ", "fr-FR", "en-US"];
    private static readonly string[] SupportedCurrencies = ["XOF"];

    private UserPreferences(Guid userId, string language, string currency)
        : base(userId)
    {
        Language = language;
        Currency = currency;
        PushEnabled = true;
        MarketingOptIn = false;
        CreatedOnUtc = DateTime.UtcNow;
    }

    private UserPreferences()
    {
        Language = DefaultLanguage;
        Currency = DefaultCurrency;
    }

    public string Language { get; private set; }

    public string Currency { get; private set; }

    public bool PushEnabled { get; private set; }

    public bool MarketingOptIn { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? UpdatedOnUtc { get; private set; }

    /// <summary>Préférences par défaut, créées à la première consultation.</summary>
    public static Result<UserPreferences> CreateDefault(Guid userId)
        => userId == Guid.Empty
            ? Result.Failure<UserPreferences>(Error.Validation(
                "users.preferences.user_required", "Des préférences doivent être rattachées à un compte."))
            : new UserPreferences(userId, DefaultLanguage, DefaultCurrency);

    /// <summary>
    /// Met à jour les champs fournis. Un paramètre null signifie « inchangé » :
    /// un client qui n'envoie que la devise ne doit pas réinitialiser la langue.
    /// </summary>
    public Result Update(string? language, string? currency, bool? pushEnabled, bool? marketingOptIn)
    {
        if (language is not null)
        {
            var normalized = language.Trim();

            if (!SupportedLanguages.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                return Result.Failure(Error.Validation(
                    "users.preferences.language_unsupported",
                    $"Langue non prise en charge : « {normalized} »."));
            }

            Language = normalized;
        }

        if (currency is not null)
        {
            var normalized = currency.Trim().ToUpperInvariant();

            if (!SupportedCurrencies.Contains(normalized))
            {
                // Refus explicite plutôt que silence. Accepter une devise inconnue
                // ici la ferait ressortir au calcul d'un panier, à un endroit où plus
                // rien ne rattache l'anomalie au moment où elle a été introduite.
                return Result.Failure(Error.Validation(
                    "users.preferences.currency_unsupported",
                    $"Devise non prise en charge : « {normalized} »."));
            }

            Currency = normalized;
        }

        if (pushEnabled is not null)
        {
            PushEnabled = pushEnabled.Value;
        }

        if (marketingOptIn is not null)
        {
            MarketingOptIn = marketingOptIn.Value;
        }

        UpdatedOnUtc = DateTime.UtcNow;
        return Result.Success();
    }
}

/// <summary>Accès aux préférences. Une seule ligne par utilisateur, d'où l'absence de liste.</summary>
public interface IUserPreferencesRepository
{
    Task<UserPreferences?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task AddAsync(UserPreferences preferences, CancellationToken cancellationToken = default);
}
