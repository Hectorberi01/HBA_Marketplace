import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http_mock_adapter/http_mock_adapter.dart';

import 'package:client_mp_mobile/src/features/checkout/checkout_data.dart';
import 'package:client_mp_mobile/src/core/network/api_exception.dart';

/// Flux critique — PAIEMENT (`POST /mobile/checkout/pay`, statut).
///
/// Le point le plus sensible : on fige le parsing du résultat de paiement
/// (id commande/paiement, redirection FedaPay = action requise), la lecture du
/// statut (le serveur fait foi), et la remontée d'erreur.
void main() {
  late Dio dio;
  late DioAdapter adapter;

  /// Clé d'idempotence factice. En production elle est générée par l'écran de
  /// paiement, une fois par tentative d'achat et réutilisée à chaque réessai —
  /// c'est ce qui distingue « je retente le même paiement » de « j'en lance un
  /// nouveau ». Ici sa valeur n'importe pas, seule sa présence est requise.
  const key = 'test-idem-key';

  setUp(() {
    dio = Dio(BaseOptions(baseUrl: 'https://test.local'));
    adapter = DioAdapter(dio: dio);
  });

  test('pay — redirection FedaPay imbriquée -> requiresAction', () async {
    adapter.onPost(
      '/mobile/checkout/pay',
      (server) => server.reply(200, {
        'orderId': 'ORD-1',
        'payment': {'id': 'PAY-1', 'status': 'Pending', 'redirectUrl': 'https://fedapay.example/checkout/abc'},
      }),
      data: Matchers.any,
    );

    final res = await CheckoutApi(dio).pay(method: PayMethod.fedapay, idempotencyKey: key);

    expect(res.orderId, 'ORD-1');
    expect(res.paymentId, 'PAY-1');
    expect(res.requiresAction, isTrue);
    expect(res.redirectUrl, 'https://fedapay.example/checkout/abc');
  });

  test('PaymentResult.fromJson — forme à plat sans redirection -> pas d\'action', () {
    final res = PaymentResult.fromJson({'orderId': 'ORD-2', 'id': 'PAY-2', 'status': 'Succeeded'});
    expect(res.orderId, 'ORD-2');
    expect(res.paymentId, 'PAY-2');
    expect(res.requiresAction, isFalse);
  });

  test('status — le serveur fait foi', () async {
    adapter.onGet('/mobile/payments/PAY-1/status', (server) => server.reply(200, {'status': 'Succeeded'}));
    final status = await CheckoutApi(dio).status('PAY-1');
    expect(status, 'Succeeded');
  });

  test('pay — 402/400 remonte en ApiException (paiement refusé)', () async {
    adapter.onPost(
      '/mobile/checkout/pay',
      (server) => server.reply(400, {'detail': 'Solde insuffisant'}),
      data: Matchers.any,
    );

    await expectLater(
      CheckoutApi(dio).pay(method: PayMethod.mtnMomo, idempotencyKey: key),
      throwsA(isA<ApiException>()),
    );
  });

  /// L'en-tête doit PARTIR, pas seulement être accepté par la signature.
  ///
  /// C'est lui, et lui seul, qui permettra au serveur de reconnaître un réessai
  /// plutôt que de créer une seconde commande. Une régression silencieuse ici — un
  /// `Options` oublié lors d'un refactor — ne se verrait qu'en production, chez un
  /// acheteur débité deux fois.
  test('pay — la clé d\'idempotence est envoyée en en-tête', () async {
    String? seenHeader;

    dio.interceptors.add(InterceptorsWrapper(
      onRequest: (options, handler) {
        seenHeader = options.headers['Idempotency-Key']?.toString();
        handler.next(options);
      },
    ));

    adapter.onPost(
      '/mobile/checkout/pay',
      (server) => server.reply(200, {'orderId': 'ORD-3', 'payment': {'id': 'PAY-3', 'status': 'Succeeded'}}),
      data: Matchers.any,
    );

    await CheckoutApi(dio).pay(method: PayMethod.fedapay, idempotencyKey: key);

    expect(seenHeader, key);
  });
}
