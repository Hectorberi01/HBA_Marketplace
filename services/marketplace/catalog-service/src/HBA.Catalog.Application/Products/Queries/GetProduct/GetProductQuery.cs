using HBA.Shared.Application.Messaging;
using HBA.Catalog.Contracts;

namespace HBA.Catalog.Application.Products.Queries.GetProduct;

/// <summary>Lecture d'un produit par son identifiant.</summary>
public sealed record GetProductQuery(Guid ProductId) : IQuery<ProductSummary>;
