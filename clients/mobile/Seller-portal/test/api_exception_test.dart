import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hba_express_pro/src/core/network/api_exception.dart';

/// Tests du parseur d'erreurs API — la zone qui affichait « Une erreur est
/// survenue » au lieu du vrai message. Le backend renvoie ses erreurs en
/// `application/problem+json` (title = code, detail = message), que Dio LAISSE
/// souvent sous forme de CHAÎNE (il ne décode que `application/json`). Ces tests
/// verrouillent le décodage de cette chaîne.
DioException _http(int status, dynamic body) {
  final req = RequestOptions(path: '/seller/products/x/variants');
  return DioException(
    requestOptions: req,
    type: DioExceptionType.badResponse,
    response: Response(requestOptions: req, statusCode: status, data: body),
  );
}

void main() {
  group('ApiException.fromDio', () {
    test('corps problem+json en CHAÎNE → extrait detail (message) et title (code)', () {
      final e = _http(
        403,
        '{"title":"offers.offer.seller_inactive","detail":"Le vendeur doit être actif.","status":403}',
      );
      final ex = ApiException.fromDio(e);

      expect(ex.message, 'Le vendeur doit être actif.');
      expect(ex.code, 'offers.offer.seller_inactive');
      expect(ex.statusCode, 403);
    });

    test('corps déjà décodé en Map → même extraction', () {
      final e = _http(409, {
        'title': 'catalog.product.slug_taken',
        'detail': 'Le slug « x » existe déjà.',
        'status': 409,
      });
      final ex = ApiException.fromDio(e);

      expect(ex.message, 'Le slug « x » existe déjà.');
      expect(ex.code, 'catalog.product.slug_taken');
      expect(ex.statusCode, 409);
    });

    test('corps vide → message par défaut selon le statut (500)', () {
      final ex = ApiException.fromDio(_http(500, ''));
      expect(ex.message, 'Erreur serveur.');
      expect(ex.statusCode, 500);
    });

    test('404 → isNotFound vrai', () {
      final ex = ApiException.fromDio(_http(404, ''));
      expect(ex.isNotFound, isTrue);
    });

    test('erreur de connexion (pas de réponse) → message réseau', () {
      final req = RequestOptions(path: '/x');
      final ex = ApiException.fromDio(
        DioException(requestOptions: req, type: DioExceptionType.connectionError),
      );
      expect(ex.message, 'Impossible de joindre le serveur.');
      expect(ex.statusCode, isNull);
    });

    test('délai dépassé → message dédié', () {
      final req = RequestOptions(path: '/x');
      final ex = ApiException.fromDio(
        DioException(requestOptions: req, type: DioExceptionType.receiveTimeout),
      );
      expect(ex.message, contains('Délai dépassé'));
    });

    test('toString() renvoie le message lisible', () {
      final ex = ApiException.fromDio(_http(400, '{"detail":"Requête refusée."}'));
      expect(ex.toString(), 'Requête refusée.');
    });
  });
}
