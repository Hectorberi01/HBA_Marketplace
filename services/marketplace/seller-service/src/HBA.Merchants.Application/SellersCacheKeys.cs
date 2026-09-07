namespace HBA.Merchants.Application;

/// <summary>Clés de cache du module Sellers.</summary>
public static class SellersCacheKeys
{
    /// <summary>Résumé d'un vendeur, par identifiant de vendeur.</summary>
    public static string Seller(Guid sellerId) => $"sellers:seller:{sellerId}";

    /// <summary>Résumé d'un vendeur, par identifiant d'utilisateur (connexion vendeur).</summary>
    public static string SellerByUser(Guid userId) => $"sellers:by-user:{userId}";

    /// <summary>10 minutes.</summary>
    public static readonly TimeSpan SellerTtl = TimeSpan.FromMinutes(10);

    /// <summary>Mémorisation d'une absence — protège les endpoints anonymes.</summary>
    public static readonly TimeSpan MissTtl = TimeSpan.FromSeconds(30);

    /// <summary>Contexte d'autorisation d'un compte : son vendeur, ses permissions.</summary>
    public static string MemberAccess(Guid userId) => $"sellers:access:{userId}";

    /// <summary>LE VENDEUR A-T-IL LE DROIT D'OPÉRER ?</summary>
    public static string SellerCanOperate(Guid sellerId) => $"sellers:can-operate:{sellerId}";

    /// <summary>Deux minutes.</summary>
    public static readonly TimeSpan MemberAccessTtl = TimeSpan.FromMinutes(2);
}
