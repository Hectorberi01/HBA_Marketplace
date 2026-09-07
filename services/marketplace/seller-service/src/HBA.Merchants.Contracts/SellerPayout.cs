namespace HBA.Merchants.Contracts;

/// <summary>LE COMPTE DE REVERSEMENT D'UN VENDEUR, RENDU SANS AMBIGUÏTÉ.</summary>
/// <param name="SellerExists">Le vendeur a-t-il été trouvé ?</param>
/// <param name="Account">Son compte, ou null s'il n'en a pas encore déclaré.</param>
public sealed record SellerPayout(bool SellerExists, PayoutAccountSummary? Account)
{
    /// <summary>Aucun vendeur ne porte cet identifiant.</summary>
    public static SellerPayout Unknown { get; } = new(SellerExists: false, Account: null);

    /// <summary>Le vendeur existe, mais n'a pas encore déclaré de compte.</summary>
    public static SellerPayout NotConfigured { get; } = new(SellerExists: true, Account: null);

    /// <summary>Le vendeur existe et voici son compte.</summary>
    public static SellerPayout Of(PayoutAccountSummary account) => new(SellerExists: true, Account: account);
}
