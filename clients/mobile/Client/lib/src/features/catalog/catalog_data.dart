import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

// ---------------------------------------------------------------------------
// Modèles (parsing tolérant des contrats BFF)
// ---------------------------------------------------------------------------

class ProductCard {
  ProductCard({
    required this.id,
    required this.name,
    required this.url,
    required this.price,
    required this.currency,
    required this.rating,
    this.compareAtPrice,
  });

  final String id;
  final String name;
  final String? url;
  final double price;
  final String currency;
  final double rating;

  /// Prix acheteur d'ORIGINE (barré) quand le vendeur applique une remise. Null =
  /// pas de promotion. Doit être strictement supérieur à [price] pour être affiché.
  final double? compareAtPrice;

  /// Le produit est-il en promotion (prix d'origine renseigné et plus élevé) ?
  bool get isOnSale => compareAtPrice != null && compareAtPrice! > price;

  /// Pourcentage de remise arrondi (ex. 20 pour −20 %). 0 si pas de promo.
  int get discountPercent =>
      isOnSale ? (((compareAtPrice! - price) / compareAtPrice!) * 100).round() : 0;

  factory ProductCard.fromJson(Map data) {
    final compareAt = Json.asDouble(data['compareAtPrice'] ?? data['compareAt'] ?? 0);
    return ProductCard(
      id: Json.str(data['id'] ?? data['productId']),
      name: Json.str(data['name'] ?? data['title'], 'Produit'),
      url: _primaryImage(data),
      price: Json.asDouble(data['price'] ?? data['minPrice'] ?? data['fromPrice']),
      currency: Json.str(data['currency'], AppConfig.defaultCurrency),
      rating: Json.asDouble(data['rating'] ?? data['averageRating']),
      compareAtPrice: compareAt > 0 ? compareAt : null,
    );
  }
}

/// Supprime les doublons de produits par identifiant (filet de sécurité si le
/// read model renvoie plusieurs fois le même produit).
List<ProductCard> dedupeProducts(Iterable<ProductCard> items) {
  final seen = <String>{};
  final out = <ProductCard>[];
  for (final p in items) {
    if (p.id.isEmpty || seen.add(p.id)) out.add(p);
  }
  return out;
}

String? _primaryImage(Map data) {
  final direct = data['imageUrl'] ?? data['primaryImageUrl'] ?? data['thumbnailUrl'];
  if (direct != null) return direct.toString();
  final media = Json.list(data['media'] ?? data['images']);
  if (media.isEmpty) return null;
  final primary = media.firstWhere(
    (m) => Json.asBool(m['isPrimary']),
    orElse: () => media.first,
  );
  return (primary['url'] ?? primary['imageUrl'])?.toString();
}

class Offer {
  Offer({
    required this.id,
    required this.sku,
    required this.sellerId,
    required this.sellerName,
    required this.price,
    required this.currency,
    required this.condition,
    required this.available,
    this.compareAtPrice,
  });

  final String id;

  /// SKU de la déclinaison vendue par cette offre — relie l'offre à une variante.
  final String sku;
  final String sellerId;
  final String sellerName;
  final double price;
  final String currency;
  final String condition;
  final bool available;

  /// Prix d'origine (barré) si le vendeur applique une remise sur cette offre.
  final double? compareAtPrice;

  bool get isOnSale => compareAtPrice != null && compareAtPrice! > price;
  int get discountPercent =>
      isOnSale ? (((compareAtPrice! - price) / compareAtPrice!) * 100).round() : 0;

