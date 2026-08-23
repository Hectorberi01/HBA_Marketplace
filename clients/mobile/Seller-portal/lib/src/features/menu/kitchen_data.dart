import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_base.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';
import '../activities/activities_data.dart';

/// Une ligne du ticket (`KitchenTicketItemDto`).
class KitchenItem {
  const KitchenItem({
    required this.id,
    required this.name,
    required this.quantity,
    required this.notes,
    required this.status,
    required this.stationId,
    required this.preparationMinutes,
    required this.options,
  });

  final String id;

  /// Nom FIGÉ à la commande. Renommer le plat après coup ne change pas le ticket
  /// déjà en cuisine — c'est voulu, et c'est pourquoi il ne faut pas résoudre le
  /// nom depuis la carte.
  final String name;

  final int quantity;
  final String? notes;
  final String status;
  final String? stationId;
  final int preparationMinutes;

  /// Déjà mises en forme par le serveur : « Taille : Grande ». Ne pas tenter de
  /// les recomposer — le contrat ne rend que la chaîne finale.
  final List<String> options;

  factory KitchenItem.fromJson(Map d) => KitchenItem(
        id: Json.str(d['id']),
        name: Json.str(d['name'], 'Article'),
        quantity: Json.asInt(d['quantity'], 1),
        notes: (d['notes']?.toString().isNotEmpty ?? false) ? d['notes'].toString() : null,
        status: Json.str(d['status']),
        stationId: (d['preparationStationId']?.toString().isNotEmpty ?? false)
            ? d['preparationStationId'].toString()
            : null,
        preparationMinutes: Json.asInt(d['preparationMinutes']),
        options: (d['options'] is List)
            ? (d['options'] as List).map((e) => e.toString()).toList()
            : const <String>[],
      );
}

/// Un ticket de cuisine (`KitchenTicketDto`).
class KitchenTicket {
  const KitchenTicket({
    required this.foodOrderId,
    required this.orderId,
    required this.status,
    required this.priority,
    required this.estimatedPreparationMinutes,
    required this.receivedAt,
    required this.elapsedSeconds,
    required this.customerNote,
    required this.otherStationsPending,
    required this.items,
  });

  final String foodOrderId;
  final String orderId;
  final String status;
  final int priority;
  final int? estimatedPreparationMinutes;
  final DateTime? receivedAt;

  /// Calculé par la passerelle, jamais négatif. C'est le chiffre qui compte en
  /// cuisine — plus lisible qu'une heure de réception.
  final int elapsedSeconds;

  final String? customerNote;
  final int otherStationsPending;
  final List<KitchenItem> items;

  /// AUCUN MONTANT N'APPARAÎT SUR UN TICKET, ET C'EST DÉLIBÉRÉ CÔTÉ SERVEUR.
  ///
  /// `KitchenTicketDto` n'a ni total, ni prix de ligne. Un poste de cuisine n'a
  /// pas à connaître le prix : il a des plats à préparer. La feuille de commande
  /// entrante de la maquette, qui affiche « Total 12 500 F CFA », n'a donc pas
  /// d'amont sur cet écran.
  String get reference => orderId.length >= 8
      ? '#${orderId.replaceAll('-', '').substring(0, 8).toUpperCase()}'
      : '#$orderId';

  factory KitchenTicket.fromJson(Map d) => KitchenTicket(
        foodOrderId: Json.str(d['foodOrderId']),
        orderId: Json.str(d['orderId']),
        status: Json.str(d['status']),
        priority: Json.asInt(d['priority']),
        estimatedPreparationMinutes: d['estimatedPreparationMinutes'] == null
            ? null
            : Json.asInt(d['estimatedPreparationMinutes']),
        receivedAt: Json.asDate(d['receivedAtUtc']),
        elapsedSeconds: Json.asInt(d['elapsedSeconds']),
        customerNote: (d['customerNote']?.toString().isNotEmpty ?? false)
            ? d['customerNote'].toString()
            : null,
        otherStationsPending: Json.asInt(d['otherStationsPending']),
        items: Json.list(d['items']).map(KitchenItem.fromJson).toList(),
      );
}

