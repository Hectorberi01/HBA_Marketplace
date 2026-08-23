import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';

class ReviewApi {
  ReviewApi(this._dio);
  final Dio _dio;

  Future<void> submit({
    required String productId,
    required String orderId,
    required int rating,
    String? title,
    required String body,
  }) async {
    try {
      await _dio.post('${AppConfig.apiPrefix}/reviews', data: {
        'productId': productId,
        'orderId': orderId,
        'rating': rating,
        'title': title,
        'body': body,
      });
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final reviewApiProvider = Provider<ReviewApi>((ref) => ReviewApi(ref.watch(dioProvider)));
