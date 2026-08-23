import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/identity/seller_identity.dart';
import '../../core/network/api_base.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/paging/paged_list.dart';
import '../../shared/utils/formatters.dart';

/// Une ligne de commande, telle que `OrderLineSummary` la rend.
class OrderLine {
  OrderLine({
    required this.productId,
    required this.sellerId,
    required this.sku,
    required this.quantity,
    required this.unitPrice,
    required this.lineTotal,
  });

  final String productId;

  /// INDISPENSABLE : UNE COMMANDE PEUT CONTENIR LES LIGNES D'AUTRES VENDEURS.
  ///
  /// `OrderRepository.ListBySellerAsync` sélectionne les commandes ayant AU MOINS
  /// UNE ligne de ce vendeur, puis rend la commande ENTIÈRE — sans filtrer les
  /// lignes. Sur un panier multi-boutiques, le vendeur voyait donc les articles
  /// et les montants de ses concurrents. Le tri se fait ici, sur ce champ.
  final String sellerId;

  final String sku;
  final int quantity;
  final double unitPrice;
  final double lineTotal;

  /// NI NOM DE PRODUIT, NI PHOTO : `OrderLineSummary` N'EN PORTE AUCUN.
  ///
  /// Le modèle lisait `productName` et `imageUrl`, deux champs qui n'existent
  /// dans aucun contrat de order-service. `productName` retombait donc toujours
  /// sur le SKU, et `imageUrl` toujours sur `null` — le repli faisait tout le
  /// travail, sans que personne ne sache que c'était le seul cas.
  ///
  /// On assume : le SKU est ce que la commande porte réellement, et c'est aussi
  /// ce que le vendeur lit sur son bon de préparation. Résoudre le libellé
  /// exigerait un appel au catalogue PAR LIGNE, sur des produits qui peuvent
  /// avoir été supprimés depuis — et rendrait un nom d'aujourd'hui pour un achat
  /// d'hier, ce qui est pire qu'un code.
  String get label => sku.isEmpty ? 'Article' : sku;

  factory OrderLine.fromJson(Map d) => OrderLine(
        productId: Json.str(d['productId']),
        sellerId: Json.str(d['sellerId']),
        sku: Json.str(d['sku']),
        quantity: Json.asInt(d['quantity'], 1),
        unitPrice: Json.asDouble(d['finalUnitPrice']),
        lineTotal: Json.asDouble(d['lineTotal']),
      );
}

/// Adresse de livraison figée sur la commande (`OrderShippingAddressSummary`).
class ShippingAddress {
  ShippingAddress({
    required this.recipient,
    required this.phone,
    required this.communeName,
    required this.quartier,
    required this.landmark,
    required this.line1,
    required this.latitude,
    required this.longitude,
  });

  final String recipient;
  final String phone;

  /// Libellé résolu par le SERVEUR depuis le code figé sur la commande : l'app
  /// n'a pas besoin du référentiel pour afficher une commande déjà passée — ce
  /// qui tombe bien, aucune route ne le sert (cf. `shared/widgets/commune_field.dart`).
  final String communeName;

  final String quartier;

  /// Point de repère — c'est l'information que le coursier utilise en premier.
  final String landmark;

  final String line1;

  /// Position figée par l'acheteur au moment de la commande. `null` s'il ne l'a
  /// pas partagée : cas normal, pas une donnée manquante.
  final double? latitude;
  final double? longitude;

  bool get hasCoordinates => latitude != null && longitude != null;

  bool get hasContent =>
      recipient.isNotEmpty || landmark.isNotEmpty || communeName.isNotEmpty;

  /// Repère en tête : c'est ce qu'on lit à voix haute à un zem.
  String get summary => [landmark, quartier, line1, communeName]
      .where((s) => s.isNotEmpty)
      .join(', ');

  factory ShippingAddress.fromJson(Map d) => ShippingAddress(
        recipient: Json.str(d['recipient']),
        phone: Json.str(d['phone']),
        communeName: Json.str(d['communeName']),
        quartier: Json.str(d['quartier']),
        landmark: Json.str(d['landmark']),
        line1: Json.str(d['line1']),
        latitude: (d['latitude'] as num?)?.toDouble(),
        longitude: (d['longitude'] as num?)?.toDouble(),
      );
}

