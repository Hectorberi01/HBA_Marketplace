using HBA.Shared.Application.Messaging;
using HBA.Merchants.Domain.Sellers;

namespace HBA.Merchants.Application.Sellers.Commands.UpdateSellerMetadata;

/// <summary>Met à jour les informations société (metadata) déclarées par le vendeur.</summary>
public sealed record UpdateSellerMetadataCommand(Guid SellerId, SellerCompanyInfo? Metadata) : ICommand;
