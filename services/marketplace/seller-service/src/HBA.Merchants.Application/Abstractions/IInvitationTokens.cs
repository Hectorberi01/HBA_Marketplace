namespace HBA.Merchants.Application.Abstractions;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE JETON D'INVITATION — FABRIQUÉ ET HACHÉ AILLEURS QUE DANS LE DOMAINE.
///
/// NI ALÉA NI CRYPTOGRAPHIE DANS UN AGRÉGAT.
///
/// Un agrégat qui tire un nombre aléatoire n'est plus testable : deux exécutions
/// du même scénario donnent deux résultats. Le domaine reçoit donc une empreinte
/// déjà calculée et ne sait rien de la façon dont elle a été obtenue — c'est
/// aussi ce qui permettra d'en changer l'algorithme sans toucher aux règles.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IInvitationTokens
{
    /// <summary>
    /// Un jeton neuf et son empreinte.
    /// </summary>
    /// <returns>
    /// <c>Token</c> part vers l'invité et n'est jamais persisté ; <c>Hash</c> est
    /// ce que la base retient.
    /// </returns>
    (string Token, string Hash) Create();

    /// <summary>L'empreinte d'un jeton présenté, pour retrouver son invitation.</summary>
    string Hash(string token);
}
