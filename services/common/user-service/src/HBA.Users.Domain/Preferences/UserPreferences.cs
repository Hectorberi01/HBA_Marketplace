using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Users.Domain.Preferences;

/// <summary>
/// Préférences d'un utilisateur : langue, devise et consentements (§10.2, table <c>
/// preferences</c>).
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

    /// <summary>Met à jour les champs fournis.</summary>
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
                // Refus explicite plutôt que silence.
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

/// <summary>
/// Accès aux préférences. Une seule ligne par utilisateur, d'où l'absence de liste.
/// </summary>
public interface IUserPreferencesRepository
{
    Task<UserPreferences?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task AddAsync(UserPreferences preferences, CancellationToken cancellationToken = default);
}
