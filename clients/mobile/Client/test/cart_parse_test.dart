import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http_mock_adapter/http_mock_adapter.dart';

import 'package:client_mp_mobile/src/features/cart/cart_data.dart';
import 'package:client_mp_mobile/src/core/network/api_exception.dart';

/// Flux critique — PANIER (`GET /mobile/cart/`).
///
/// Le panier porte le montant à payer : son parsing doit être exact. On fige la
/// source unique (grandTotal = total réglé), la somme des lignes, la remise
/// déduite et la présence d'un coupon.
void main() {
  late Dio dio;
  late DioAdapter adapter;

  setUp(() {
    dio = Dio(BaseOptions(baseUrl: 'https://test.local'));
    adapter = DioAdapter(dio: dio);
  });

  test('parse — lignes, sous-total, grandTotal, remise, coupon', () async {
    adapter.onGet('/mobile/cart/', (server) => server.reply(200, {
          'currency': 'XOF',
          'subtotal': 12000,
          'grandTotal': 10000,
          'promotionCode': 'BIENVENUE',
          'lines': [
            {'offerId': 'O1', 'productName': 'Casque', 'finalUnitPrice': 5000, 'quantity': 2, 'currency': 'XOF', 'lineTotal': 10000},
          ],
        }));

    final cart = await CartApi(dio).get();

    expect(cart.lines, hasLength(1));
    expect(cart.itemCount, 2);
    expect(cart.subtotal, 12000);
    expect(cart.grandTotal, 10000);
    expect(cart.total, 10000, reason: 'total affiché = grandTotal (montant réglé)');
    expect(cart.discount, 2000, reason: 'remise = subtotal - grandTotal');
    expect(cart.hasCoupon, isTrue);
    expect(cart.promotionCode, 'BIENVENUE');
  });

  test('parse — panier vide', () async {
    adapter.onGet('/mobile/cart/', (server) => server.reply(200, {'lines': <dynamic>[], 'currency': 'XOF'}));

    final cart = await CartApi(dio).get();
    expect(cart.isEmpty, isTrue);
    expect(cart.itemCount, 0);
    expect(cart.hasCoupon, isFalse);
  });

  test('erreur serveur — 500 remonte en ApiException', () async {
    adapter.onGet('/mobile/cart/', (server) => server.reply(500, {'detail': 'Erreur serveur'}));
    await expectLater(CartApi(dio).get(), throwsA(isA<ApiException>()));
  });
}
