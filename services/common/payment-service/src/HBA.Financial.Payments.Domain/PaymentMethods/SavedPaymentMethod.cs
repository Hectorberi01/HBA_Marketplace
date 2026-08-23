using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Financial.Payments.Domain.PaymentMethods;

/// <summary>Famille du moyen de paiement enregistré.</summary>
/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QU'ON PEUT ENREGISTRER — À NE PAS CONFONDRE AVEC <c>PaymentMethod</c>.
///
/// Le module Payments porte DÉJÀ une énumération <c>PaymentMethod</c> : MobileMoney,
/// Card, BankTransfer, CashOnDelivery. Elle dit COMMENT un paiement a été fait.
///
/// Celle-ci dit ce qu'un acheteur peut ENREGISTRER pour réutiliser plus tard. Les
/// deux se ressemblent, et la tentation de les fusionner viendra — c'est d'ailleurs
/// la collision de noms entre cette classe et cette énumération qui a fait échouer
/// la compilation lors du déplacement depuis Identity.
///
/// NE PAS LES FUSIONNER. Un paiement à la livraison est un mode de règlement
/// parfaitement valide ; ce n'est pas un instrument qu'on enregistre dans un
/// carnet. Fusionner autoriserait la création d'un « moyen de paiement enregistré
/// de type CashOnDelivery » — un objet sans référence de compte, que rien ne
/// pourrait débiter, et que l'interface afficherait comme une carte.
///
/// Deux ensembles proches ne sont pas le même ensemble.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum PaymentMethodType
{
    /// <summary>Compte Mobile Money (MTN MoMo, Moov…).</summary>
    MobileMoney = 0,

    /// <summary>Carte bancaire (stockée masquée : marque + 4 derniers chiffres).</summary>
    Card = 1
}

/// <summary>
/// Moyen de paiement enregistré par un client. Agrégat racine simple, rattaché à
/// un compte par <see cref="UserId"/> (référence inter-agrégat, pas de FK).
///
/// Sécurité : on ne persiste JAMAIS de donnée sensible complète. Pour une carte,
/// seuls la marque, les 4 derniers chiffres et l'expiration sont conservés (aucun
/// PAN complet ni CVV). Pour Mobile Money, le numéro (MSISDN) sert à initier le
/// paiement via la passerelle.
/// </summary>
public sealed class SavedPaymentMethod : AggregateRoot<SavedPaymentMethodId>
{
    private SavedPaymentMethod()
    {
    }

    private SavedPaymentMethod(
        SavedPaymentMethodId id,
        Guid userId,
        PaymentMethodType type,
        string label,
        string provider,
        string accountRef,
        int? expiryMonth,
        int? expiryYear,
        string? holderName,
        bool isDefault)
        : base(id)
    {
        UserId = userId;
        Type = type;
        Label = label;
        Provider = provider;
        AccountRef = accountRef;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        HolderName = holderName;
        IsDefault = isDefault;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public Guid UserId { get; private set; }
    public PaymentMethodType Type { get; private set; }
    public string Label { get; private set; } = default!;

    /// <summary>Opérateur Mobile Money (MTN/Moov) ou marque de carte (Visa/Mastercard…).</summary>
    public string Provider { get; private set; } = default!;

    /// <summary>MSISDN pour Mobile Money ; 4 derniers chiffres pour une carte.</summary>
    public string AccountRef { get; private set; } = default!;

    public int? ExpiryMonth { get; private set; }
    public int? ExpiryYear { get; private set; }
    public string? HolderName { get; private set; }
    public bool IsDefault { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    /// <summary>Crée un moyen Mobile Money (opérateur + numéro).</summary>
    public static Result<SavedPaymentMethod> CreateMobileMoney(
        Guid userId, string? label, string provider, string msisdn, bool isDefault)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<SavedPaymentMethod>(Error.Validation("payments.payment_method.user_required", "Utilisateur requis."));
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            return Result.Failure<SavedPaymentMethod>(Error.Validation("payments.payment_method.provider_required", "Opérateur requis."));
        }

        var digits = new string((msisdn ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < 8)
        {
            return Result.Failure<SavedPaymentMethod>(Error.Validation("payments.payment_method.msisdn_invalid", "Numéro Mobile Money invalide."));
        }

        return Result.Success(new SavedPaymentMethod(
            SavedPaymentMethodId.New(),
            userId,
            PaymentMethodType.MobileMoney,
            string.IsNullOrWhiteSpace(label) ? provider.Trim() : label!.Trim(),
            provider.Trim(),
            digits,
            expiryMonth: null,
            expiryYear: null,
            holderName: null,
            isDefault));
    }

    /// <summary>
    /// Crée un moyen Carte. Le numéro complet n'est utilisé que pour en extraire
    /// les 4 derniers chiffres puis est ignoré (jamais persisté).
    /// </summary>
    public static Result<SavedPaymentMethod> CreateCard(
        Guid userId, string? label, string brand, string cardNumber,
        int expiryMonth, int expiryYear, string? holderName, bool isDefault)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<SavedPaymentMethod>(Error.Validation("payments.payment_method.user_required", "Utilisateur requis."));
        }