/// Statuts réellement émis par order-service (`OrderStatus`, sérialisé en
/// PascalCase par `order.Status.ToString()`).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LA MAQUETTE EN COMPTE HUIT, LE SERVEUR EN CONNAÎT HUIT — MAIS PAS LES MÊMES.
///
/// La maquette Express liste : Nouvelle, Acceptée, À préparer, En préparation,
/// Prête, En livraison, Livrée, Refusée. Aucun de ces mots n'est un statut de
/// commande côté HBA. Les valeurs réelles sont :
///
///   Pending, AwaitingPayment, Paid, Confirmed, Cancelled, Failed, Delivered,
///   UnderReview
///
/// Il n'y a AUCUN équivalent amont pour « À préparer », « Prête » ni « En
/// livraison » : ces étapes-là appartiennent au colis (module Shipping, jamais
/// extrait du monolithe) et à la course (delivery-service), pas à la commande.
/// « Acceptée » et « Refusée » n'existent que pour la RESTAURATION, dans
/// food-service, sur le ticket de cuisine.
///
/// Les filtres correspondants sont donc absents de l'écran plutôt que présents et
/// toujours vides. Les afficher grisés ferait chercher la condition qui les
/// rouvrirait ; les afficher actifs rendrait une liste vide qu'on lirait comme
/// « aucune commande à préparer ».
///
/// ET SURTOUT : UN VENDEUR NE PEUT FAIRE AVANCER AUCUNE COMMANDE.
///
/// order-service n'expose AUCUNE route de changement de statut par le vendeur —
/// ni accepter, ni préparer, ni expédier. Les transitions sont pilotées par
/// événements Kafka (paiement encaissé, course terminée). Aucun bouton d'action
/// ne doit donc apparaître sur une commande de marchandise : il ne pourrait que
/// tomber sur un 404.
/// ═════════════════════════════════════════════════════════════════════════════
class SellerOrderStatus {
  const SellerOrderStatus._();

  static const pending = 'Pending';
  static const awaitingPayment = 'AwaitingPayment';
  static const paid = 'Paid';
  static const confirmed = 'Confirmed';
  static const cancelled = 'Cancelled';
  static const failed = 'Failed';
  static const delivered = 'Delivered';
  static const underReview = 'UnderReview';

  /// Les seuls filtres qui ont un amont. L'ordre suit le parcours réel.
  static const List<String> all = [
    pending,
    awaitingPayment,
    paid,
    confirmed,
    delivered,
    underReview,
    cancelled,
    failed,
  ];

  /// Libellé vendeur. Traduit le vocabulaire du service, sans lui ajouter
  /// d'étape qu'il ne connaît pas.
  static String label(String status) {
    switch (status) {
      case pending:
        return 'En cours de création';
      case awaitingPayment:
        return 'En attente de paiement';
      case paid:
        return 'Payée';
      case confirmed:
        return 'Confirmée';
      case cancelled:
        return 'Annulée';
      case failed:
        return 'Échouée';
      case delivered:
        return 'Livrée';
      case underReview:
        return 'En arbitrage';
    }
    // Statut inconnu : on montre le code brut plutôt qu'un libellé inventé. Le
    // jour où order-service en ajoute un, le vendeur le voit, et nous aussi.
    return status.isEmpty ? '—' : status;
  }
}

/// Une commande reçue par le vendeur (`OrderSummary`).
class SellerOrder {
  SellerOrder({
    required this.id,
    required this.status,
    required this.kind,
    required this.createdAt,
    required this.currency,
    required this.grandTotal,
    required this.shippingFee,
    required this.lines,
    required this.myLines,
    required this.address,
    required this.reviewReason,
  });

  final String id;

  /// Valeur brute de `OrderStatus` (PascalCase) — cf. [SellerOrderStatus].
  final String status;

  /// `Goods` ou `Food`.
  final String kind;

  final DateTime? createdAt;
  final String currency;

  /// TOTAL DE LA COMMANDE ENTIÈRE, PAS DE LA PART DU VENDEUR.
  ///
  /// Sur un panier multi-boutiques, `grandTotal` inclut les lignes des autres
  /// vendeurs et les frais de livraison. Afficher ce chiffre comme « votre
  /// vente » gonflerait le chiffre d'affaires perçu. C'est [myTotal] qu'il faut
  /// montrer au vendeur — la somme de SES lignes.
  final double grandTotal;

