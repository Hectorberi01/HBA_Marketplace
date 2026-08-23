import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

/// Résultat d'un paiement initié (place la commande puis lance le paiement).
class PaymentResult {
  PaymentResult({
    required this.orderId,
    required this.paymentId,
    required this.status,
    required this.redirectUrl,
  });

  final String orderId;
  final String paymentId;
  final String status;
  final String? redirectUrl;

  bool get requiresAction => redirectUrl != null && redirectUrl!.isNotEmpty;

  factory PaymentResult.fromJson(Map d) {
    final payment = d['payment'] is Map ? d['payment'] as Map : d;
    return PaymentResult(
      orderId: Json.str(d['orderId'] ?? payment['orderId']),
      paymentId: Json.str(payment['id'] ?? payment['paymentId']),
      status: Json.str(payment['status'], 'Pending'),
      redirectUrl: (payment['redirectUrl'] ?? payment['checkoutUrl'] ?? payment['hostedUrl'])?.toString(),
    );
  }
}

/// Moyens de paiement proposés (mobile money + carte + FedaPay hébergé).
enum PayMethod { mtnMomo, moovMoney, wave, card, fedapay }

extension PayMethodX on PayMethod {
  String get label => switch (this) {
        PayMethod.mtnMomo => 'MTN Mobile Money',
        PayMethod.moovMoney => 'Moov Money',
        PayMethod.wave => 'Wave',
        PayMethod.card => 'Carte bancaire',
        PayMethod.fedapay => 'FedaPay',
      };

  String get method => switch (this) {
        PayMethod.card || PayMethod.fedapay => 'Card',
        _ => 'MobileMoney',
      };

  String get provider => switch (this) {
        PayMethod.mtnMomo => 'MtnMomo',
        PayMethod.moovMoney => 'MoovMoney',
        PayMethod.wave => 'Wave',
        // La carte passe par la page sécurisée FedaPay (Visa/MasterCard).
        PayMethod.card => 'FedaPay',
        PayMethod.fedapay => 'FedaPay',
      };

  /// Flux à demander au BFF : page hébergée (redirection) ou RequestToPay (USSD).
  String get flow => switch (this) {
        PayMethod.card || PayMethod.fedapay => 'HostedCheckout',
        _ => 'RequestToPay',
      };

  /// Vrai si le numéro du payeur est requis en amont (Mobile Money direct).
  bool get requiresPhone => this == PayMethod.mtnMomo || this == PayMethod.moovMoney || this == PayMethod.wave;

  /// Vrai si le paiement se finalise sur une page web (redirection / WebView).
  bool get isHosted => this == PayMethod.fedapay || this == PayMethod.card;
}

/// Option de livraison renvoyée par le devis BFF (forfait).
class ShippingOption {
  const ShippingOption({required this.code, required this.label, required this.eta, required this.amount, required this.currency});
  final String code;
  final String label;
  final String eta;
  final double amount;
  final String currency;

  factory ShippingOption.fromJson(Map d) => ShippingOption(
        code: Json.str(d['code']),
        label: Json.str(d['label'], 'Livraison'),
        eta: Json.str(d['eta']),
        amount: Json.asDouble(d['amount']),
        currency: Json.str(d['currency'], AppConfig.defaultCurrency),
      );
}

/// Devis calculé PAR LE SERVEUR pour une option de livraison donnée : sous-total,
/// remise, frais de port et TOTAL À PAYER réels (= ce que le serveur débitera).
/// À préférer au calcul client, qui peut diverger des montants serveur.
class CheckoutQuote {
  const CheckoutQuote({
    required this.subtotal,
    required this.discount,
    required this.shippingAmount,
    required this.total,
    required this.currency,
    required this.options,
  });

  final double subtotal;
  final double discount;
  final double shippingAmount;
  final double total;
  final String currency;
  final List<ShippingOption> options;

  factory CheckoutQuote.fromJson(Map d) {
    final ship = d['shipping'] is Map ? d['shipping'] as Map : const {};
    return CheckoutQuote(
      subtotal: Json.asDouble(d['subtotal']),
      discount: Json.asDouble(d['discount']),
      shippingAmount: Json.asDouble(ship['amount']),
      total: Json.asDouble(d['total']),
      currency: Json.str(d['currency'], AppConfig.defaultCurrency),
      options: Json.list(d['shippingOptions']).map(ShippingOption.fromJson).toList(),
    );
  }
}

/// Le prestataire a tranché : plus rien ne bougera sur ce paiement.
///
/// Public parce que la reprise de paiement, depuis le détail de commande, doit
/// appliquer EXACTEMENT le même critère d'arrêt que le tunnel d'achat. Deux
/// listes séparées finiraient par diverger, et l'un des deux écrans resterait à
/// sonder un paiement déjà tranché.
bool isTerminalPaymentStatus(String s) => const [
      'captured', 'succeeded', 'paid', 'completed', 'failed', 'cancelled', 'declined',
    ].contains(s.toLowerCase());

/// Le paiement a abouti.
bool isSuccessfulPaymentStatus(String s) =>
    const ['captured', 'succeeded', 'paid', 'completed'].contains(s.toLowerCase());

class CheckoutApi {
  CheckoutApi(this._dio);
  final Dio _dio;
  static const _p = '${AppConfig.apiPrefix}/checkout';

