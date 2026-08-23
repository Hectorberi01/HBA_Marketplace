import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_base.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';
import '../activities/activities_data.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// TABLEAUX DE BORD — LES DEUX BFF DE LA PASSERELLE.
///
/// CE QUE LA MAQUETTE MONTRE ET QUE PERSONNE NE CALCULE.
///
/// Quatre blocs du tableau de bord Express n'ont AUCUN amont, et il faut le lire
/// une fois pour toutes :
///
///   • « Performance · 7 jours » — aucune série temporelle n'existe. Elle était
///     calculée par le BFF vendeur du monolithe ; ni service HBA ni route BFF ne
///     la rend. C'est le module `analytics` déjà neutralisé en VEN2.
///   • « +12 % vs hier » — aucun delta J-1. `MerchantTodayDto` ne porte que le
///     JOUR COURANT : il n'y a pas de valeur d'hier à comparer.
///   • « Meilleures ventes » — personne n'agrège les ventes par produit. Ni
///     order-service, ni catalog-service, ni le BFF.
///   • « 5 produits presque en rupture » — `MerchantDashboardDto` n'a AUCUN bloc
///     stock, et le DTO le documente explicitement comme une absence voulue.
///     inventory-service sait répondre par SKU, mais rien ne balaie le catalogue
///     d'un vendeur pour en tirer une alerte.
///
/// Ces quatre cartes sont donc RETIRÉES des écrans, avec un commentaire à
/// l'endroit du retrait. Les garder sur des ratios inventés — c'est ce que
/// faisaient `expressWeekBars` et `expressBestSellers` — donnerait à un vendeur
/// des chiffres qu'il croirait siens.
///
/// CE QUI EXISTE, EN REVANCHE : le jour courant (commandes, chiffre, panier
/// moyen, commandes à traiter), les soldes, et les cinq dernières commandes.
/// C'est peu, mais c'est vrai.
/// ═════════════════════════════════════════════════════════════════════════════

/// Bloc « aujourd'hui » du tableau de bord boutique (`MerchantTodayDto`).
class MerchantToday {
  const MerchantToday({
    required this.ordersToday,
    required this.revenueToday,
    required this.averageBasket,
    required this.currency,
    required this.ordersToProcess,
  });

  final int ordersToday;
  final double revenueToday;

  /// `null` quand il n'y a eu AUCUNE commande aujourd'hui (le serveur garde la
  /// division). Afficher « 0 F CFA » de panier moyen laisserait croire à des
  /// ventes à zéro franc : c'est un tiret qu'il faut montrer.
  final double? averageBasket;

  /// `null` s'il n'y a ni commande du jour ni portefeuille. Ne pas retomber sur
  /// XOF en dur : une devise supposée sur un montant réel est un faux montant.
  final String? currency;

  /// Commandes aux statuts `Paid`, `Confirmed` ou `Preparing`.
  final int ordersToProcess;

  factory MerchantToday.fromJson(Map d) => MerchantToday(
        ordersToday: Json.asInt(d['ordersToday']),
        revenueToday: Json.asDouble(d['revenueToday']),
        averageBasket: d['averageBasket'] == null ? null : Json.asDouble(d['averageBasket']),
        currency: (d['currency']?.toString().isNotEmpty ?? false)
            ? d['currency'].toString()
            : null,
        ordersToProcess: Json.asInt(d['ordersToProcess']),
      );
}

/// Soldes tels que le BFF les agrège (`MerchantWalletDto` / `RestaurantWalletDto`).
class DashboardWallet {
  const DashboardWallet({
    required this.pendingBalance,
    required this.availableBalance,
    required this.pendingWithdrawal,
    required this.currency,
  });

  final double pendingBalance;
  final double availableBalance;
  final double pendingWithdrawal;
  final String currency;

  factory DashboardWallet.fromJson(Map d) => DashboardWallet(
        pendingBalance: Json.asDouble(d['pendingBalance']),
        availableBalance: Json.asDouble(d['availableBalance']),
        pendingWithdrawal: Json.asDouble(d['pendingWithdrawal']),
        currency: Json.str(d['currency'], AppConfig.defaultCurrency),
      );
}

/// En-tête de la boutique (`MerchantStoreDto`).
class MerchantStoreHeader {
  const MerchantStoreHeader({
    required this.id,
    required this.name,
    required this.logoUrl,
    required this.status,
    required this.isSelling,
    required this.contactPhone,
  });

