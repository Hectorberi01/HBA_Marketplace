namespace HBA.Merchants.Contracts;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE COMPTE DE REVERSEMENT D'UN VENDEUR, RENDU SANS AMBIGUÏTÉ.
///
/// CE TYPE EXISTE PARCE QU'UN `PayoutAccountSummary?` NE SUFFISAIT PAS.
///
/// `SellerSummary.Payout` est nullable, et son `null` recouvrait TROIS choses
/// distinctes : le vendeur n'existe pas, le vendeur existe sans compte, ou — et
/// c'est le cas qui a coûté cher — le transport gRPC ne porte tout simplement pas
/// le champ, donc le mappeur a écrit `null` faute de mieux.
///
/// Le troisième cas a rendu impossible TOUT retrait vendeur de la plateforme :
/// wallet-service lisait `seller?.Payout`, obtenait `null` quel que soit le
/// vendeur, et refusait chaque demande avec « Aucun compte de versement Mobile
/// Money configuré » — un message qui envoyait le vendeur ressaisir un compte
/// déjà enregistré.
///
/// Ici, il n'y a plus de troisième cas : ce type ne se construit qu'à partir
/// d'une réponse réelle. Et les deux autres restent distincts, parce qu'ils
/// n'appellent pas la même conduite — un identifiant inconnu est une erreur, un
/// compte manquant est une étape d'onboarding à terminer.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
