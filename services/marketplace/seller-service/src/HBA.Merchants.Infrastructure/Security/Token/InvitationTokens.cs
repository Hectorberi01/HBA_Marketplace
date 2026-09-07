using System.Security.Cryptography;
using System.Text;
using HBA.Merchants.Application.Abstractions;

namespace HBA.Merchants.Infrastructure.Security;

/// <summary>LE JETON D'INVITATION — TRENTE-DEUX OCTETS D'ALÉA, ET SON EMPREINTE SHA-256.</summary>
internal sealed class InvitationTokens : IInvitationTokens
{
    private const int OctetsDAlea = 32;

    public (string Token, string Hash) Create()
    {
        var octets = RandomNumberGenerator.GetBytes(OctetsDAlea);
        var token = Base64UrlTextEncoder(octets);

        return (token, Hash(token));
    }

    public string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string Base64UrlTextEncoder(byte[] octets)
        => Convert.ToBase64String(octets)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
