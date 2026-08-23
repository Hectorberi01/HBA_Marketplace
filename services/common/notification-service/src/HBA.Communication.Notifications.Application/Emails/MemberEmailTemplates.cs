using System.Net;
using HBA.Communication.Notifications.Application.Abstractions;

namespace HBA.Communication.Notifications.Application.Emails;

/// <summary>
/// Gabarit de l'invitation à rejoindre l'équipe d'un vendeur.
///
/// Même sobriété que <see cref="AccountEmailTemplates"/> : pas d'image, pas de CSS
/// externe, pas de webfont. Un e-mail chargé passe plus souvent en spam — et une
/// invitation en spam, c'est un employé qui n'entre jamais et un commerçant qui
/// croit la plateforme cassée.
/// </summary>
public static class MemberEmailTemplates
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE NOM DE LA BOUTIQUE EST DANS LE SUJET, ET CE N'EST PAS COSMÉTIQUE.
    ///
    /// C'est le seul e-mail de la plateforme qui demande à quelqu'un d'ouvrir un
    /// lien vers un compte qu'il n'a pas encore. Un message qui dirait « vous avez
    /// été invité » sans dire PAR QUI est indiscernable d'un hameçonnage — et la
    /// bonne réaction du destinataire serait alors de ne pas cliquer.
    ///
    /// ET IL DIT CE QU'IL FAUT FAIRE SI L'INVITATION EST INATTENDUE.
    ///
    /// « Ignorez cet e-mail » n'est pas une politesse : une invitation non
    /// acceptée expire d'elle-même, et le dire évite qu'un destinataire surpris
    /// clique « pour voir » — ce qui est exactement ce qu'un hameçonnage cherche à
    /// obtenir.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static EmailMessage SellerInvitation(
        string to, string? displayName, string shopName, string invitationUrl, DateTime expiresOnUtc)
    {
        var salutation = string.IsNullOrWhiteSpace(displayName) ? "Bonjour" : $"Bonjour {displayName.Trim()}";
        var jours = Math.Max(1, (int)Math.Ceiling((expiresOnUtc - DateTime.UtcNow).TotalDays));

        var nom = WebUtility.HtmlEncode(salutation);
        var boutique = WebUtility.HtmlEncode(shopName);
        var url = WebUtility.HtmlEncode(invitationUrl);

        var html = $"""
            <div style="font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;font-size:16px;color:#111;line-height:1.6">
              <p>{nom},</p>
              <p><strong>{boutique}</strong> vous invite à rejoindre son équipe sur HBA Express.</p>
              <p style="margin:28px 0">
                <a href="{url}" style="background:#111;color:#fff;padding:12px 22px;border-radius:6px;text-decoration:none;display:inline-block">
                  Rejoindre l'équipe
                </a>
              </p>
              <p style="color:#555;font-size:14px">Ou copiez ce lien dans votre navigateur :<br>{url}</p>
              <p style="color:#555;font-size:14px">
                Cette invitation expire dans {jours} jour(s) et ne peut être utilisée qu'une fois.
                Elle doit être acceptée avec le compte HBA Express associé à cette adresse e-mail.
              </p>
              <p style="color:#555;font-size:14px">
                Si vous ne connaissez pas {boutique}, ignorez cet e-mail : l'invitation expirera d'elle-même.
              </p>
            </div>
            """;

        var text = $"""
            {salutation},

            {shopName} vous invite à rejoindre son équipe sur HBA Express.

            {invitationUrl}

            Cette invitation expire dans {jours} jour(s) et ne peut être utilisée qu'une fois.
            Elle doit être acceptée avec le compte HBA Express associé à cette adresse e-mail.

            Si vous ne connaissez pas {shopName}, ignorez cet e-mail : l'invitation
            expirera d'elle-même.
            """;

        return new EmailMessage(to, $"{shopName} vous invite à rejoindre son équipe — HBA Express", html, text);
    }
}
