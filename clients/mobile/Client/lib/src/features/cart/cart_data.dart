import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

class CartLine {
  CartLine({
    required this.offerId,
    required this.productName,
    required this.imageUrl,
    required this.unitPrice,
    required this.quantity,
    required this.currency,
    double lineTotal = 0,
  }) : _lineTotal = lineTotal;

  final String offerId;
  final String productName;
  final String? imageUrl;
  final double unitPrice;
  final int quantity;
  final String currency;
  final double _lineTotal;

  double get lineTotal => _lineTotal != 0 ? _lineTotal : unitPrice * quantity;

  factory CartLine.fromJson(Map d) {
    return CartLine(
      offerId: Json.str(d['offerId'] ?? d['id']),
      // Le contrat CartLineSummary n'a pas de nom produit : on affiche le SKU.
      productName: Json.str(d['productName'] ?? d['name'] ?? d['sku'], 'Article'),
      imageUrl: (d['imageUrl'] ?? d['thumbnailUrl'])?.toString(),
      // Prix : finalUnitPrice (remises incluses) sinon unitBaseAmount.
      unitPrice: Json.asDouble(d['finalUnitPrice'] ?? d['unitBaseAmount'] ?? d['unitPrice'] ?? d['price']),
      quantity: Json.asInt(d['quantity'], 1),
      currency: Json.str(d['currency'], AppConfig.defaultCurrency),
      lineTotal: Json.asDouble(d['lineTotal']),
    );
  }
}

class Cart {
  Cart({
    required this.lines,
    required this.currency,
    double? subtotal,
    double? grandTotal,
    this.promotionCode,
  })  : subtotal = subtotal ?? lines.fold(0.0, (s, l) => s + l.lineTotal),
        grandTotal = grandTotal ?? subtotal ?? lines.fold(0.0, (s, l) => s + l.lineTotal);

  final List<CartLine> lines;
  final String currency;

  /// Sous-total avant remises (contrat backend : `subtotal`).
  final double subtotal;

  /// Total à payer, remises incluses (contrat backend : `grandTotal`). C'est
  /// le montant que l'acheteur réglera réellement — d'où la source unique.
  final double grandTotal;

  /// Code promo attaché au panier côté serveur (null si aucun).
  final String? promotionCode;

  /// Remise totale (vendeur + plateforme), déduite des montants du serveur.
  double get discount => (subtotal - grandTotal).clamp(0, double.infinity).toDouble();
  bool get hasCoupon => promotionCode != null && promotionCode!.isNotEmpty;

  /// Conservé pour compatibilité : le total affiché est le grandTotal.
  double get total => grandTotal;

  int get itemCount => lines.fold(0, (s, l) => s + l.quantity);
  bool get isEmpty => lines.isEmpty;

  static Cart empty() =>
      Cart(lines: const [], currency: AppConfig.defaultCurrency, subtotal: 0, grandTotal: 0);

  factory Cart.fromJson(dynamic data) {
    if (data is! Map) return Cart.empty();
    final lines = Json.list(data['lines'] ?? data['items']).map(CartLine.fromJson).toList();
    final subtotal = data['subtotal'] != null ? Json.asDouble(data['subtotal']) : null;
    // grandTotal (remises incluses), avec repli sur l'ancien alias `total`.
    final grand = data['grandTotal'] != null
        ? Json.asDouble(data['grandTotal'])
        : (data['total'] != null ? Json.asDouble(data['total']) : subtotal);
    final code = data['promotionCode']?.toString();
    return Cart(
      lines: lines,
      currency: Json.str(data['currency'], lines.isEmpty ? AppConfig.defaultCurrency : lines.first.currency),
      subtotal: subtotal,
      grandTotal: grand,
      promotionCode: (code != null && code.isNotEmpty) ? code : null,
    );
  }
}

/// Issue de l'application ou du retrait d'un code promo. Le panier revalorisé
/// (sous-total, remise, grandTotal, promotionCode) est rechargé séparément :
/// cet objet ne sert qu'au retour utilisateur immédiat (SnackBar).
///
/// Contrat backend `POST /mobile/cart/coupon` : 200 + panier revalorisé si le
/// code est accepté, 204 (corps vide) si le domaine le refuse (invalide,
/// expiré, déjà utilisé). Le succès se lit donc sur le STATUT, plus sur un
/// champ `valid` qui n'existe plus.
class CouponOutcome {
  CouponOutcome({required this.applied, this.code = '', this.message = ''});

