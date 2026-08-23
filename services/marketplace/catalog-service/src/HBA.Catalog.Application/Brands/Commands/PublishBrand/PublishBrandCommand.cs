using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Brands.Commands.PublishBrand;

/// <summary>Publie (approuve) une marque après modération : Pending -> Active.</summary>
public sealed record PublishBrandCommand(Guid BrandId) : ICommand;
