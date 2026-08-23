namespace HBA.Catalog.Contracts;

/// <summary>
/// Vue publique d'un produit, exposée aux autres modules via l'API in-process.
/// DTO stable : ne reflète pas la structure interne de l'agrégat.
/// </summary>
public sealed record ProductSummary(
    Guid Id,
    Guid SellerId,
    Guid CategoryId,
    Guid? BrandId,
    string Name,
    string Description,
    string Slug,
    string Status,
    string? Gtin,
    string? Ean,
    Guid? ProductGroupId,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ProductVariantSummary> Variants,
    IReadOnlyList<ProductMediaSummary> Media,

    /// <summary>
    /// AJOUTÉ EN DERNIER, AVEC UN DÉFAUT, ET C'EST DÉLIBÉRÉ.
    ///
    /// Ce DTO se construit positionnellement à un seul endroit
    /// (<c>ProductMapping.Projeter</c>) — mais l'insérer AU MILIEU de quinze
    /// paramètres dont plusieurs sont des listes ferait glisser les suivants sans
    /// que le compilateur bronche, exactement comme la tarification l'a fait dans
    /// les commandes. En dernier, un oubli d'appelant donne une fiche sans
    /// caractéristiques, pas une fiche dont les images sont dans les variantes.
    /// </summary>
    IReadOnlyList<ProductSpecificationGroupSummary>? Specifications = null,

    /// <summary>
    /// La boutique à laquelle la fiche est rattachée, ou <c>null</c>.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// C'EST CE QUI PERMET AU CADRAGE PAR BOUTIQUE DE MORDRE (lot F).
    ///
    /// `Product.StoreId` existe dans le domaine depuis longtemps ; il ne
    /// traversait simplement pas ce DTO, donc `DenyUnlessProductOwnerAsync` ne
    /// pouvait comparer QUE le vendeur. Un responsable de la boutique A modifiait
    /// donc les fiches de la boutique B du même vendeur, et rien ne pouvait le
    /// voir depuis la garde.
    ///
    /// NULLABLE, ET LE `null` N'EST PAS UN REFUS.
    ///
    /// Les fiches créées avant que la boutique ne soit exigée n'en portent pas.
    /// Les refuser fermerait le catalogue historique d'un vendeur à toute son
    /// équipe ; `CanInStore(null, …)` retombe donc sur l'union, c'est-à-dire sur
    /// le comportement d'avant le cadrage. Le jour où toutes les fiches en
    /// porteront une, ce sera une contrainte de schéma, pas une garde à durcir.
    ///
    /// EN DERNIER ET AVEC UN DÉFAUT, comme `Specifications` — pour la raison
    /// écrite juste au-dessus.
    /// </remarks>
    Guid? StoreId = null);

/// <summary>
/// Un groupe de la fiche technique, vu de l'extérieur (§12).
///
/// <paramref name="DisplayOrder"/> N'EST PAS DÉCORATIF.
///
/// C'est la seule raison pour laquelle ces caractéristiques sont stockées en deux
/// tables plutôt que dans un jsonb : le vendeur choisit l'ordre, et le client doit
/// le respecter. Un client qui rendrait ces groupes dans l'ordre de réception sans
/// trier afficherait, un jour, une fiche technique dans le désordre — sans que rien
/// ne casse, et sans que personne ne signale autre chose qu'« une fiche mal faite ».
/// </summary>
public sealed record ProductSpecificationGroupSummary(
    Guid Id,
    string Name,
    int DisplayOrder,
    IReadOnlyList<ProductSpecificationSummary> Items);

/// <summary>Une ligne de la fiche technique — « Type : Super Retina XDR OLED ».</summary>
public sealed record ProductSpecificationSummary(
    Guid Id,
    string Name,
    string Value,
    int DisplayOrder);

public sealed record ProductVariantSummary(
    Guid Id,
    string Sku,
    IReadOnlyDictionary<string, string> Attributes,
    string? Barcode,
    int WeightGrams);

/// <summary>
/// Une image de produit, vue de l'extérieur.
///
/// DEUX IDENTIFIANTS, ET ILS NE DÉSIGNENT PAS LA MÊME CHOSE.
///
/// <paramref name="Id"/> est celui de la LIGNE — c'est lui qu'on renvoie pour
/// détacher l'image ou la promouvoir en principale. <paramref name="MediaId"/>
/// est celui du FICHIER dans le service média — c'est lui qui sert à demander
/// une variante ou à retrouver l'original. Les confondre donnerait un « média
/// introuvable » sur des routes qui, elles, fonctionnent.
///
/// <paramref name="MediaId"/> vaut zéro pour une image d'avant la bascule : son
/// fichier vit encore dans l'ancien stockage, et seule l'URL le désigne.
/// </summary>
public sealed record ProductMediaSummary(
    Guid Id,
    Guid MediaId,
    string Url,
    string Type,
    bool IsPrimary,
    int Position,
    string AltText);