  factory Offer.fromJson(Map data) {
    final status = Json.str(data['status']).toLowerCase();
    final compareAt = Json.asDouble(data['compareAtAmount'] ?? data['compareAtPrice'] ?? 0);
    return Offer(
      id: Json.str(data['id'] ?? data['offerId']),
      sku: Json.str(data['sku']),
      sellerId: Json.str(data['sellerId']),
      sellerName: Json.str(data['sellerName'] ?? data['shopName'], 'Vendeur'),
      // Le contrat Offers expose le prix sous « basePriceAmount ».
      price: Json.asDouble(data['basePriceAmount'] ?? data['price'] ?? data['amount']),
      currency: Json.str(data['currency'], AppConfig.defaultCurrency),
      condition: Json.str(data['condition'], 'New'),
      // Une offre est disponible si elle est Active (sinon Paused / OutOfStock).
      available: data['status'] != null ? status == 'active' : Json.asBool(data['available'] ?? data['inStock'], true),
      compareAtPrice: compareAt > 0 ? compareAt : null,
    );
  }
}

/// Déclinaison d'un produit (taille, couleur…) identifiée par un SKU. Les offres
/// se rattachent à un SKU : choisir une variante = filtrer les offres.
class ProductVariant {
  ProductVariant({required this.id, required this.sku, required this.attributes});

  final String id;
  final String sku;

  /// Attributs de la déclinaison, ex. { "Taille": "M", "Couleur": "Rouge" }.
  final Map<String, String> attributes;

  factory ProductVariant.fromJson(Map d) => ProductVariant(
        id: Json.str(d['id']),
        sku: Json.str(d['sku']),
        attributes: (d['attributes'] is Map)
            ? (d['attributes'] as Map).map((k, v) => MapEntry(k.toString(), v.toString()))
            : <String, String>{},
      );
}

class Review {
  Review({required this.author, required this.rating, required this.title, required this.body, required this.reply});
  final String author;
  final int rating;
  final String title;
  final String body;
  final String? reply;

  factory Review.fromJson(Map data) {
    return Review(
      author: Json.str(data['authorName'] ?? data['author'], 'Client'),
      rating: Json.asInt(data['rating']),
      title: Json.str(data['title']),
      body: Json.str(data['body'] ?? data['comment']),
      reply: (data['sellerReply'] ?? data['reply'])?.toString(),
    );
  }
}

class ProductDetail {
  ProductDetail({
    required this.id,
    required this.name,
    required this.description,
    required this.images,
    required this.rating,
    required this.offers,
    required this.variants,
  });

  final String id;
  final String name;
  final String description;
  final List<String> images;
  final double rating;
  final List<Offer> offers;

  /// Déclinaisons du produit (vide si produit sans variantes).
  final List<ProductVariant> variants;

  double? get minPrice => offers.isEmpty ? null : offers.map((o) => o.price).reduce((a, b) => a < b ? a : b);
  String get currency => offers.isEmpty ? AppConfig.defaultCurrency : offers.first.currency;

  /// Y a-t-il un vrai choix de déclinaison à présenter à l'acheteur ?
  bool get hasVariantChoice => variants.length > 1 && variants.any((v) => v.attributes.isNotEmpty);

  /// Clés d'attributs dans l'ordre de première apparition (ex. [Taille, Couleur]).
  List<String> get attributeKeys {
    final keys = <String>[];
    for (final v in variants) {
      for (final k in v.attributes.keys) {
        if (!keys.contains(k)) keys.add(k);
      }
    }
    return keys;
  }

  factory ProductDetail.fromJson(Map data) {
    // Le BFF renvoie { product: {...}, offers: [...], rating: {...} }.
    final product = data['product'] is Map ? data['product'] as Map : data;
    final media = Json.list(product['media'] ?? product['images']);
    final ratingMap = data['rating'] is Map ? data['rating'] as Map : null;
    
    return ProductDetail(
      id: Json.str(product['id'] ?? product['productId']),
      name: Json.str(product['name'] ?? product['title'], 'Produit'),
      description: Json.str(product['description']),
      images: media
          .map((m) => (m['url'] ?? m['imageUrl'])?.toString())
          .whereType<String>()
          .toList(),
      rating: ratingMap != null
          ? Json.asDouble(ratingMap['average'] ?? ratingMap['rating'])
          : Json.asDouble(product['rating'] ?? product['averageRating']),
      offers: Json.list(data['offers'] ?? product['offers']).map(Offer.fromJson).toList(),
      variants: Json.list(product['variants']).map(ProductVariant.fromJson).toList(),
    );
  }
}

