namespace HBA.Gateway.Application.Contracts.Catalog;

/// <summary>
/// Miroirs des contrats publics de <c>catalog-service</c>.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// RECOPIÉS DEPUIS `HBA.Catalog.Contracts`, PAS RÉFÉRENCÉS. C'EST DÉLIBÉRÉ.
///
/// Référencer le projet de contrats du service donnerait un couplage à la
/// compilation entre la passerelle et chaque microservice : plus aucun service ne
/// pourrait être déployé sans recompiler la passerelle, et l'indépendance de
/// déploiement — la seule raison d'avoir découpé — disparaîtrait.
///
/// Le prix est réel : ces records peuvent DÉRIVER du service. C'est ce que les
/// tests de contrat du §42 doivent attraper, et c'est pour cela qu'ils ne sont pas
/// facultatifs.
///
/// CHAMPS OMIS VOLONTAIREMENT.
///
/// Un miroir n'est pas une copie : il ne porte que ce que la passerelle LIT. Le
/// `AttributeSchema` d'une catégorie, par exemple, sert à la validation d'un
/// produit côté vendeur et n'a rien à faire dans une réponse mobile. Ce que la
/// désérialisation ignore ne coûte rien ; ce qu'on transporte inutilement, si.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record CatalogProduct(
    Guid Id,
    Guid SellerId,
    Guid CategoryId,
    Guid? BrandId,
    string Name,
    string Description,
    string Slug,
    string Status,
    IReadOnlyList<CatalogProductVariant> Variants,
    IReadOnlyList<CatalogProductMedia> Media);

public sealed record CatalogProductVariant(
    Guid Id,
    string Sku,
    IReadOnlyDictionary<string, string> Attributes,
    int WeightGrams);

/// <summary>
/// DEUX IDENTIFIANTS, ET LE SERVICE PRÉVIENT QU'ILS DIFFÈRENT.
///
/// <paramref name="Id"/> désigne la LIGNE, <paramref name="MediaId"/> le FICHIER
/// dans media-service. Les confondre donne un « média introuvable » sur des
/// routes qui fonctionnent. <paramref name="MediaId"/> vaut <c>Guid.Empty</c>
/// pour une image d'avant la bascule vers media-service : seule l'URL la désigne.
/// </summary>
public sealed record CatalogProductMedia(
    Guid Id,
    Guid MediaId,
    string Url,
    string Type,
    bool IsPrimary,
    int Position,
    string AltText);

public sealed record CatalogCategory(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Slug,
    string Path,
    string Status,
    string? ImageUrl);