  final String id;
  final String name;
  final String? logoUrl;
  final String status;

  /// La boutique accepte-t-elle des commandes ? C'est le seul « ouvert/fermé »
  /// que le contrat porte côté boutique.
  final bool isSelling;

  final String contactPhone;

  factory MerchantStoreHeader.fromJson(Map d) => MerchantStoreHeader(
        id: Json.str(d['id']),
        name: Json.str(d['name'], 'Ma boutique'),
        logoUrl:
            (d['logoUrl']?.toString().isNotEmpty ?? false) ? d['logoUrl'].toString() : null,
        status: Json.str(d['status']),
        isSelling: Json.asBool(d['isSelling']),
        contactPhone: Json.str(d['contactPhone']),
      );
}

/// Commande résumée du tableau de bord (`MerchantOrderDto`).
///
/// PLUS COURTE QUE `SellerOrder` : ni lignes, ni adresse, ni acheteur. C'est
/// une projection d'aperçu — cinq lignes, pas une liste de travail. Pour agir sur
/// une commande, l'écran Commandes est la bonne porte.
class DashboardOrder {
  const DashboardOrder({
    required this.id,
    required this.status,
    required this.grandTotal,
    required this.currency,
    required this.createdAt,
  });

  final String id;
  final String status;
  final double grandTotal;
  final String currency;
  final DateTime? createdAt;

  String get reference => id.length >= 8
      ? 'CMD-${id.replaceAll('-', '').substring(0, 8).toUpperCase()}'
      : 'CMD-$id';

  factory DashboardOrder.fromJson(Map d) => DashboardOrder(
        id: Json.str(d['id']),
        status: Json.str(d['status']),
        grandTotal: Json.asDouble(d['grandTotal']),
        currency: Json.str(d['currency'], AppConfig.defaultCurrency),
        createdAt: Json.asDate(d['createdAtUtc']),
      );
}

/// Tableau de bord d'une boutique (`MerchantDashboardDto`).
class MerchantDashboard {
  const MerchantDashboard({
    required this.store,
    required this.today,
    required this.wallet,
    required this.recentOrders,
  });

  final MerchantStoreHeader store;
  final MerchantToday today;

  /// `null` quand financial-service n'a pas répondu — un avertissement
  /// `{source: Financial}` accompagne alors l'enveloppe. Ne PAS afficher zéro :
  /// un vendeur qui lit « 0 F CFA disponible » appelle le support.
  final DashboardWallet? wallet;

  /// Les cinq dernières. Vide si order-service n'a pas répondu (avertissement
  /// `{source: Order}`) — d'où l'importance de lire les avertissements avant de
  /// conclure « aucune commande ».
  final List<DashboardOrder> recentOrders;

  factory MerchantDashboard.fromJson(Map d) => MerchantDashboard(
        store: MerchantStoreHeader.fromJson(Json.map(d['store'])),
        today: MerchantToday.fromJson(Json.map(d['today'])),
        wallet: d['wallet'] is Map ? DashboardWallet.fromJson(Json.map(d['wallet'])) : null,
        recentOrders: Json.list(d['recentOrders']).map(DashboardOrder.fromJson).toList(),
      );
}

/// En-tête restaurant (`RestaurantHeaderDto`).
class RestaurantHeader {
  const RestaurantHeader({
    required this.id,
    required this.name,
    required this.status,
    required this.role,
    required this.permissions,
  });

  final String id;
  final String name;
  final String status;
  final String role;

  /// CE QUI OUVRE OU FERME LES BOUTONS DE L'ÉCRAN.
  ///
  /// Le personnel d'un restaurant a des permissions nominatives
  /// (`restaurant.kitchen.manage`, `restaurant.menu.manage`…). Un cuisinier n'a
  /// pas à voir le bouton « Modifier la carte » : il recevrait 403.
  final List<String> permissions;

  bool can(String permission) => permissions.contains(permission);

  factory RestaurantHeader.fromJson(Map d) => RestaurantHeader(
        id: Json.str(d['id']),
        name: Json.str(d['name'], 'Mon restaurant'),
        status: Json.str(d['status']),
        role: Json.str(d['role']),
        permissions: (d['permissions'] is List)
            ? (d['permissions'] as List).map((e) => e.toString()).toList()
            : const <String>[],
      );
}

/// Codes de permission du personnel restaurant (`FoodPermissionCodes`).
class FoodPermission {
  const FoodPermission._();

