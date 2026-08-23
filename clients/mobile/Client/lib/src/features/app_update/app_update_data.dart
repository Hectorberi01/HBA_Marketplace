import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

/// Politique de version renvoyée par le backend (`GET /mobile/app/version`).
///
/// Les numéros sont des BUILDS (versionCode Android / CFBundleVersion iOS) : des
/// entiers strictement croissants, comparables de façon fiable — là où une
/// version marketing (« 1.2.0 ») se compare mal.
class AppVersionPolicy {
  const AppVersionPolicy({
    required this.minSupportedBuild,
    required this.latestBuild,
    required this.updateUrlAndroid,
    required this.updateUrlIos,
    required this.message,
  });

  /// Build minimal accepté. Un build strictement inférieur est bloqué.
  final int minSupportedBuild;

  /// Dernier build publié (informatif).
  final int latestBuild;

  final String updateUrlAndroid;
  final String updateUrlIos;

  /// Message facultatif affiché sur l'écran de blocage.
  final String message;

  factory AppVersionPolicy.fromJson(Map d) => AppVersionPolicy(
        minSupportedBuild: Json.asInt(d['minSupportedBuild']),
        latestBuild: Json.asInt(d['latestBuild']),
        updateUrlAndroid: Json.str(d['updateUrlAndroid']),
        updateUrlIos: Json.str(d['updateUrlIos']),
        message: Json.str(d['message']),
      );
}

class AppUpdateApi {
  AppUpdateApi(this._dio);
  final Dio _dio;

  /// Endpoint ANONYME : joignable même déconnecté (un client périmé doit
  /// pouvoir apprendre qu'il l'est avant toute tentative de connexion).
  Future<AppVersionPolicy> policy() async {
    try {
      final resp = await _dio.get('${AppConfig.apiPrefix}/app/version');
      final data = resp.data;
      return AppVersionPolicy.fromJson(data is Map ? data : const {});
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final appUpdateApiProvider = Provider<AppUpdateApi>((ref) => AppUpdateApi(ref.watch(dioProvider)));
