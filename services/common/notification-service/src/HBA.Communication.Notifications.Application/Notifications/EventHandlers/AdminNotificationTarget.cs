using HBA.Identity.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Communication.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// Résout le compte administrateur à prévenir, à partir de l'adresse
/// d'exploitation.
/// </summary>
public sealed class AdminNotificationTarget
{
    /// <summary>Même section que l'amorçage d'identity-service (`ADMIN__EMAIL`).</summary>
    private const string AdminEmailKey = "Admin:Email";

    private readonly IIdentityModuleApi _identity;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminNotificationTarget> _logger;

    public AdminNotificationTarget(
        IIdentityModuleApi identity,
        IConfiguration configuration,
        ILogger<AdminNotificationTarget> logger)
    {
        _identity = identity;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Identifiant du compte admin à notifier, ou `null` s'il est introuvable.</summary>
    public async Task<Guid?> ResolveAsync(CancellationToken cancellationToken)
    {
        var email = _configuration[AdminEmailKey];

        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning(
                "Notification d'exploitation ignorée : « {Key} » n'est pas renseigné.", AdminEmailKey);
            return null;
        }

        var user = await _identity.GetUserByEmailAsync(email, cancellationToken);

        if (user is null)
        {
            _logger.LogWarning(
                "Notification d'exploitation ignorée : aucun compte ne porte l'adresse « {Email} ».", email);
            return null;
        }

        return user.Id;
    }
}