  final double shippingFee;

  /// Toutes les lignes de la commande, tous vendeurs confondus.
  final List<OrderLine> lines;

  /// Les seules lignes de CE vendeur. C'est ce qu'il doit préparer et encaisser.
  final List<OrderLine> myLines;

  final ShippingAddress? address;

  /// Motif de mise en arbitrage, quand `status == UnderReview`. Sans lui, le
  /// vendeur voit « En arbitrage » sans savoir ce qui est reproché.
  final String? reviewReason;

  String get reference => id.length >= 8
      ? 'CMD-${id.replaceAll('-', '').substring(0, 8).toUpperCase()}'
      : 'CMD-$id';

  /// Nombre d'articles DE CE VENDEUR.
  ///
  /// LE CONTRAT NE PORTE PAS DE « NOMBRE D'ARTICLES » : on l'additionne
  /// depuis les quantités des lignes. C'est exact, contrairement à un compteur
  /// que le serveur ne calcule pas.
  int get itemCount => myLines.fold(0, (sum, l) => sum + l.quantity);

  /// Ce que ce vendeur a réellement vendu sur cette commande.
  double get myTotal => myLines.fold(0.0, (sum, l) => sum + l.lineTotal);

  /// IL N'Y A PAS DE STATUT DE PAIEMENT DISTINCT, ET IL N'Y EN A JAMAIS EU.
  ///
  /// Le modèle portait `paymentStatus`, lu sur un champ que `OrderSummary` n'a
  /// pas : il valait TOUJOURS la chaîne vide, donc `isPaid` était TOUJOURS faux.
  /// Un vendeur ne voyait jamais une commande comme encaissée, quelle qu'elle
  /// soit. Le paiement se lit sur le statut de la commande : `Paid` marque
  /// l'encaissement, `Confirmed` et `Delivered` le suivent.
  bool get isPaid =>
      status == SellerOrderStatus.paid ||
      status == SellerOrderStatus.confirmed ||
      status == SellerOrderStatus.delivered;

  /// AUCUN NOM DE CLIENT N'EST DISPONIBLE.
  ///
  /// `OrderSummary` ne rend que `BuyerId`, un GUID. Le modèle affichait
  /// « Client ABC123 » fabriqué à partir de ses six premiers caractères : un
  /// pseudo-nom qui ressemble à une donnée sans en être une. Le seul nom réel est
  /// celui du DESTINATAIRE de la livraison, quand l'acheteur a renseigné une
  /// adresse — et il est ici.
  String? get recipientName {
    final r = address?.recipient.trim() ?? '';
    return r.isEmpty ? null : r;
  }

  factory SellerOrder.fromJson(Map d, {required String sellerId}) {
    final lines = Json.list(d['lines']).map(OrderLine.fromJson).toList();

    return SellerOrder(
      id: Json.str(d['id']),
      status: Json.str(d['status']),
      kind: Json.str(d['kind'], 'Goods'),
      createdAt: Json.asDate(d['createdAtUtc']),
      currency: Json.str(d['currency'], AppConfig.defaultCurrency),
      grandTotal: Json.asDouble(d['grandTotal']),
      shippingFee: Json.asDouble(d['shippingFee']),
      lines: lines,
      myLines: lines.where((l) => l.sellerId == sellerId).toList(),
      address: d['shippingAddress'] is Map
          ? ShippingAddress.fromJson(Json.map(d['shippingAddress']))
          : null,
      reviewReason: (d['reviewReason']?.toString().isNotEmpty ?? false)
          ? d['reviewReason'].toString()
          : null,
    );
  }
}

/// Commandes reçues par le vendeur — order-service.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE N'EST PAS `/api/orders`, ET LA CONFUSION COÛTAIT CHER.
///
/// `GET /api/orders` est scopée par le `buyerId` du jeton : pour un vendeur, elle
/// rend SES PROPRES ACHATS. L'écran « Commandes » affichait donc, en toute
/// vraisemblance, une liste de commandes qui n'avaient rien à voir avec sa
/// boutique — et un vendeur sans achat personnel voyait un écran vide.
///
/// Les commandes REÇUES se lisent sur `GET /api/sellers/{sellerId}/orders`, dont
/// la route de passerelle (« seller-orders ») a été ajoutée par cette tâche : elle
/// n'existait pas, seul le BFF merchant atteignait le service en interne.
/// ═════════════════════════════════════════════════════════════════════════════
class OrdersApi extends ApiBase {
  const OrdersApi(super.dio);