/// Suggestion d'autocomplétion de recherche.
class Suggestion {
  Suggestion({required this.productId, required this.name});
  final String productId;
  final String name;

  factory Suggestion.fromJson(Map d) => Suggestion(
        productId: Json.str(d['productId'] ?? d['id']),
        name: Json.str(d['name'] ?? d['title']),
      );
}

class Category {
  Category({required this.id, required this.name, required this.slug, this.parentId});
  final String id;
  final String name;
  final String slug;

  /// Identifiant de la catégorie parente, ou null pour une catégorie de premier
  /// niveau (« parente »). Sert à n'afficher que les rayons principaux.
  final String? parentId;

  /// Catégorie de premier niveau (aucun parent).
  bool get isParent => parentId == null || parentId!.isEmpty;

  factory Category.fromJson(Map data) => Category(
        id: Json.str(data['id']),
        name: Json.str(data['name'], 'Catégorie'),
        slug: Json.str(data['slug']),
        parentId: (data['parentId'] as Object?)?.toString().isNotEmpty == true
            ? data['parentId'].toString()
            : null,
      );
}

// ---------------------------------------------------------------------------
// API
// ---------------------------------------------------------------------------

/// Page de résultats paginée (lazy loading). `hasMore` dérive du total serveur.
class ProductPage {
  ProductPage({required this.items, required this.total, required this.page, required this.pageSize});
  final List<ProductCard> items;
  final int total;
  final int page;
  final int pageSize;
  bool get hasMore => page * pageSize < total;
}

class CatalogApi {
  CatalogApi(this._dio);
  final Dio _dio;
  static const _p = AppConfig.apiPrefix;

  Future<ProductDetail> product(String id) => _wrap(() async {
        final resp = await _dio.get('$_p/products/$id');
        return ProductDetail.fromJson(resp.data as Map);
      });

  Future<List<Review>> reviews(String productId) => _wrap(() async {
        final resp = await _dio.get('$_p/products/$productId/reviews');
        return Json.list(resp.data).map(Review.fromJson).toList();
      });

  Future<List<ProductCard>> related(String productId) => _wrap(() async {
        final resp = await _dio.get('$_p/products/$productId/related');
        return Json.list(resp.data).map(ProductCard.fromJson).toList();
      });

  /// Recherche produits. [categoryId] filtre par rayon ; [sort] pilote le tri
  /// côté serveur (`price` = prix croissant, `rating` = mieux notés, sinon
  /// pertinence). Tri serveur = correct sur TOUT le jeu de résultats, pas juste
  /// la page chargée.
  Future<List<ProductCard>> search(String query, {String? categoryId, String? sort}) => _wrap(() async {
        final resp = await _dio.get('$_p/search', queryParameters: {
          if (query.isNotEmpty) 'q': query,
          if (categoryId != null && categoryId.isNotEmpty) 'category': categoryId,
          if (sort != null && sort.isNotEmpty) 'sort': sort,
        });
        final data = resp.data;
        final items = data is Map ? (data['items'] ?? data['results']) : data;
        return dedupeProducts(Json.list(items).map(ProductCard.fromJson));
      });

