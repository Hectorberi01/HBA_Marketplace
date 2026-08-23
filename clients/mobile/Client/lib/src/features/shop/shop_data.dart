import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';
import '../catalog/catalog_data.dart';

/// Profil public d'une boutique vendeur (+ ses produits si fournis).
class Shop {
  Shop({
    required this.id,
    required this.shopName,
    required this.logoUrl,
    required this.rating,
    required this.salesCount,
    required this.verified,
    required this.products,
  });

  final String id;
  final String shopName;
  final String? logoUrl;
  final double rating;
  final int salesCount;
  final bool verified;
  final List<ProductCard> products;

  factory Shop.fromJson(Map data) {
    final seller = data['seller'] is Map ? data['seller'] as Map : data;
    return Shop(
      id: Json.str(seller['id']),
      shopName: Json.str(seller['shopName'] ?? seller['name'], 'Boutique'),
      logoUrl: (seller['logoUrl'])?.toString(),
      rating: Json.asDouble(seller['rating']),
      salesCount: Json.asInt(seller['salesCount']),
      verified: Json.str(seller['kybStatus']).toLowerCase() == 'verified',
      products: Json.list(data['products']).map(ProductCard.fromJson).toList(),
    );
  }
}

class ShopApi {
  ShopApi(this._dio);
  final Dio _dio;

  Future<Shop> shop(String sellerId) async {
    try {
      final resp = await _dio.get('${AppConfig.apiPrefix}/shop/$sellerId');
      return Shop.fromJson(resp.data as Map);
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final shopApiProvider = Provider<ShopApi>((ref) => ShopApi(ref.watch(dioProvider)));
final shopProvider = FutureProvider.family<Shop, String>((ref, id) => ref.watch(shopApiProvider).shop(id));
