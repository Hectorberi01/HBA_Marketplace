using HBA.Shared.Application.Messaging;
using HBA.Merchants.Domain.Sellers;

namespace HBA.Merchants.Application.Sellers.Commands.RegisterSeller;

/// <summary>
/// Onboarde un vendeur rattaché à un compte Identity existant et vérifié.
/// <paramref name="Metadata"/> (infos société déclarées) est optionnelle et null
/// par défaut : l'onboarding admin ne la fournit pas, l'auto-inscription oui.
/// </summary>
public sealed record RegisterSellerCommand(
    Guid UserId,
    string ShopName,
    decimal CommissionRate = 0.10m,
    SellerCompanyInfo? Metadata = null) : ICommand<Guid>;
