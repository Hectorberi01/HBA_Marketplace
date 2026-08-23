using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Brands.Commands.CreateBrand;

public sealed record CreateBrandCommand(
    string Name,
    string? LogoUrl = null,
    string? Description = null) : ICommand<Guid>;
