using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Users.Domain.Devices;

/// <summary>Plateformes d'appareil reconnues (§10.2, table <c>devices</c>).</summary>
public static class DevicePlatforms
{
    public const string Ios = "IOS";
    public const string Android = "ANDROID";
    public const string Web = "WEB";

    public static readonly string[] All = [Ios, Android, Web];
}

/// <summary>
/// Appareil enregistré pour les notifications push.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// L'IDENTITÉ D'UN APPAREIL EST LE COUPLE (utilisateur, jeton), PAS LE JETON SEUL.
///
/// Un jeton push est réattribué par le fournisseur : le même jeton FCM peut, après
/// réinstallation, désigner un autre compte sur le même téléphone. Indexer sur le
/// jeton seul ferait donc partir les notifications de l'un chez l'autre.
///
/// À l'inverse, un même utilisateur a légitimement plusieurs appareils. C'est
/// pourquoi il n'y a ni contrainte d'unicité sur `UserId`, ni sur `PushToken`,
/// mais sur le couple : réenregistrer le même appareil MET À JOUR la ligne au lieu
/// d'en créer une seconde, sinon chaque ouverture de l'application ajouterait un
/// destinataire et l'utilisateur recevrait ses notifications en double, en triple…
///
/// `LastSeenAt` N'EST PAS DÉCORATIF.
///
/// Les fournisseurs refusent les jetons périmés, et un jeton mort ne se signale
/// pas : il échoue silencieusement. Cette date est la seule base d'une purge, et
/// sans purge la table grossit d'une ligne par réinstallation, indéfiniment.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class UserDevice : AggregateRoot<Guid>
{
    public const int MaxPushToken = 512;

    private UserDevice(Guid id, Guid userId, string platform, string pushToken)
        : base(id)
    {
        UserId = userId;
        Platform = platform;
        PushToken = pushToken;
        RegisteredOnUtc = DateTime.UtcNow;
        LastSeenAtUtc = DateTime.UtcNow;
    }

    private UserDevice()
    {
        Platform = string.Empty;
        PushToken = string.Empty;
    }

    public Guid UserId { get; private set; }

    public string Platform { get; private set; }

    public string PushToken { get; private set; }

    public DateTime RegisteredOnUtc { get; private set; }

    public DateTime LastSeenAtUtc { get; private set; }

    public static Result<UserDevice> Register(Guid userId, string? platform, string? pushToken)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<UserDevice>(Error.Validation(
                "users.device.user_required", "Un appareil doit être rattaché à un compte."));
        }

        var normalizedPlatform = (platform ?? string.Empty).Trim().ToUpperInvariant();

        if (!DevicePlatforms.All.Contains(normalizedPlatform))
        {
            return Result.Failure<UserDevice>(Error.Validation(
                "users.device.platform_unsupported",
                $"Plateforme non prise en charge : « {platform} »."));
        }

        var token = (pushToken ?? string.Empty).Trim();

        if (token.Length == 0)
        {
            return Result.Failure<UserDevice>(Error.Validation(
                "users.device.token_required", "Le jeton de notification est obligatoire."));
        }

        if (token.Length > MaxPushToken)
        {
            return Result.Failure<UserDevice>(Error.Validation(
                "users.device.token_too_long", "Le jeton de notification est trop long."));
        }

        return new UserDevice(Guid.NewGuid(), userId, normalizedPlatform, token);
    }

    /// <summary>Réenregistrement du même appareil : on rafraîchit, on ne duplique pas.</summary>
    public void Touch(string platform)
    {
        Platform = platform.Trim().ToUpperInvariant();
        LastSeenAtUtc = DateTime.UtcNow;
    }
}

/// <summary>Accès aux appareils d'un utilisateur.</summary>
public interface IUserDeviceRepository
{
    Task AddAsync(UserDevice device, CancellationToken cancellationToken = default);

    /// <summary>Retrouve un appareil par le couple identifiant — voir l'encadré de <see cref="UserDevice"/>.</summary>
    Task<UserDevice?> FindAsync(Guid userId, string pushToken, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserDevice>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