        var digits = new string((cardNumber ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < 12)
        {
            return Result.Failure<SavedPaymentMethod>(Error.Validation("payments.payment_method.card_invalid", "Numéro de carte invalide."));
        }

        if (expiryMonth is < 1 or > 12)
        {
            return Result.Failure<SavedPaymentMethod>(Error.Validation("payments.payment_method.expiry_invalid", "Mois d'expiration invalide."));
        }

        if (expiryYear < 2000)
        {
            return Result.Failure<SavedPaymentMethod>(Error.Validation("payments.payment_method.expiry_invalid", "Année d'expiration invalide."));
        }

        var last4 = digits[^4..];
        var resolvedBrand = string.IsNullOrWhiteSpace(brand) ? DetectBrand(digits) : brand.Trim();

        return Result.Success(new SavedPaymentMethod(
            SavedPaymentMethodId.New(),
            userId,
            PaymentMethodType.Card,
            string.IsNullOrWhiteSpace(label) ? resolvedBrand : label!.Trim(),
            resolvedBrand,
            last4,
            expiryMonth,
            expiryYear,
            string.IsNullOrWhiteSpace(holderName) ? null : holderName!.Trim(),
            isDefault));
    }

    /// <summary>
    /// Met à jour un moyen Mobile Money : libellé, opérateur et numéro (MSISDN).
    /// Refuse l'appel si le moyen n'est pas de type Mobile Money.
    /// </summary>
    public Result UpdateMobileMoney(string? label, string provider, string msisdn)
    {
        if (Type != PaymentMethodType.MobileMoney)
        {
            return Result.Failure(Error.Validation("payments.payment_method.type_mismatch", "Ce moyen n'est pas un compte Mobile Money."));
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            return Result.Failure(Error.Validation("payments.payment_method.provider_required", "Opérateur requis."));
        }

        var digits = new string((msisdn ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < 8)
        {
            return Result.Failure(Error.Validation("payments.payment_method.msisdn_invalid", "Numéro Mobile Money invalide."));
        }

        Provider = provider.Trim();
        AccountRef = digits;
        Label = string.IsNullOrWhiteSpace(label) ? Provider : label!.Trim();
        return Result.Success();
    }

    /// <summary>
    /// Met à jour un moyen Carte : libellé, expiration et titulaire. Le numéro de
    /// carte n'est pas modifiable (on ne conserve que les 4 derniers chiffres).
    /// Refuse l'appel si le moyen n'est pas de type Carte.
    /// </summary>
    public Result UpdateCard(string? label, int expiryMonth, int expiryYear, string? holderName)
    {
        if (Type != PaymentMethodType.Card)
        {
            return Result.Failure(Error.Validation("payments.payment_method.type_mismatch", "Ce moyen n'est pas une carte."));
        }

        if (expiryMonth is < 1 or > 12)
        {
            return Result.Failure(Error.Validation("payments.payment_method.expiry_invalid", "Mois d'expiration invalide."));
        }

        if (expiryYear < 2000)
        {
            return Result.Failure(Error.Validation("payments.payment_method.expiry_invalid", "Année d'expiration invalide."));
        }

        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        HolderName = string.IsNullOrWhiteSpace(holderName) ? null : holderName!.Trim();
        Label = string.IsNullOrWhiteSpace(label) ? Provider : label!.Trim();
        return Result.Success();
    }

    public void MarkDefault() => IsDefault = true;

    public void ClearDefault() => IsDefault = false;

    /// <summary>Détection sommaire de la marque d'après le préfixe (BIN).</summary>
    private static string DetectBrand(string digits)
    {
        if (digits.StartsWith('4'))
        {
            return "Visa";
        }

        if (digits.Length >= 2 && int.TryParse(digits[..2], out var two) && two is >= 51 and <= 55)
        {
            return "Mastercard";
        }

        if (digits.Length >= 2 && (digits.StartsWith("34") || digits.StartsWith("37")))
        {
            return "Amex";
        }

        return "Carte";
    }
}
