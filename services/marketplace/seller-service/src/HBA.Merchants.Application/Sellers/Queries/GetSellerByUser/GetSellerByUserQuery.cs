using HBA.Shared.Application.Messaging;
using HBA.Merchants.Application.Sellers.Queries.GetSeller;

namespace HBA.Merchants.Application.Sellers.Queries.GetSellerByUser;

/// <summary>Récupère la boutique rattachée à un compte utilisateur — `GET /merchants/me`.</summary>
public sealed record GetSellerByUserQuery(Guid UserId) : IQuery<SellerDetail>;