  static const orderAccept = 'restaurant.order.accept';
  static const orderReject = 'restaurant.order.reject';
  static const menuManage = 'restaurant.menu.manage';
  static const staffManage = 'restaurant.staff.manage';
  static const kitchenManage = 'restaurant.kitchen.manage';
  static const settingsManage = 'restaurant.settings.manage';
  static const analyticsRead = 'restaurant.analytics.read';
}

/// Tableau de bord d'un restaurant (`RestaurantDashboardDto`).
class RestaurantDashboard {
  const RestaurantDashboard({
    required this.restaurant,
    required this.acceptsOrdersNow,
    required this.blockedReason,
    required this.wallet,
    required this.kitchenPending,
    required this.kitchenPreparing,
    required this.kitchenReady,
  });

  final RestaurantHeader restaurant;

  final bool acceptsOrdersNow;

  /// Chaîne VIDE quand rien ne bloque — non nullable dans le contrat. Valeurs
  /// possibles : `NotInService`, `Closed`, `Paused`, `NothingAvailable`…
  final String blockedReason;

  /// `null` DANS DEUX CAS INDISCERNABLES, ET AUCUN NE PRODUIT D'AVERTISSEMENT.
  ///
  /// Soit le membre n'a pas la permission de lire les finances (ou le restaurant
  /// n'a pas de vendeur de reversement rattaché) — l'appel n'est alors même pas
  /// émis ; soit financial-service est indisponible. Le BFF traite la dépendance
  /// comme optionnelle et n'émet AUCUN `warning` dans les deux cas. La carte des
  /// soldes est donc simplement masquée, sans message d'erreur : on ne sait pas
  /// laquelle des deux raisons annoncer.
  final DashboardWallet? wallet;

  /// Compteurs de la cuisine. CE SONT DES NOMBRES, PAS DES TICKETS : le bloc
  /// `kitchen` du tableau de bord ne contient jamais le détail des commandes.
  final int kitchenPending;
  final int kitchenPreparing;
  final int kitchenReady;

  factory RestaurantDashboard.fromJson(Map d) {
    final service = Json.map(d['service']);
    final kitchen = Json.map(d['kitchen']);
    return RestaurantDashboard(
      restaurant: RestaurantHeader.fromJson(Json.map(d['restaurant'])),
      acceptsOrdersNow: Json.asBool(service['acceptsOrdersNow']),
      blockedReason: Json.str(service['blockedReason']),
      wallet: d['wallet'] is Map ? DashboardWallet.fromJson(Json.map(d['wallet'])) : null,
      kitchenPending: Json.asInt(kitchen['pending']),
      kitchenPreparing: Json.asInt(kitchen['preparing']),
      kitchenReady: Json.asInt(kitchen['ready']),
    );
  }
}

class DashboardApi extends ApiBase {
  const DashboardApi(super.dio);

  /// EXIGE LE RÔLE `Seller`. 404 si la boutique n'appartient pas au vendeur du
  /// jeton — le BFF ne dit pas « interdit », il dit « inconnu », pour ne pas
  /// confirmer l'existence d'une boutique tierce.
  Future<BffResult<MerchantDashboard>> store(String storeId) => guard(() async {
        final resp = await dio.get('${AppConfig.bffMerchant}/stores/$storeId/dashboard');
        return BffResult.parse(resp.data, MerchantDashboard.fromJson);
      });

  /// EXIGE LE RÔLE `FoodPartner`, PAS `Seller`.
  ///
  /// Un compte qui n'a que l'un des deux reçoit 403 sur l'autre. Ce n'est pas une
  /// panne : c'est un partenaire qui n'exerce pas ce métier-là.
  Future<BffResult<RestaurantDashboard>> restaurant(String restaurantId) => guard(() async {
        final resp =
            await dio.get('${AppConfig.bffRestaurant}/restaurants/$restaurantId/dashboard');
        return BffResult.parse(resp.data, RestaurantDashboard.fromJson);
      });
}

final dashboardApiProvider =
    Provider<DashboardApi>((ref) => DashboardApi(ref.watch(dioProvider)));

final storeDashboardProvider =
    FutureProvider.family<BffResult<MerchantDashboard>, String>(
        (ref, storeId) => ref.watch(dashboardApiProvider).store(storeId));

final restaurantDashboardProvider =
    FutureProvider.family<BffResult<RestaurantDashboard>, String>(
        (ref, restaurantId) => ref.watch(dashboardApiProvider).restaurant(restaurantId));
