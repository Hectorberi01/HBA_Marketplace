using HBA.Identity.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Communication.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// Résout le compte administrateur à prévenir, à partir de l'adresse d'exploitation.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE TYPE VIVAIT DANS `DisputeNotificationHandlers.cs`, ET C'ÉTAIT PIÉGEUX.
///
/// En retirant ce fichier — ses deux gestionnaires écoutent des événements que le
/// module Disputes, non extrait, ne publie pas encore — j'ai emporté ce type avec
/// lui. Or il sert aussi à `SellerRegisteredAdminNotificationHandler`, dans un
/// tout autre fichier. La compilation a échoué sur « le type ou le nom d'espace
/// 'AdminNotificationTarget' est introuvable », sans rien qui relie l'erreur à la
/// suppression.
///
/// Il est désormais dans son propre fichier : sa durée de vie ne dépend plus de
/// celle des litiges.
///
/// LA CLÉ DE CONFIGURATION A CHANGÉ : `Admin:Email`, ET NON
///    `Identity:Bootstrap:AdminEmail`.
///
/// C'est celle qu'`identity-service` utilise depuis l'amorçage du premier
/// administrateur. Garder l'ancienne aurait obligé à renseigner deux variables
/// pour la même adresse — et le jour où elles auraient divergé, les notifications
/// d'exploitation seraient parties vers un compte inexistant, en silence.
///
/// IL N'EXISTE PAS DE « LISTE DES ADMINISTRATEURS ».
///
/// Personne ne l'expose entre services. S'appuyer sur cette adresse évite
/// d'ouvrir l'annuaire d'Identity pour un seul besoin. Le jour où plusieurs
/// administrateurs devront être prévenus, c'est identity-service qui devra
/// exposer la liste — pas ce fichier qui devra deviner.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
    /// <remarks>
    /// NE LÈVE JAMAIS.
    ///
    /// Ces notifications sont un effet de bord d'un fait déjà acquis : une
    /// boutique EST créée, un litige EST ouvert. Faire échouer le traitement de
    /// l'événement parce que personne n'a configuré d'adresse d'exploitation
    /// ferait rejouer l'événement indéfiniment par l'outbox, sans jamais aboutir.
    /// On trace, et on n'envoie rien.
    /// </remarks>
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
