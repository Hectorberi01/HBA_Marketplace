using OtpNet;
using HBA.Identity.Application.Abstractions;

namespace HBA.Identity.Infrastructure.Security;

/// <summary>Double authentification TOTP (RFC 6238) via Otp.NET.</summary>
internal sealed class TotpService : ITotpService
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE CHAÎNE S'AFFICHE DANS L'APPLICATION D'AUTHENTIFICATION DE L'UTILISATEUR.
    ///
    /// Le monolithe l'a laissée à « Marketplace ». Le renommage n'est ni cosmétique
    /// ni anodin, et il faut savoir dans quel sens :
    ///
    ///   • Il n'invalide AUCUN enrôlement existant. Le code TOTP se calcule à
    ///     partir du secret et de l'heure ; l'émetteur n'est qu'une étiquette de
    ///     l'URI `otpauth://`, lue une seule fois au moment du scan.
    ///   • Mais les personnes déjà enrôlées continueront de voir « Marketplace »
    ///     dans leur application, tandis que les nouvelles verront « HBA ». Les
    ///     deux entrées cohabiteront chez qui possède deux comptes.
    ///
    /// L'incohérence est visible et sans danger ; garder le nom d'un produit qui
    /// n'existe plus le serait tout autant, en plus d'être faux. À arbitrer par
    /// le support avant l'ouverture au public : c'est lui qui recevra les appels.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private const string Issuer = "HBA";

    public string GenerateSecret()
    {
        var key = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(key);
    }

    public string BuildOtpAuthUri(string secret, string accountName)
    {
        var label = Uri.EscapeDataString($"{Issuer}:{accountName}");
        var issuer = Uri.EscapeDataString(Issuer);
        return $"otpauth://totp/{label}?secret={secret}&issuer={issuer}&algorithm=SHA1&digits=6&period=30";
    }

    public bool VerifyCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var totp = new Totp(Base32Encoding.ToBytes(secret));
        return totp.VerifyTotp(code.Trim(), out _, VerificationWindow.RfcSpecifiedNetworkDelay);
    }
}
