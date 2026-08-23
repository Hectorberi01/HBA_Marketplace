using HBA.Shared.Application.Messaging;
using HBA.Catalog.Contracts;

namespace HBA.Catalog.Application.Categories.Queries.GetCategory;

public sealed record GetCategoryQuery(Guid CategoryId) : IQuery<CategorySummary>;