  /// NI PAGINATION, NI RECHERCHE, NI PÉRIODE CÔTÉ SERVEUR.
  ///
  /// `ListBySellerAsync(Guid sellerId, …)` n'accepte AUCUN paramètre de requête :
  /// pas de `page`, pas de `pageSize`, pas de `status`, pas de `from`/`to`. Le
  /// dépôt rend tout l'historique, trié par date décroissante, sans `Take`.
  ///
  /// L'ancienne méthode envoyait `page`, `pageSize` et `search` « au cas où un
  /// BFF pas encore redéployé les ignorerait » : ils sont effectivement ignorés,
  /// mais silencieusement — l'écran croyait paginer et rechargeait la liste
  /// entière à chaque page. On ne les envoie donc plus du tout, et le filtrage
  /// est assumé comme LOCAL (voir [OrdersPagedNotifier]).
  ///
  /// À combler côté service : `?from=&to=&status=&page=`. Sur un vendeur actif,
  /// cette route deviendra ingérable.
  Future<List<SellerOrder>> orders(String sellerId) => guard(() async {
        final resp = await dio.get('/api/sellers/$sellerId/orders');
        return Json.list(resp.data)
            .map((e) => SellerOrder.fromJson(e, sellerId: sellerId))
            .toList();
      });
}

final ordersApiProvider = Provider<OrdersApi>((ref) => OrdersApi(ref.watch(dioProvider)));

/// Toutes les commandes du vendeur connecté.
final ordersProvider = FutureProvider<List<SellerOrder>>((ref) async {
  final sellerId = await ref.watch(requiredSellerIdProvider.future);
  return ref.watch(ordersApiProvider).orders(sellerId);
});

/// Une commande précise.
///
/// RÉSOLUE DANS LA LISTE, ET NON PAR `GET /api/orders/{id}`.
///
/// Cette route existe, mais `GetOrderQuery(id, buyerId)` la scope à l'ACHETEUR :
/// un vendeur qui la demande reçoit 404 sur sa propre commande. Il n'existe
/// aucune route de détail vendeur. Comme `/api/sellers/{id}/orders` rend déjà la
/// commande complète — lignes et adresse comprises — la chercher dans la liste
/// donne exactement la même donnée, sans requête supplémentaire ni 404.
final orderProvider = FutureProvider.family<SellerOrder, String>((ref, id) async {
  final orders = await ref.watch(ordersProvider.future);
  for (final o in orders) {
    if (o.id == id || o.reference == id) return o;
  }
  throw ApiException(
    "Cette commande n'est pas (ou plus) dans votre liste.",
    code: 'order.not_in_seller_scope',
  );
});

/// Liste « paginée » de l'écran Commandes.
///
/// LA PAGINATION EST UNE FEINTE, ET IL FAUT LE SAVOIR.
///
/// Le serveur ne pagine pas. La première page rend donc TOUT, et les suivantes
/// rien — ce qui suffit à faire taire le défilement infini sans mentir sur ce qui
/// existe. La recherche est appliquée LOCALEMENT sur la référence et le SKU des
/// lignes du vendeur : c'est le seul texte dont l'application dispose, faute de
/// nom de client et de nom de produit dans le contrat.
class OrdersPagedNotifier extends PagedNotifier<SellerOrder> {
  @override
  Future<List<SellerOrder>> fetch({
    required int page,
    required int pageSize,
    required String search,
  }) async {
    if (page > 1) return const [];

    final all = await ref.read(ordersProvider.future);
    final q = search.trim().toLowerCase();
    if (q.isEmpty) return all;

    return all
        .where((o) =>
            o.reference.toLowerCase().contains(q) ||
            o.myLines.any((l) => l.sku.toLowerCase().contains(q)))
        .toList();
  }
}

final ordersPagedProvider =
    NotifierProvider.autoDispose<OrdersPagedNotifier, PagedState<SellerOrder>>(
        OrdersPagedNotifier.new);
