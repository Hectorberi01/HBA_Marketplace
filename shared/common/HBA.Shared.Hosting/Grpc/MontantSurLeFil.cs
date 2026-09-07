using System.Globalization;
using Grpc.Core;

namespace HBA.Shared.Hosting.Grpc;

/// <summary>L'argent qui traverse gRPC en TEXTE : écriture et lecture, au même endroit.</summary>
public static class MontantSurLeFil
{
    /// <summary>Un montant vers le fil.</summary>
    public static string Ecrire(decimal montant)
        => montant.ToString(CultureInfo.InvariantCulture);

    /// <summary>Un montant venu du fil, qui DOIT être présent.</summary>
    public static decimal Lire(string? valeur, string champ)
        => Analyser(valeur) ?? throw new RpcException(new Status(
            StatusCode.InvalidArgument,
            $"« {champ} » n'est pas un montant exploitable."));

    /// <summary>Un montant venu du fil qui peut légitimement être ABSENT.</summary>
    public static decimal? LireOuAbsent(string? valeur, string champ)
        => string.IsNullOrWhiteSpace(valeur)
            ? null
            : Analyser(valeur) ?? throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"« {champ} » n'est pas un montant exploitable."));

    private static decimal? Analyser(string? valeur)
        => decimal.TryParse(valeur, NumberStyles.Number, CultureInfo.InvariantCulture, out var montant)
            ? montant
            : null;
}
