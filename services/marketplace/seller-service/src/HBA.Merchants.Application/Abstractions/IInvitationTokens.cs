namespace HBA.Merchants.Application.Abstractions;

/// <summary>LE JETON D'INVITATION — FABRIQUÉ ET HACHÉ AILLEURS QUE DANS LE DOMAINE.</summary>
public interface IInvitationTokens
{
    /// <summary>Un jeton neuf et son empreinte.</summary>
    /// <returns>
    /// <c> Token</c> part vers l'invité et n'est jamais persisté ; <c> Hash</c> est
    /// ce que la base retient.
    /// </returns>
    (string Token, string Hash) Create();

    /// <summary>L'empreinte d'un jeton présenté, pour retrouver son invitation.</summary>
    string Hash(string token);
}
