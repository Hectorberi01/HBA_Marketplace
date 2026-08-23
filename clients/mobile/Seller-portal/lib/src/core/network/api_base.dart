import 'package:dio/dio.dart';

import 'api_exception.dart';

/// Base commune aux clients d'API : convertit toute [DioException] en
/// [ApiException] lisible. Chaque appel réseau passe par [guard] — sans ça, une
/// erreur Dio brute (avec sa stack et son URL) remonterait jusqu'à l'écran.
abstract class ApiBase {
  const ApiBase(this.dio);

  final Dio dio;

  Future<T> guard<T>(Future<T> Function() call) async {
    try {
      return await call();
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}