/// Un poste de préparation (`KitchenStationDto`).
class KitchenStation {
  const KitchenStation({required this.id, required this.name, required this.isActive});

  final String id;
  final String name;
  final bool isActive;

  factory KitchenStation.fromJson(Map d) => KitchenStation(
        id: Json.str(d['id']),
        name: Json.str(d['name']),
        isActive: Json.asBool(d['isActive']),
      );
}

/// L'écran de cuisine (`RestaurantKitchenDto`).
class KitchenBoard {
  const KitchenBoard({
    required this.restaurantId,
    required this.stations,
    required this.pending,
    required this.preparing,
    required this.ready,
  });

  final String restaurantId;

  /// TOUS LES POSTES, TOUJOURS — LE FILTRE PAR POSTE N'EST PAS EXPOSÉ.
  ///
  /// food-service accepte `?stationId=`, mais le BFF ne le transmet pas
  /// (`FoodClient.GetKitchenAsync` appelle sans query) : `stationId` de la
  /// réponse est TOUJOURS `null` et les trois seaux contiennent les tickets de
  /// tout l'établissement. Un sélecteur de poste à l'écran serait donc un
  /// sélecteur qui ne filtre rien.
  final List<KitchenStation> stations;

  final List<KitchenTicket> pending;
  final List<KitchenTicket> preparing;
  final List<KitchenTicket> ready;

  factory KitchenBoard.fromJson(Map d) => KitchenBoard(
        restaurantId: Json.str(d['restaurantId']),
        stations: Json.list(d['stations']).map(KitchenStation.fromJson).toList(),
        pending: Json.list(d['pending']).map(KitchenTicket.fromJson).toList(),
        preparing: Json.list(d['preparing']).map(KitchenTicket.fromJson).toList(),
        ready: Json.list(d['ready']).map(KitchenTicket.fromJson).toList(),
      );
}

/// Motifs de refus acceptés (`FoodRejectionReason`). Valeurs EXACTES : un motif
/// inconnu rend 400 `food.order.invalid_rejection_reason`.
const kRejectionReasons = <({String value, String label})>[
  (value: 'OutOfStock', label: 'Ingrédient épuisé'),
  (value: 'KitchenOverloaded', label: 'Cuisine saturée'),
  (value: 'Closing', label: 'Fermeture imminente'),
  (value: 'ItemUnavailable', label: 'Plat indisponible'),
  (value: 'TechnicalProblem', label: 'Problème technique'),
  (value: 'Other', label: 'Autre motif'),
];

