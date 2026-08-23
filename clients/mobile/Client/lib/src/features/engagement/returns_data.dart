import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

class ReturnRequest {
  ReturnRequest({
    required this.id,
    required this.orderId,
    required this.reason,
    required this.status,
    required this.currency,
    required this.refundAmount,
    required this.carrier,
    required this.trackingNumber,
    required this.createdAt,
    required this.resolvedAt,
  });

  final String id;
  final String orderId;
  final String reason;
  final String status;
  final String currency;
  final double? refundAmount;
  final String? carrier;
  final String? trackingNumber;
  final DateTime? createdAt;
  final DateTime? resolvedAt;

  factory ReturnRequest.fromJson(Map d) => ReturnRequest(
        id: Json.str(d['id']),
        orderId: Json.str(d['orderId']),
        reason: Json.str(d['reason']),
        status: Json.str(d['status'], 'Requested'),
        currency: Json.str(d['currency'], AppConfig.defaultCurrency),
        refundAmount: d['refundAmount'] == null ? null : Json.asDouble(d['refundAmount']),
        carrier: (d['carrier'])?.toString(),
        trackingNumber: (d['trackingNumber'])?.toString(),
        createdAt: Json.asDate(d['createdAtUtc'] ?? d['createdAt']),
        resolvedAt: Json.asDate(d['resolvedAtUtc'] ?? d['resolvedAt']),
      );
}

class ReturnsApi {
  ReturnsApi(this._dio);
  final Dio _dio;

  Future<List<ReturnRequest>> list() async {
    try {
      final resp = await _dio.get('${AppConfig.apiPrefix}/returns');
      return Json.list(resp.data).map(ReturnRequest.fromJson).toList();
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final returnsApiProvider = Provider<ReturnsApi>((ref) => ReturnsApi(ref.watch(dioProvider)));
final returnsProvider = FutureProvider<List<ReturnRequest>>((ref) => ref.watch(returnsApiProvider).list());
