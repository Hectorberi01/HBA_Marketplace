namespace HBA.Identity.Application.Abstractions;

/// <summary>
/// Durées de vie des jetons, fournies par la configuration (implémenté en
/// Infrastructure).
/// </summary>
public interface IAuthTokenSettings
{
    TimeSpan RefreshTokenLifetime { get; }

    TimeSpan EmailVerificationLifetime { get; }
}