/// ═════════════════════════════════════════════════════════════════════════════
/// ÉCRAN DE CUISINE — LECTURE PAR LE BFF, ACTIONS EN DIRECT SUR food-service.
///
/// DEUX AMONTS POUR UN SEUL ÉCRAN, ET C'EST NORMAL.
///
/// La lecture passe par `GET /api/v1/bff/restaurant/restaurants/{id}/kitchen`,
/// qui répartit les tickets en trois seaux et calcule le temps écoulé. Les
/// actions, elles, n'ont pas d'entrée BFF : le contrôleur n'a AUCUN `[HttpPost]`.
/// Elles passent en proxy sur `/api/food/partner/…` et répondent 204 sans corps —
/// il faut donc relire le tableau après chaque geste.
///
/// CORRIGÉ (VEN5-a) : ACCEPTER ET REFUSER SONT DÉSORMAIS GARDÉES.
///
/// Ce commentaire signalait que `accept` et `reject` étaient les deux seules
/// routes partenaire à ne PAS appeler `DenyUnlessStaffAsync` — tout compte
/// authentifié connaissant les deux GUID pouvait accepter ou refuser la commande
/// de n'importe quel établissement. La garde a été posée, avec les permissions
/// `restaurant.order.accept` et `restaurant.order.reject`, qui existaient dans
/// `FoodPermission` depuis le §8 sans être réclamées nulle part.
///
/// `preparing` et `ready` exigent, elles, `restaurant.kitchen.manage` — un
/// caissier accepte, un cuisinier prépare.
///
/// LA MAQUETTE ANNONCE « À VALIDER SOUS 3 MINUTES ». RIEN NE L'APPLIQUE.
///
/// Le domaine Food ne porte aucune échéance d'acceptation : ni date limite, ni
/// service qui expire une commande non acceptée. Un compte à rebours à l'écran
/// atteindrait zéro sans que rien ne se produise — promesse à ne pas faire.
///
/// LA LISTE DES COMMANDES EN ATTENTE EXISTE DÉSORMAIS (tâche #227).
///
/// `ListPendingFoodOrdersQuery` et `GetFoodOrderQuery` vivaient dans
/// food-service, avec gestionnaire et projection, SANS AUCUN APPELANT : le
/// commentaire de la première citait pourtant la route du cahier et prévenait que
/// sans elle « l'acceptation serait un bouton sans liste ». C'est exactement ce
/// qui s'est passé pendant deux phases. `GET /restaurants/{id}/orders` les expose
/// enfin, sous la permission `restaurant.order.accept`.
/// ═════════════════════════════════════════════════════════════════════════════

/// Une commande REÇUE, en attente de la décision du restaurant.
///
/// ═══════════════════════════════════════════════════════════════════════════════
/// CE N'EST PAS UN `KitchenTicket`, ET LES CONFONDRE SERAIT UNE FAUTE.
///
/// Un ticket de cuisine n'existe qu'APRÈS acceptation : `GetKitchenBoardQuery`
/// écarte explicitement `PendingRestaurantAcceptance`, pour que personne ne
/// commence un plat que le restaurant n'a pas accepté. Les deux listes sont donc
/// distinctes par nature — l'une décide, l'autre exécute — et c'est pourquoi il
/// fallait une route de plus plutôt qu'un filtre sur le tableau existant.
///
/// AUCUNE ÉCHÉANCE N'EST AFFICHÉE, MÊME SI LA MAQUETTE EN PROMET UNE.
///
/// La maquette annonce « à valider sous 3 minutes ». Le domaine Food ne porte NI
/// date limite NI service qui expire une commande non acceptée : un compte à
/// rebours atteindrait zéro sans que rien ne se produise. On montre le temps
/// ÉCOULÉ, qui est un fait, plutôt qu'un délai restant, qui serait une promesse.
/// ═══════════════════════════════════════════════════════════════════════════════
class PendingFoodOrder {
  const PendingFoodOrder({
    required this.id,
    required this.orderId,
    required this.total,
    required this.currency,
    required this.customerNote,
    required this.receivedAt,
    required this.lines,
  });

  final String id;
  final String orderId;
  final double total;
  final String currency;
  final String? customerNote;

  /// VENU DU SERVEUR, jamais de l'horloge du téléphone : c'est `ReceivedAtUtc`
  /// qui fait foi, et un appareil déréglé afficherait n'importe quoi.
  final DateTime receivedAt;

  /// « 2 × Poulet braisé », déjà mis en forme : le restaurateur décide sur ce
  /// qu'il doit préparer, pas sur des identifiants.
  final List<String> lines;

  /// Temps écoulé depuis la réception.
  Duration get attente => DateTime.now().toUtc().difference(receivedAt);

  /// SEUIL D'ALERTE PUREMENT VISUEL, ET IL N'ENGAGE RIEN. Cinq minutes est le
  /// moment où un client commence à annuler ; rien côté serveur n'y est attaché.
  bool get tarde => attente.inMinutes >= 5;

