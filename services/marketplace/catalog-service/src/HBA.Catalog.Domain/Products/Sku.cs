using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Products;

/// <summary>
/// SKU : référence unique d'une variante, contrat partagé avec Inventory et Pricing
/// (cf.
/// </summary>
public sealed partial class Sku : ValueObject
{
    private Sku(string value) => Value = value;

    public string Value { get; }

    public static Result<Sku> Create(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Error.Validation("catalog.sku.empty", "Le SKU ne peut pas être vide.");
        }

        var normalized = input.Trim().ToUpperInvariant();

        if (normalized.Length > 64)
        {
            return Error.Validation("catalog.sku.length", "Le SKU doit faire au plus 64 caractères.");
        }

        if (!SkuPattern().IsMatch(normalized))
        {
            return Error.Validation("catalog.sku.format", "Le SKU n'accepte que lettres, chiffres, tirets et underscores.");
        }

        return new Sku(normalized);
    }

    /// <summary>
    /// Génère un SKU automatiquement quand le vendeur n'en saisit pas : préfixe = 6
    /// premiers caractères de l'ID vendeur (repère d'origine, lisible dans les
    /// exports), suivi d'un code aléatoire cryptographique.
    /// </summary>
    public static Sku Generate(Guid sellerId)
    {
        var prefix = sellerId.ToString("N")[..6].ToUpperInvariant();
        return new Sku($"{prefix}-{RandomCode(8)}");
    }

    private static string RandomCode(int length)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var buffer = new char[length];
        for (var i = 0; i < length; i++)
        {
            buffer[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(buffer);
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Z0-9_-]+$")]
    private static partial Regex SkuPattern();
}
