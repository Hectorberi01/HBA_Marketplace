import 'package:dio/dio.dart';

/// Exception normalisée pour les erreurs d'API, avec un message lisible.
class ApiException implements Exception {
  ApiException(this.message, {this.statusCode, this.code});

  final String message;
  final int? statusCode;
  final String? code;

  /// Construit une exception à partir d'une [DioException], en extrayant le
  /// message/code renvoyés par le backend (format Result/ProblemDetails).
  factory ApiException.fromDio(DioException e) {
    final response = e.response;
    if (response != null) {
      final data = response.data;
      String? message;
      String? code;
      if (data is Map) {
        message = (data['detail'] ?? data['message'] ?? data['title'] ?? data['error'])
            ?.toString();
        code = (data['code'] ?? data['errorCode'])?.toString();
      }
      return ApiException(
        message ?? _statusMessage(response.statusCode),
        statusCode: response.statusCode,
        code: code,
      );
    }

    switch (e.type) {
      case DioExceptionType.connectionTimeout:
      case DioExceptionType.receiveTimeout:
      case DioExceptionType.sendTimeout:
        return ApiException('Délai dépassé. Vérifiez votre connexion.');
      case DioExceptionType.connectionError:
        return ApiException('Impossible de joindre le serveur.');
      default:
        return ApiException(e.message ?? 'Une erreur est survenue.');
    }
  }

  static String _statusMessage(int? status) {
    switch (status) {
      case 400:
        return 'Requête invalide.';
      case 401:
        return 'Session expirée, veuillez vous reconnecter.';
      case 403:
        return "Action non autorisée.";
      case 404:
        return 'Ressource introuvable.';
      case 409:
        return 'Conflit avec l\'état actuel.';
      case 500:
        return 'Erreur serveur.';
      default:
        return 'Une erreur est survenue.';
    }
  }

  @override
  String toString() => message;
}
