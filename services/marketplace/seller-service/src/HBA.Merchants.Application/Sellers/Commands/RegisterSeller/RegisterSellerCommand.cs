using HBA.Shared.Application.Messaging;
using HBA.Merchants.Domain.Sellers;

namespace HBA.Merchants.Application.Sellers.Commands.RegisterSeller;

/// <summary>Onboarde un vendeur rattaché à un compte Identity existant et vérifié.</summary>
public sealed record RegisterSellerCommand(
    Guid UserId,
    string ShopName,
    decimal CommissionRate = 0.10m,
    SellerCompanyInfo? Metadata = null) : ICommand<Guid>;
