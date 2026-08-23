import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_exception.dart';
import '../../core/network/not_migrated.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

class OrderLine {
  OrderLine({
    required this.offerId,
    required this.productId,
    required this.sellerId,
    required this.sku,
    required this.name,
    required this.imageUrl,
    required this.quantity,
    required this.lineTotal,
  });
  final String offerId;
  final String productId;

  /// Vendeur du produit — sert à ouvrir la conversation avec le bon vendeur.
  final String sellerId;
  final String sku;
  final String name;
  final String? imageUrl;
  final int quantity;
  final double lineTotal;

  factory OrderLine.fromJson(Map d) => OrderLine(
        offerId: Json.str(d['offerId']),
        productId: Json.str(d['productId']),
        sellerId: Json.str(d['sellerId']),
        sku: Json.str(d['sku']),
        name: Json.str(d['productName'] ?? d['name'] ?? d['sku'], 'Article'),
        imageUrl: (d['imageUrl'] ?? d['thumbnailUrl'])?.toString(),
        quantity: Json.asInt(d['quantity'], 1),
        lineTotal: Json.asDouble(d['lineTotal'] ?? d['total']),
      );
}

class OrderShippingAddress {
  OrderShippingAddress({
    required this.label,
    required this.recipient,
    required this.phone,
    required this.communeName,
    required this.quartier,
    required this.landmark,
    required this.line1,
  });
  final String label;
  final String recipient;
  final String phone;

  /// Libellé résolu par le SERVEUR depuis le code figé sur la commande. L'app
  /// n'a donc pas besoin du référentiel pour afficher une commande passée.
  final String communeName;

  final String? quartier;

  /// Point de repère. Vide sur les commandes antérieures à la refonte : le champ
  /// n'existait pas, et rien ne permet de le reconstituer.
  final String? landmark;

  final String? line1;

  /// Repère en tête, comme sur l'adresse du carnet : c'est ce qu'on lit à un zem.
  String get summary => [landmark, quartier, line1, communeName]
      .where((s) => s != null && s.isNotEmpty)
      .join(', ');

  factory OrderShippingAddress.fromJson(Map d) => OrderShippingAddress(
        label: Json.str(d['label'], 'Adresse'),
        recipient: Json.str(d['recipient']),
        phone: Json.str(d['phone']),
        communeName: Json.str(d['communeName']),
        quartier: _orNull(d['quartier']),
        landmark: _orNull(d['landmark']),
        line1: _orNull(d['line1']),
      );

  static String? _orNull(dynamic v) {
    final s = v?.toString().trim();
    return (s == null || s.isEmpty) ? null : s;
  }
}

class OrderItem {
  OrderItem({
    required this.id,
    required this.status,
    required this.total,
    required this.subtotal,
    required this.shippingFee,
    required this.currency,
    required this.createdAt,
    required this.lines,
    this.shippingAddress,
  });

  final String id;
  final String status;

  /// Montant réellement dû : articles + livraison.
  final double total;

  /// Somme des articles, SANS la livraison.
  final double subtotal;

  /// Frais de livraison figés à la commande.
  ///
  /// Ils manquaient au modèle, et l'écran de détail affichait donc le total sous
  /// le libellé « Sous-total » : un article à 1 150 XOF suivi d'un « sous-total »
  /// de 2 650, sans rien pour expliquer l'écart. Sur une place de marché, un écart
  /// inexpliqué entre le prix d'un article et la somme réclamée est exactement ce
  /// qui déclenche un litige.
  final double shippingFee;

  final String currency;
  final DateTime? createdAt;
  final List<OrderLine> lines;
  final OrderShippingAddress? shippingAddress;

  String get reference =>
      'CMD-${id.replaceAll('-', '').substring(0, id.length >= 8 ? 8 : id.length).toUpperCase()}';
  int get itemCount => lines.fold(0, (s, l) => s + l.quantity);

  /// La commande attend son règlement : rien n'a encore été encaissé.
  bool get isAwaitingPayment => switch (status.toLowerCase()) {
        'awaitingpayment' || 'awaiting_payment' || 'pending' => true,
        _ => false,
      };

  factory OrderItem.fromJson(Map d) {
    final total = Json.asDouble(d['grandTotal'] ?? d['total']);
    final shippingFee = Json.asDouble(d['shippingFee']);

    // Repli : les versions du serveur antérieures à l'ajout de `shippingFee`
    // n'envoient ni lui ni un `subtotal` cohérent. On préfère alors afficher le
    // total seul plutôt qu'une ventilation fausse — un sous-total erroné est pire
    // qu'un sous-total absent.
    final rawSubtotal = Json.asDouble(d['subtotal']);
    final subtotal = rawSubtotal > 0 ? rawSubtotal : total - shippingFee;

    return OrderItem(
      id: Json.str(d['id'] ?? d['orderId']),
      status: Json.str(d['status'], 'Pending'),
      total: total,
      subtotal: subtotal,
      shippingFee: shippingFee,
      currency: Json.str(d['currency'], AppConfig.defaultCurrency),
      createdAt: Json.asDate(d['createdAtUtc'] ?? d['createdAt']),
      lines: Json.list(d['lines'] ?? d['items']).map(OrderLine.fromJson).toList(),
      shippingAddress: d['shippingAddress'] is Map
          ? OrderShippingAddress.fromJson(d['shippingAddress'] as Map)
          : null,
    );
  }
}

