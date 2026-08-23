import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';
import '../catalog/catalog_data.dart';

/// Une section de la page d'accueil (carrousel ou grille de produits).
class HomeSection {
  HomeSection({required this.title, required this.products});
  final String title;
  final List<ProductCard> products;
}

class HomeFeed {
  HomeFeed({required this.sections, required this.categories});
  final List<HomeSection> sections;
  final List<Category> categories;
}

/// Bannière promotionnelle du carrousel d'accueil (image + lien optionnel).
class HomeBanner {
  HomeBanner({required this.imageUrl, this.linkUrl, this.title});
  final String imageUrl;
  final String? linkUrl;
  final String? title;

  factory HomeBanner.fromJson(Map data) => HomeBanner(
        imageUrl: Json.str(data['imageUrl']),
        linkUrl: Json.str(data['linkUrl']).isEmpty ? null : Json.str(data['linkUrl']),
        title: Json.str(data['title']).isEmpty ? null : Json.str(data['title']),
      );
}

class HomeApi {
  HomeApi(this._dio);
  final Dio _dio;
  static const _p = AppConfig.apiPrefix;

  Future<HomeFeed> feed() async {
    try {
      final home = await _dio.get('$_p/home');
      final sections = _parseSections(home.data);

      // Les catégories peuvent ne pas être exposées (endpoint optionnel) : on
      // ne fait pas échouer tout l'accueil pour autant.
      List<Category> categories = const [];
      try {
        final cats = await _dio.get('$_p/categories');
        if ((cats.statusCode ?? 0) < 400) {
          categories = Json.list(cats.data).map(Category.fromJson).toList();
        }
      } catch (_) {
        categories = const [];
      }

      return HomeFeed(sections: sections, categories: categories);
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Le BFF peut renvoyer soit une liste de sections, soit une liste de
  /// produits, soit un objet à clés multiples — on extrait au mieux.
  List<HomeSection> _parseSections(dynamic data) {
    // Cas 0 (forme réelle du BFF Mobile) :
    //   { featured: { items: [...] }, promoted: [...], recommendations: {...} }
    if (data is Map && (data['featured'] is Map || data['promoted'] is List)) {
      final sections = <HomeSection>[];

      // Section ÉDITORIALE « Mis en avant » (produits tagués featured) — en tête.
      final promoted = Json.list(data['promoted']);
      if (promoted.isNotEmpty) {
        sections.add(HomeSection(title: 'Mis en avant', products: dedupeProducts(promoted.map(ProductCard.fromJson))));
      }

      // Sélection générale « À la une ».
      if (data['featured'] is Map) {
        final featured = data['featured'] as Map;
        final items = Json.list(featured['items'] ?? featured['results']);
        if (items.isNotEmpty) {
          sections.add(HomeSection(title: 'À la une', products: dedupeProducts(items.map(ProductCard.fromJson))));
        }
      }
      return sections;
    }

    // Cas 1 : { sections: [ { title, products: [...] } ] }
    if (data is Map && data['sections'] is List) {
      return (data['sections'] as List).whereType<Map>().map((s) {
        return HomeSection(
          title: Json.str(s['title'] ?? s['name'], 'Sélection'),
          products: Json.list(s['products'] ?? s['items']).map(ProductCard.fromJson).toList(),
        );
      }).where((s) => s.products.isNotEmpty).toList();
    }

    // Cas 2 : objet avec listes nommées (featured, trending, newArrivals…)
    if (data is Map) {
      final sections = <HomeSection>[];
      data.forEach((key, value) {
        if (value is List && value.isNotEmpty && value.first is Map) {
          sections.add(HomeSection(
            title: _label(key.toString()),
            products: Json.list(value).map(ProductCard.fromJson).toList(),
          ));
        }
      });
      if (sections.isNotEmpty) return sections;
    }

    // Cas 3 : liste de produits directe.
    final products = Json.list(data).map(ProductCard.fromJson).toList();
    if (products.isNotEmpty) {
      return [HomeSection(title: 'À la une', products: products)];
    }
    return const [];
  }

  String _label(String key) {
    switch (key.toLowerCase()) {
      case 'featured':
        return 'À la une';
      case 'trending':
        return 'Tendances';
      case 'newarrivals':
      case 'new':
        return 'Nouveautés';
      case 'recommendations':
        return 'Pour vous';
      case 'deals':
      case 'promotions':
        return 'Promotions';
      default:
        return key;
    }
  }
}

final homeApiProvider = Provider<HomeApi>((ref) => HomeApi(ref.watch(dioProvider)));
final homeFeedProvider = FutureProvider<HomeFeed>((ref) => ref.watch(homeApiProvider).feed());

/// Bannières du carrousel d'accueil. Dégradation gracieuse : liste vide si
/// l'endpoint échoue ou n'est pas configuré (le carrousel se cache alors).
final homeBannersProvider = FutureProvider<List<HomeBanner>>((ref) async {
  try {
    final res = await ref.watch(dioProvider).get('${AppConfig.apiPrefix}/home/banners');
    return Json.list(res.data).map(HomeBanner.fromJson).toList();
  } catch (_) {
    return const <HomeBanner>[];
  }
});
