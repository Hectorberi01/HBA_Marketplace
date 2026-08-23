import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

class LoyaltyTransaction {
  LoyaltyTransaction({required this.amount, required this.reason, required this.createdAt});
  final int amount;
  final String reason;
  final DateTime? createdAt;

  factory LoyaltyTransaction.fromJson(Map d) => LoyaltyTransaction(
        amount: Json.asInt(d['amount']),
        reason: Json.str(d['reason']),
        createdAt: Json.asDate(d['createdAtUtc'] ?? d['createdAt']),
      );
}

class LoyaltyAccount {
  LoyaltyAccount({
    required this.pointsBalance,
    required this.lifetimePoints,
    required this.tier,
    required this.transactions,
  });

  final int pointsBalance;
  final int lifetimePoints;
  final String tier;
  final List<LoyaltyTransaction> transactions;

  factory LoyaltyAccount.fromJson(Map d) => LoyaltyAccount(
        pointsBalance: Json.asInt(d['pointsBalance']),
        lifetimePoints: Json.asInt(d['lifetimePoints']),
        tier: Json.str(d['tier'], 'Bronze'),
        transactions: Json.list(d['transactions']).map(LoyaltyTransaction.fromJson).toList(),
      );
}

class LoyaltyApi {
  LoyaltyApi(this._dio);
  final Dio _dio;
  static const _p = '${AppConfig.apiPrefix}/loyalty';

  Future<LoyaltyAccount> get() async {
    try {
      final resp = await _dio.get(_p);
      return LoyaltyAccount.fromJson(resp.data as Map);
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  Future<void> redeem(int amount) async {
    try {
      await _dio.post('$_p/redeem', data: {'amount': amount});
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final loyaltyApiProvider = Provider<LoyaltyApi>((ref) => LoyaltyApi(ref.watch(dioProvider)));
final loyaltyProvider = FutureProvider<LoyaltyAccount>((ref) => ref.watch(loyaltyApiProvider).get());
