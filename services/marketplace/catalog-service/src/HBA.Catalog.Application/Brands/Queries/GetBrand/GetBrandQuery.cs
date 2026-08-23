using HBA.Shared.Application.Messaging;
using HBA.Catalog.Contracts;

namespace HBA.Catalog.Application.Brands.Queries.GetBrand;

public sealed record GetBrandQuery(Guid BrandId) : IQuery<BrandSummary>;