  /// Recherche PAGINÉE (lazy loading). [categoryIds] filtre sur un ensemble de
  /// rayons (une catégorie + son sous-arbre) ; [categoryId] reste possible pour un
  /// filtre simple. Renvoie la page + le total pour savoir s'il reste des résultats.
  Future<ProductPage> searchPaged({
    String query = '',
    String? categoryId,
    List<String>? categoryIds,
    String? sort,
    int page = 1,
    int pageSize = 20,
  }) =>
      _wrap(() async {
        final resp = await _dio.get('$_p/search', queryParameters: {
          if (query.isNotEmpty) 'q': query,
          if (categoryId != null && categoryId.isNotEmpty) 'category': categoryId,
          if (categoryIds != null && categoryIds.isNotEmpty) 'categoryIds': categoryIds.join(','),
          if (sort != null && sort.isNotEmpty) 'sort': sort,
          'page': page,
          'size': pageSize,
        });
        final data = resp.data;
        final rawItems = data is Map ? (data['items'] ?? data['results'] ?? const []) : data;
        final items = dedupeProducts(Json.list(rawItems).map(ProductCard.fromJson)).toList();
        final total = (data is Map ? (data['total'] as num?)?.toInt() : null) ?? items.length;
        return ProductPage(items: items, total: total, page: page, pageSize: pageSize);
      });

  Future<List<Suggestion>> suggest(String query) => _wrap(() async {
        final resp = await _dio.get('$_p/search/suggest', queryParameters: {'q': query});
        return Json.list(resp.data).map(Suggestion.fromJson).toList();
      });

  Future<List<Category>> categories() => _wrap(() async {
        final resp = await _dio.get('$_p/categories');
        return Json.list(resp.data).map(Category.fromJson).toList();
      });

  Future<T> _wrap<T>(Future<T> Function() fn) async {
    try {
      return await fn();
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final catalogApiProvider = Provider<CatalogApi>((ref) => CatalogApi(ref.watch(dioProvider)));

final productDetailProvider =
    FutureProvider.family<ProductDetail, String>((ref, id) => ref.watch(catalogApiProvider).product(id));

final productReviewsProvider =
    FutureProvider.family<List<Review>, String>((ref, id) => ref.watch(catalogApiProvider).reviews(id));

/// Paramètres de recherche : terme + filtre catégorie + tri. Le record a une
/// égalité de valeur : il sert directement de clé de `family`.
typedef SearchParams = ({String query, String? categoryId, String? sort});

final searchResultsProvider =
    FutureProvider.family<List<ProductCard>, SearchParams>((ref, p) async {
  final hasCategory = p.categoryId != null && p.categoryId!.isNotEmpty;
  if (p.query.trim().isEmpty && !hasCategory) return <ProductCard>[];
  return ref.watch(catalogApiProvider).search(p.query.trim(), categoryId: p.categoryId, sort: p.sort);
});

/// Liste des catégories (pour le filtre de recherche + navigation).
final categoriesProvider =
    FutureProvider<List<Category>>((ref) => ref.watch(catalogApiProvider).categories());

/// Tous les identifiants du sous-arbre d'une catégorie ([rootId] inclus), à
/// partir de l'arbre plat des catégories (chaque `parentId` pointe vers le
/// parent). Sert à afficher les produits d'un rayon ET de ses sous-catégories.
///
/// Fonction PURE et publique : réutilisée par la page rayon et testable seule.
List<String> categorySubtreeIds(String rootId, List<Category> all) {
  final childrenOf = <String, List<String>>{};
  for (final c in all) {
    final pid = c.parentId;
    if (pid != null && pid.isNotEmpty) (childrenOf[pid] ??= []).add(c.id);
  }
  final out = <String>[];
  final stack = <String>[rootId];
  while (stack.isNotEmpty) {
    final id = stack.removeLast();
    out.add(id);
    final kids = childrenOf[id];
    if (kids != null) stack.addAll(kids);
  }
  return out;
}

final relatedProductsProvider =
    FutureProvider.family<List<ProductCard>, String>((ref, id) async {
  try {
    return await ref.watch(catalogApiProvider).related(id);
  } catch (_) {
    return <ProductCard>[];
  }
});

final suggestionsProvider =
    FutureProvider.family<List<Suggestion>, String>((ref, q) async {
  if (q.trim().length < 2) return <Suggestion>[];
  try {
    return await ref.watch(catalogApiProvider).suggest(q.trim());
  } catch (_) {
    return <Suggestion>[];
  }
});