  final bool applied;
  final String code;
  final String message;

  static CouponOutcome rejected([String message = 'Code invalide ou expiré.']) =>
      CouponOutcome(applied: false, message: message);
}

class CartApi {
  CartApi(this._dio);
  final Dio _dio;
  static const _p = AppConfig.cart;

  Future<Cart> get() => _wrap(() async {
        final resp = await _dio.get('$_p/');
        return Cart.fromJson(resp.data);
      });

  /// Attache un code promo au panier. 200 = accepté (corps = panier
  /// revalorisé, dont `promotionCode`), 204 = refusé par le domaine.
  Future<CouponOutcome> applyCoupon(String code) => _wrap(() async {
        final resp = await _dio.post('$_p/coupon', data: {'code': code});
        final data = resp.data;
        if (resp.statusCode == 204 || data is! Map) {
          return CouponOutcome.rejected();
        }
        final applied = Json.str(data['promotionCode']);
        return CouponOutcome(applied: applied.isNotEmpty, code: applied);
      });

  /// Retire le code promo du panier. Sans cet
  /// appel, la promotion restait attachée côté serveur et le total « remisé »
  /// revenait au rechargement, alors que l'acheteur croyait l'avoir enlevée.
  Future<void> removeCoupon() => _wrap(() async {
        await _dio.delete('$_p/coupon');
      });

  Future<void> add(String offerId, int quantity) => _wrap(() async {
        await _dio.post('$_p/items', data: {'offerId': offerId, 'quantity': quantity});
      });

  Future<void> updateQuantity(String offerId, int quantity) => _wrap(() async {
        // PUT, ET NON PATCH — LE VERBE A CHANGÉ AVEC LA PASSERELLE.
        //
        // Le BFF du monolithe exposait PATCH ; commerce-service expose
        // `PUT /api/cart/items/{offerId}`. Garder PATCH aurait produit un 405,
        // que Dio remonte comme une erreur réseau quelconque — on aurait
        // cherché du côté de la connexion, pas du verbe.
        await _dio.put('$_p/items/$offerId', data: {'quantity': quantity});
      });

  Future<void> remove(String offerId) => _wrap(() async {
        await _dio.delete('$_p/items/$offerId');
      });

  Future<T> _wrap<T>(Future<T> Function() fn) async {
    try {
      return await fn();
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final cartApiProvider = Provider<CartApi>((ref) => CartApi(ref.watch(dioProvider)));

/// Panier courant, rechargé après chaque mutation.
class CartController extends AsyncNotifier<Cart> {
  @override
  Future<Cart> build() => ref.watch(cartApiProvider).get();

  Future<void> add(String offerId, {int quantity = 1}) async {
    await ref.read(cartApiProvider).add(offerId, quantity);
    ref.invalidateSelf();
    await future;
  }

  Future<void> setQuantity(String offerId, int quantity) async {
    if (quantity <= 0) return remove(offerId);
    await ref.read(cartApiProvider).updateQuantity(offerId, quantity);
    ref.invalidateSelf();
    await future;
  }

  Future<void> remove(String offerId) async {
    await ref.read(cartApiProvider).remove(offerId);
    ref.invalidateSelf();
    await future;
  }

  /// Applique un code promo puis RECHARGE le panier : le sous-total, la remise
  /// et le grandTotal affichés proviennent alors du serveur, pas d'un calcul
  /// client.
  Future<CouponOutcome> applyCoupon(String code) async {
    final outcome = await ref.read(cartApiProvider).applyCoupon(code);
    ref.invalidateSelf();
    await future;
    return outcome;
  }

  /// Retire le code promo puis recharge le panier.
  Future<void> removeCoupon() async {
    await ref.read(cartApiProvider).removeCoupon();
    ref.invalidateSelf();
    await future;
  }
}

final cartControllerProvider =
    AsyncNotifierProvider<CartController, Cart>(CartController.new);

/// Nombre d'articles (pour le badge de navigation).
final cartCountProvider = Provider<int>((ref) {
  return ref.watch(cartControllerProvider).maybeWhen(
        data: (c) => c.itemCount,
        orElse: () => 0,
      );
});