  factory PendingFoodOrder.fromJson(Map d) => PendingFoodOrder(
        id: Json.str(d['id']),
        orderId: Json.str(d['orderId']),
        total: Json.asDouble(d['total']),
        currency: Json.str(d['currency'], AppConfig.defaultCurrency),
        customerNote: (d['customerNote']?.toString().isNotEmpty ?? false)
            ? d['customerNote'].toString()
            : null,
        receivedAt: Json.asDate(d['receivedAtUtc']) ?? DateTime.now().toUtc(),
        lines: Json.list(d['items'])
            .map((i) {
              final m = Json.map(i);
              final q = Json.asInt(m['quantity']);
              final nom = Json.str(m['nameSnapshot'], 'Article');
              return q > 1 ? '$q × $nom' : nom;
            })
            .toList(),
      );
}

class KitchenApi extends ApiBase {
  const KitchenApi(super.dio);

  static const _partner = '${AppConfig.food}/partner/restaurants';

  Future<BffResult<KitchenBoard>> board(String restaurantId) => guard(() async {
        final resp =
            await dio.get('${AppConfig.bffRestaurant}/restaurants/$restaurantId/kitchen');
        return BffResult.parse(resp.data, KitchenBoard.fromJson);
      });

  /// Les commandes en attente de décision, les plus anciennes d'abord.
  ///
  /// CE N'EST PAS UNE ROUTE DE BFF : elle vient directement de food-service,
  /// contrairement au tableau de cuisine. Rien à agréger ici — une seule source,
  /// donc aucune raison d'ajouter un intermédiaire qui pourrait tomber.
  Future<List<PendingFoodOrder>> pending(String restaurantId) => guard(() async {
        final resp = await dio.get('$_partner/$restaurantId/orders');
        return Json.list(resp.data).map(PendingFoodOrder.fromJson).toList();
      });

  Future<void> accept(String restaurantId, String foodOrderId) => guard(() async {
        await dio.post('$_partner/$restaurantId/orders/$foodOrderId/accept');
      });

  /// [reason] doit être une valeur de [kRejectionReasons].
  Future<void> reject(
    String restaurantId,
    String foodOrderId, {
    required String reason,
    String? comment,
  }) =>
      guard(() async {
        await dio.post(
          '$_partner/$restaurantId/orders/$foodOrderId/reject',
          data: {'reason': reason, 'comment': comment},
        );
      });

  Future<void> startPreparing(String restaurantId, String foodOrderId) => guard(() async {
        await dio.post('$_partner/$restaurantId/orders/$foodOrderId/preparing');
      });

  Future<void> markReady(String restaurantId, String foodOrderId) => guard(() async {
        await dio.post('$_partner/$restaurantId/orders/$foodOrderId/ready');
      });

  /// LA GRANULARITÉ LIGNE N'EXISTE PAS EN HTTP.
  ///
  /// `StartKitchenItemCommand`, `MarkKitchenItemReadyCommand` et
  /// `ReopenKitchenItemCommand` sont écrites et n'ont aucune route : on ne peut
  /// pas cocher un plat, seulement le ticket entier. Un écran de cuisine
  /// multi-postes en aura besoin.
}

final kitchenApiProvider = Provider<KitchenApi>((ref) => KitchenApi(ref.watch(dioProvider)));

final kitchenBoardProvider = FutureProvider.family<BffResult<KitchenBoard>, String>(
    (ref, restaurantId) => ref.watch(kitchenApiProvider).board(restaurantId));

/// Les commandes en attente d'acceptation d'un restaurant.
///
/// SÉPARÉ DE `kitchenBoardProvider`, ET NON FUSIONNÉ AVEC LUI.
///
/// Les deux se rafraîchissent au même rythme mais n'ont pas la même conséquence :
/// une erreur sur la file d'acceptation ne doit pas vider l'écran de cuisine, où
/// des plats sont en cours. Les fondre en un seul fournisseur ferait tomber les
/// deux ensemble.
final pendingOrdersProvider = FutureProvider.family<List<PendingFoodOrder>, String>(
    (ref, restaurantId) => ref.watch(kitchenApiProvider).pending(restaurantId));