  Future<List<ShippingOption>> shippingOptions() async {
    try {
      final resp = await _dio.get('$_p/shipping-options');
      return Json.list(resp.data).map(ShippingOption.fromJson).toList();
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Devis serveur pour l'option [shipping] : totaux réels du panier actif.
  Future<CheckoutQuote> quote({String shipping = 'standard'}) async {
    try {
      final resp = await _dio.get('$_p/quote', queryParameters: {'shipping': shipping});
      return CheckoutQuote.fromJson(resp.data is Map ? resp.data as Map : const {});
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Crée la commande et initie le paiement.
  ///
  /// ───────────────────────────────────────────────────────────────────────────
  /// [idempotencyKey] EST LE GARDE-FOU CONTRE LE DOUBLE PAIEMENT.
  ///
  /// Le délai de réception est de 20 secondes. Sur un réseau mobile béninois, le
  /// serveur peut parfaitement créer la commande et initier le paiement pendant
  /// que le client abandonne : l'acheteur voit « Délai dépassé », le bouton
  /// redevient actif, il réappuie — et il paie deux fois.
  ///
  /// L'appelant génère la clé UNE FOIS par tentative d'achat et la RÉUTILISE
  /// telle quelle à chaque nouvel essai. C'est ce qui distingue « je réessaie le
  /// même paiement » de « j'en lance un nouveau ».
  ///
  /// Le serveur l'exploite : `/checkout/pay` hache la clé avec l'identifiant de
  /// l'acheteur et resert la réponse mémorisée pendant 24 heures. (Ce commentaire
  /// disait le contraire — l'en-tête a depuis été pris en compte côté BFF.)
  /// ───────────────────────────────────────────────────────────────────────────
  Future<PaymentResult> pay({
    required PayMethod method,
    required String idempotencyKey,
    String? payerPhone,
    String? returnUrl,
    String? cancelUrl,
    String? addressId,
    String? shippingCode,
  }) async {
    try {
      final resp = await _dio.post(
        '$_p/pay',
        options: Options(headers: {'Idempotency-Key': idempotencyKey}),
        data: {
        'method': method.method,
        'provider': method.provider,
        'flow': method.flow,
        'returnUrl': returnUrl,
        'cancelUrl': cancelUrl,
        'payerPhone': payerPhone,
        'addressId': addressId,
        'shippingCode': shippingCode,
      });
      if ((resp.statusCode ?? 0) >= 400) {
        final data = resp.data;
        throw ApiException(
          data is Map ? Json.str(data['detail'] ?? data['message'], 'Paiement refusé') : 'Paiement refusé',
          statusCode: resp.statusCode,
        );
      }
      return PaymentResult.fromJson(resp.data as Map);
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// ───────────────────────────────────────────────────────────────────────────
  /// RELANCE LE PAIEMENT D'UNE COMMANDE DÉJÀ CRÉÉE.
  ///
  /// [pay] part du PANIER : il crée une commande puis la paie. Quand le paiement
  /// échoue, la commande existe et le panier a été vidé — cette route ne sert donc
  /// plus à rien, et l'acheteur restait devant une commande impayable.
  ///
  /// Ici, tout est déjà figé côté serveur : articles, adresse, frais de livraison.
  /// D'où l'absence d'`addressId` et de `shippingCode`, et surtout l'absence de clé
  /// d'idempotence — elle existe pour empêcher la création de DEUX commandes, ce
  /// qui ne peut plus arriver. C'est le serveur qui interdit deux paiements
  /// simultanés, après avoir vérifié l'état réel auprès du prestataire.
  /// ───────────────────────────────────────────────────────────────────────────
  Future<PaymentResult> payOrder({
    required String orderId,
    required PayMethod method,
    String? payerPhone,
    String? returnUrl,
    String? cancelUrl,
  }) async {
    try {
      final resp = await _dio.post('${AppConfig.apiPrefix}/orders/$orderId/pay', data: {
        'method': method.method,
        'provider': method.provider,
        'flow': method.flow,
        'returnUrl': returnUrl,
        'cancelUrl': cancelUrl,
        'payerPhone': payerPhone,
      });
      if ((resp.statusCode ?? 0) >= 400) {
        final data = resp.data;
        throw ApiException(
          data is Map ? Json.str(data['detail'] ?? data['message'], 'Paiement refusé') : 'Paiement refusé',
          statusCode: resp.statusCode,
        );
      }
      return PaymentResult.fromJson(resp.data as Map);
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Au retour d'une page hébergée (FedaPay/carte) : demande au serveur de
  /// réconcilier le statut auprès du PSP avant de l'interroger.
  Future<void> confirmRedirect(String paymentId) async {
    try {
      await _dio.post('${AppConfig.apiPrefix}/payments/$paymentId/confirm');
    } on DioException catch (_) {
      // Non bloquant : le polling de statut prendra le relais.
    }
  }

  Future<String> status(String paymentId) async {
    try {
      final resp = await _dio.get('${AppConfig.apiPrefix}/payments/$paymentId/status');
      final data = resp.data;
      return data is Map ? Json.str(data['status'], 'Pending') : 'Pending';
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final checkoutApiProvider = Provider<CheckoutApi>((ref) => CheckoutApi(ref.watch(dioProvider)));

/// Options de livraison (forfaits) chargées depuis le BFF.
final shippingOptionsProvider =
    FutureProvider<List<ShippingOption>>((ref) => ref.watch(checkoutApiProvider).shippingOptions());

/// Devis serveur pour l'option de livraison choisie (clé = code de livraison).
final checkoutQuoteProvider = FutureProvider.family<CheckoutQuote, String>(
    (ref, shippingCode) => ref.watch(checkoutApiProvider).quote(shipping: shippingCode));
