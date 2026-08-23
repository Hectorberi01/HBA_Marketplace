using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Brands.Commands.UnpublishBrand;

/// <summary>Dépublie une marque (Active -> Pending), la retirant du catalogue actif.</summary>
public sealed record UnpublishBrandCommand(Guid BrandId) : ICommand;
