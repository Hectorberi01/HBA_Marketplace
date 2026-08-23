import 'package:flutter_test/flutter_test.dart';
import 'package:hba_express_pro/src/features/activities/activities_data.dart';
import 'package:hba_express_pro/src/features/dashboard/dashboard_data.dart';

/// Parsing des tableaux de bord partenaire, tels que les DEUX BFF les rendent.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// `SellerKpis` ET `SalesReport` ONT DISPARU. LEUR SORT N'EST PAS LE MÊME.
///
/// `SellerKpis` était la projection du BFF vendeur du monolithe : un objet plat
/// mêlant commandes, ventes à 30 jours, avis et répartition par statut. Les BFF
/// HBA rendent la même matière sous une autre forme — `MerchantDashboardDto`,
/// découpé en `store` / `today` / `wallet` / `recentOrders`. Les tests de parsing
/// sont donc REPRIS ci-dessous contre ces blocs.
///
/// `SalesReport`, en revanche, n'a AUCUN équivalent : la série temporelle des
/// ventes était calculée par le BFF du monolithe, et ni service HBA ni route BFF
/// ne l'agrège (module `analytics` de `core/network/not_migrated.dart`). Ses deux
/// tests sont supprimés plutôt que réécrits : il n'y a rien à parser.
///
/// Ce qui manque exactement côté serveur, pour le jour du rebranchement :
///   • une agrégation `ordersByStatus` — personne ne compte les commandes par
///     statut ; `MerchantTodayDto` ne porte que `ordersToProcess` ;
///   • une commission dérivée (brut − net) — `MerchantTodayDto` rend le chiffre
///     d'affaires du jour, pas la part plateforme ;
///   • une série `snapshots[]` par période — c'est le module `analytics`.
/// ═════════════════════════════════════════════════════════════════════════════
void main() {
  group('MerchantToday.fromJson', () {
    test('parse le jour courant', () {
      final t = MerchantToday.fromJson({
        'ordersToday': 5,
        'revenueToday': 125000,
        'averageBasket': 25000,
        'currency': 'XOF',
        'ordersToProcess': 2,
      });

      expect(t.ordersToday, 5);
      expect(t.revenueToday, 125000.0);
      expect(t.averageBasket, 25000.0);
      expect(t.currency, 'XOF');
      expect(t.ordersToProcess, 2);
    });

    test('aucune commande — panier moyen NUL, pas zéro', () {
      // Le serveur garde la division : `averageBasket` arrive `null`. Le retomber
      // sur 0 ferait lire « 0 F CFA de panier moyen », c'est-à-dire des ventes à
      // zéro franc, là où il faut afficher un tiret.
      final t = MerchantToday.fromJson({'ordersToday': 0, 'revenueToday': 0});

      expect(t.ordersToday, 0);
      expect(t.averageBasket, isNull);
    });

    test('devise absente — NULLE, jamais supposée', () {
      // Retomber sur XOF en dur mettrait une devise inventée sur un montant réel.
      final t = MerchantToday.fromJson({'ordersToday': 3, 'revenueToday': 9000});

      expect(t.currency, isNull);
    });
  });

  group('MerchantDashboard.fromJson', () {
    Map<String, dynamic> dashboardJson() => {
          'store': {
            'id': 'S1',
            'name': 'Ma Super Boutique',
            'logoUrl': 'https://cdn.test/logo.png',
            'status': 'Active',
            'isSelling': true,
            'contactPhone': '+229 01 97 24 18 06',
          },
          'today': {
            'ordersToday': 4,
            'revenueToday': 60000,
            'averageBasket': 15000,
            'currency': 'XOF',
            'ordersToProcess': 1,
          },
          'wallet': {
            'pendingBalance': 30000,
            'availableBalance': 45000,
            'pendingWithdrawal': 10000,
            'currency': 'XOF',
          },
          'recentOrders': [
            {
              'id': '0a1b2c3d-4e5f-6789-abcd-ef0123456789',
              'status': 'Confirmed',
              'grandTotal': 25000,
              'currency': 'XOF',
              'createdAtUtc': '2026-07-20T10:30:00Z',
            },
          ],
        };

    test('parse les quatre blocs, référence de commande comprise', () {
      final d = MerchantDashboard.fromJson(dashboardJson());

      expect(d.store.name, 'Ma Super Boutique');
      expect(d.store.isSelling, isTrue);
      expect(d.today.ordersToProcess, 1);
      expect(d.wallet?.availableBalance, 45000.0);
      expect(d.recentOrders, hasLength(1));
      expect(d.recentOrders.first.reference, 'CMD-0A1B2C3D');
    });

    test('financial-service muet — wallet NUL, et surtout pas zéro', () {
      // Le BFF omet le bloc et joint un avertissement `{source: Financial}`. Un
      // vendeur qui lit « 0 F CFA disponible » alors qu'il a 45 000 F appelle le
      // support : c'est l'absence qu'il faut afficher, pas un solde.
      final json = dashboardJson()..remove('wallet');
      final d = MerchantDashboard.fromJson(json);

      expect(d.wallet, isNull);
      expect(d.today.ordersToday, 4, reason: 'le reste du tableau de bord tient');
    });

    test('order-service muet — liste vide, à lire avec les avertissements', () {
      final json = dashboardJson()..remove('recentOrders');
      final d = MerchantDashboard.fromJson(json);

      expect(d.recentOrders, isEmpty);
    });
  });

  group('BffResult.parse', () {
    test('sépare la donnée des avertissements de dégradation', () {
      // C'est l'enveloppe qui distingue « rien à afficher » de « une dépendance
      // n'a pas répondu ». Sans elle, une liste vide se lit comme une absence de
      // commandes, alors que c'est order-service qui n'a pas répondu.
      final result = BffResult.parse({
        'data': {
          'store': {'id': 'S1', 'name': 'Boutique', 'status': 'Active', 'isSelling': true, 'contactPhone': ''},
          'today': {'ordersToday': 0, 'revenueToday': 0, 'ordersToProcess': 0},
          'recentOrders': [],
        },
        'warnings': [
          {'source': 'Order', 'code': 'SERVICE_UNAVAILABLE'},
        ],
      }, MerchantDashboard.fromJson);

      expect(result.isPartial, isTrue);
      expect(result.warnings, hasLength(1));
      expect(result.warnings.first.isUnavailable, isTrue);
      // Le message nomme le SUJET, pas le microservice : « vos dernières
      // commandes », et non « order-service ».
      expect(result.warnings.first.message, contains('commandes'));
      expect(result.data.recentOrders, isEmpty);
    });

    test('sans avertissement, le résultat est complet', () {
      final result = BffResult.parse({
        'data': {
          'store': {'id': 'S1', 'name': 'Boutique', 'status': 'Active', 'isSelling': true, 'contactPhone': ''},
          'today': {'ordersToday': 0, 'revenueToday': 0, 'ordersToProcess': 0},
          'recentOrders': [],
        },
      }, MerchantDashboard.fromJson);

      expect(result.isPartial, isFalse);
    });
  });

  group('RestaurantDashboard.fromJson', () {
    test('parse les blocs imbriqués service / kitchen et les permissions', () {
      final d = RestaurantDashboard.fromJson({
        'restaurant': {
          'id': 'R1',
          'name': 'Chez Awa',
          'status': 'Approved',
          'role': 'Cook',
          'permissions': ['restaurant.kitchen.manage'],
        },
        'service': {'acceptsOrdersNow': false, 'blockedReason': 'Closed'},
        'kitchen': {'pending': 3, 'preparing': 2, 'ready': 1},
      });

      expect(d.restaurant.name, 'Chez Awa');
      expect(d.acceptsOrdersNow, isFalse);
      expect(d.blockedReason, 'Closed');
      expect(d.kitchenPending, 3);
      expect(d.kitchenReady, 1);

      // Les permissions ouvrent ou ferment les boutons : un cuisinier qui verrait
      // « Modifier la carte » recevrait un 403 qu'il ne pourrait pas s'expliquer.
      expect(d.restaurant.can(FoodPermission.kitchenManage), isTrue);
      expect(d.restaurant.can(FoodPermission.menuManage), isFalse);
    });

    test('rien ne bloque — blockedReason est la chaîne VIDE, pas null', () {
      // Le contrat le déclare non nullable : tester `!= null` laisserait passer
      // un bandeau « service bloqué » vide sur un restaurant parfaitement ouvert.
      final d = RestaurantDashboard.fromJson({
        'restaurant': {'id': 'R1', 'name': 'Chez Awa', 'status': 'Approved', 'role': 'Owner'},
        'service': {'acceptsOrdersNow': true},
        'kitchen': {},
      });

      expect(d.acceptsOrdersNow, isTrue);
      expect(d.blockedReason, isEmpty);
      expect(d.kitchenPending, 0);
    });
  });
}