class Shipment {
  Shipment({
    required this.status,
    required this.carrier,
    required this.trackingNumber,
    required this.shippedAt,
    required this.deliveredAt,
  });
  final String status;
  final String? carrier;
  final String? trackingNumber;
  final DateTime? shippedAt;
  final DateTime? deliveredAt;

  factory Shipment.fromJson(Map d) => Shipment(
        status: Json.str(d['status'], 'Pending'),
        carrier: (d['carrier'])?.toString(),
        trackingNumber: (d['trackingNumber'])?.toString(),
        shippedAt: Json.asDate(d['shippedAtUtc'] ?? d['shippedAt']),
        deliveredAt: Json.asDate(d['deliveredAtUtc'] ?? d['deliveredAt']),
      );
}

/// Détail commande = commande + ses expéditions (tracking) en un appel.
class OrderBundle {
  OrderBundle({required this.order, required this.shipments});
  final OrderItem order;
  final List<Shipment> shipments;
}

/// Étape de suivi (0 = annulée, 1 = confirmée, 2 = préparée, 3 = expédiée, 4 = livrée).
///
/// On combine le statut de la commande (paiement/confirmation) ET le statut des
/// expéditions (préparée/expédiée/livrée par le vendeur) en prenant le plus
/// avancé : ainsi l'app reflète les actions vendeur même si le statut commande
/// reste en retrait.
int fulfillmentStep(String orderStatus, List<Shipment> shipments) {
  if (orderStatus.toLowerCase() == 'cancelled') return 0;

  int fromOrder(String s) => switch (s.toLowerCase()) {
        'delivered' => 4,
        'shipped' => 3,
        'processing' || 'paid' || 'confirmed' => 2,

        // ─────────────────────────────────────────────────────────────────────
        // TANT QUE LE PAIEMENT N'EST PAS ENCAISSÉ, AUCUNE ÉTAPE N'EST FRANCHIE.
        //
        // Ces statuts tombaient dans le `_ => 1` ci-dessous, et l'application
        // cochait « Commande confirmée » sur une commande jamais payée. L'acheteur
        // voyait donc sa commande validée d'un côté et « En attente de paiement »
        // de l'autre : les deux ne peuvent pas être vrais, et c'est le premier
        // qu'on croit. Il n'avait alors aucune raison de réessayer de payer.
        // ─────────────────────────────────────────────────────────────────────
        'awaitingpayment' || 'awaiting_payment' || 'pending' => 0,

        _ => 1,
      };

  int fromShipment(String s) => switch (s.toLowerCase()) {
        'delivered' => 4,
        'shipped' => 3,
        'preparing' || 'prepared' => 2,
        'pending' => 1,
        _ => 0,
      };

  var idx = fromOrder(orderStatus);
  for (final sh in shipments) {
    final si = fromShipment(sh.status);
    if (si > idx) idx = si;
  }
  return idx;
}

class OrdersApi {
  OrdersApi(this._dio);
  final Dio _dio;
  static const _p = AppConfig.orders;

  Future<List<OrderItem>> list() => _wrap(() async {
        final resp = await _dio.get('$_p/');
        return Json.list(resp.data).map(OrderItem.fromJson).toList();
      });

  Future<OrderBundle> detail(String id) => _wrap(() async {
        final resp = await _dio.get('$_p/$id');
        final data = resp.data;
        // Le BFF renvoie { order: {...}, shipments: [...] }.
        final orderMap = (data is Map && data['order'] is Map) ? data['order'] as Map : (data as Map);
        final shipments = Json.list(data['shipments']);
        return OrderBundle(
          order: OrderItem.fromJson(orderMap),
          shipments: shipments.map(Shipment.fromJson).toList(),
        );
      });

  Future<void> cancel(String id, String? reason) => _wrap(() async {
        await _dio.post('$_p/$id/cancel', data: {'reason': reason});
      });

  /// Demande de retour — INDISPONIBLE sur la nouvelle plateforme.
  ///
  /// Le module Returns n'a pas été extrait du monolithe : ni agrégat, ni route.
  /// L'ancien `POST /orders/{id}/return` visait le BFF `/mobile`, qui reste en
  /// service pour l'ancienne version de l'application.
  ///
  /// Lever ici plutôt que d'appeler dans le vide : l'écran affiche un message
  /// exact au lieu d'un 404 qu'on prendrait pour une panne.
  Future<void> requestReturn(String id, String offerId, String reason) async =>
      NotMigrated.call('returns', screen: 'demande de retour');

  Future<T> _wrap<T>(Future<T> Function() fn) async {
    try {
      return await fn();
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final ordersApiProvider = Provider<OrdersApi>((ref) => OrdersApi(ref.watch(dioProvider)));
final ordersListProvider = FutureProvider<List<OrderItem>>((ref) => ref.watch(ordersApiProvider).list());
final orderDetailProvider =
    FutureProvider.family<OrderBundle, String>((ref, id) => ref.watch(ordersApiProvider).detail(id));
