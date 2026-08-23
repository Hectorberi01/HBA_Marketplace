import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http_mock_adapter/http_mock_adapter.dart';

import 'package:hba_express_pro/src/features/orders/orders_data.dart';
import 'package:hba_express_pro/src/core/network/api_exception.dart';

/// Flux critique n°2 — COMMANDES REÇUES (`GET /api/sellers/{sellerId}/orders`).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// TROIS CHANGEMENTS DE FOND DEPUIS LA VERSION PRÉCÉDENTE DE CE FICHIER.
///
///   • LE CHEMIN. `GET /seller/orders/` visait le BFF du monolithe. `/api/orders`
///     n'est pas davantage la bonne porte : elle est scopée par le `buyerId` du
///     jeton et rend les ACHATS du vendeur. Les commandes reçues se lisent sur
///     `/api/sellers/{sellerId}/orders`, d'où l'argument désormais exigé par
///     `OrdersApi.orders`.
///
///   • `paymentStatus` N'EXISTE PAS. `OrderSummary` ne porte pas de statut de
///     paiement distinct : le champ que le test fabriquait rendait `isPaid`
///     toujours faux dans l'application réelle. Le paiement se lit sur le statut
///     de la commande (`Paid`, `Confirmed`, `Delivered`).
///
///   • LE VENDEUR EST DANS LA LIGNE. `ListBySellerAsync` rend la commande
///     ENTIÈRE dès qu'une ligne appartient au vendeur : le tri se fait côté
///     application sur `OrderLine.sellerId`. C'est ce que vérifie le test
///     « panier multi-boutiques » ci-dessous, et c'est ce qui empêche un vendeur
///     de lire les articles et les montants de ses concurrents.
/// ═════════════════════════════════════════════════════════════════════════════
void main() {
  late Dio dio;
  late DioAdapter adapter;

  // Le vendeur connecté. Résolu par `requiredSellerIdProvider` dans
  // l'application ; passé en clair ici, puisqu'on teste la couche API seule.
  const sellerId = 'be11e5e1-0000-0000-0000-000000000001';
  const otherSellerId = 'be11e5e1-0000-0000-0000-000000000002';
  const path = '/api/sellers/$sellerId/orders';

  setUp(() {
    dio = Dio(BaseOptions(baseUrl: 'https://test.local'));
    adapter = DioAdapter(dio: dio);
  });

  // Une commande encaissée complète, réutilisée par plusieurs tests.
  Map<String, dynamic> paidOrderJson() => {
        'id': '0a1b2c3d-4e5f-6789-abcd-ef0123456789',
        'status': 'Confirmed',
        'kind': 'Goods',
        'createdAtUtc': '2026-07-20T10:30:00Z',
        'currency': 'XOF',
        'grandTotal': 25000,
        'shippingFee': 1500,
        'lines': [
          {
            'productId': 'P1',
            'sellerId': sellerId,
            'sku': 'SKU-1',
            'quantity': 2,
            'finalUnitPrice': 5000,
            'lineTotal': 10000,
          },
          {
            'productId': 'P2',
            'sellerId': sellerId,
            'sku': 'SKU-2',
            'quantity': 3,
            'finalUnitPrice': 5000,
            'lineTotal': 15000,
          },
        ],
        // Adresse au format béninois : commune (libellé résolu par le serveur
        // depuis le code figé sur la commande), quartier, point de repère.
        // Ni ville libre, ni « line2 » : le serveur ne les envoie plus.
        'shippingAddress': {
          'recipient': 'Awa Koné',
          'phone': '+229 01 97 24 18 06',
          'communeName': 'Cotonou',
          'quartier': 'Fidjrossè',
          'landmark': 'en face de la pharmacie Sainte-Rita',
          'line1': 'Rue 12',
          'latitude': 6.3703,
          'longitude': 2.3912,
        },
      };

  test('liste — parse, référence lisible, total articles, adresse', () async {
    adapter.onGet(path, (server) => server.reply(200, [paidOrderJson()]));

    final orders = await OrdersApi(dio).orders(sellerId);

    expect(orders, hasLength(1));
    final o = orders.first;
    expect(o.reference, 'CMD-0A1B2C3D');
    expect(o.itemCount, 5, reason: 'somme des quantités (2 + 3), pas le nombre de lignes');
    expect(o.grandTotal, 25000);
    expect(o.address?.communeName, 'Cotonou');
    expect(o.address?.landmark, 'en face de la pharmacie Sainte-Rita');

    // ASSERTION DÉPLACÉE, PAS AFFAIBLIE : `expect(o.customer, 'Awa Koné')`.
    //
    // `SellerOrder.customer` n'existe plus, et le champ `customer` que ce test
    // fabriquait n'est dans aucun contrat de order-service. `OrderSummary` ne
    // rend que `BuyerId`, un GUID : l'ancien modèle en tirait « Client ABC123 »,
    // un pseudo-nom qui ressemblait à une donnée sans en être une.
    //
    // Le SEUL nom réel est celui du destinataire de la livraison, quand
    // l'acheteur a renseigné une adresse. C'est lui qu'on vérifie ici.
    expect(o.recipientName, 'Awa Koné');

    // Le repère vient EN TÊTE du résumé : c'est ce qu'on lit à voix haute à un
    // zem, et la seule information qui permette réellement de trouver la porte.
    expect(o.address?.summary, startsWith('en face de la pharmacie Sainte-Rita'));
    expect(o.address?.summary, contains('Cotonou'));

    expect(o.address?.hasCoordinates, isTrue);
  });

  test('adresse — sans coordonnées, hasCoordinates est faux', () async {
    // Cas NORMAL, pas une erreur : commande antérieure à la refonte, ou acheteur
    // qui n'a pas partagé sa position. La position est facultative de bout en bout.
    final json = paidOrderJson();
    // `Map.from` plutôt qu'un transtypage : le littéral imbriqué est inféré
    // Map<String, Object>, et le figer dans un `as Map<String, dynamic>` casserait
    // au premier ajout d'une valeur nulle au gabarit.
    final addr = Map<String, dynamic>.from(json['shippingAddress'] as Map)
      ..['latitude'] = null
      ..['longitude'] = null;
    json['shippingAddress'] = addr;
    adapter.onGet(path, (server) => server.reply(200, [json]));

    final orders = await OrdersApi(dio).orders(sellerId);

    expect(orders.first.address?.hasCoordinates, isFalse);
    expect(orders.first.address?.hasContent, isTrue,
        reason: 'une adresse sans position reste parfaitement exploitable');
  });

  test('isPaid — VRAI seulement sur Paid / Confirmed / Delivered', () async {
    adapter.onGet(
      path,
      (server) => server.reply(200, [
        paidOrderJson(),
        {
          ...paidOrderJson(),
          'id': '11111111-2222-3333-4444-555555555555',
          'status': 'AwaitingPayment',
        },
        {
          ...paidOrderJson(),
          'id': '22222222-3333-4444-5555-666666666666',
          'status': 'Cancelled',
        },
      ]),
    );

    final orders = await OrdersApi(dio).orders(sellerId);

    expect(orders[0].isPaid, isTrue, reason: 'status = Confirmed');
    expect(orders[1].isPaid, isFalse,
        reason: "status = AwaitingPayment → ne PAS autoriser l'expédition");
    expect(orders[2].isPaid, isFalse, reason: 'status = Cancelled');
  });

  test('panier multi-boutiques — le vendeur ne voit QUE ses lignes', () async {
    // La route rend la commande entière dès qu'une ligne appartient au vendeur.
    // Sans le tri sur `OrderLine.sellerId`, le vendeur lirait les articles et les
    // montants de ses concurrents, et son chiffre d'affaires serait gonflé du
    // total de la commande.
    final json = paidOrderJson();
    json['lines'] = [
      ...(json['lines'] as List),
      {
        'productId': 'P9',
        'sellerId': otherSellerId,
        'sku': 'SKU-CONCURRENT',
        'quantity': 4,
        'finalUnitPrice': 9000,
        'lineTotal': 36000,
      },
    ];
    adapter.onGet(path, (server) => server.reply(200, [json]));

    final o = (await OrdersApi(dio).orders(sellerId)).first;

    expect(o.lines, hasLength(3), reason: 'la commande entière est bien reçue');
    expect(o.myLines, hasLength(2), reason: 'seules les lignes de ce vendeur');
    expect(o.myLines.map((l) => l.sku), isNot(contains('SKU-CONCURRENT')));
    expect(o.itemCount, 5, reason: 'les 4 articles du concurrent ne comptent pas');
    expect(o.myTotal, 25000, reason: 'la part du vendeur, pas le grandTotal (61 000)');
  });

  test('ligne — le libellé retombe sur le SKU, faute de nom de produit', () async {
    // `OrderLineSummary` ne porte ni `productName` ni `imageUrl` : le SKU est ce
    // que la commande contient réellement, et ce que le vendeur lit sur son bon
    // de préparation.
    adapter.onGet(path, (server) => server.reply(200, [paidOrderJson()]));

    final o = (await OrdersApi(dio).orders(sellerId)).first;

    expect(o.myLines.first.label, 'SKU-1');
  });

  // TEST SUPPRIMÉ : « détail — GET /seller/orders/{id} parse un objet unique ».
  //
  // `OrdersApi.order(id)` n'existe plus, et ce n'est pas un renommage : il
  // n'existe AUCUNE route de détail vendeur. `GET /api/orders/{id}` existe mais
  // `GetOrderQuery(id, buyerId)` la scope à l'ACHETEUR — un vendeur qui la
  // demande reçoit 404 sur sa propre commande.
  //
  // Comme `/api/sellers/{id}/orders` rend déjà la commande complète (lignes et
  // adresse comprises), le détail est résolu DANS la liste par `orderProvider`.
  // Le couvrir demanderait un `ProviderContainer` avec `dioProvider` et
  // `requiredSellerIdProvider` surchargés : c'est un test de fournisseurs, pas
  // de couche API, et il n'a pas sa place dans ce fichier.

  test('statut inconnu — libellé brut plutôt qu\'un mot inventé', () async {
    // Le jour où order-service ajoute un statut, le vendeur le voit, et nous
    // aussi. Un repli sur « En cours » masquerait l'ajout.
    expect(SellerOrderStatus.label('Escrowed'), 'Escrowed');
    expect(SellerOrderStatus.label('Delivered'), 'Livrée');
    expect(SellerOrderStatus.label(''), '—');
  });

  test('erreur serveur — 500 remonte en ApiException (jamais DioException brute)', () async {
    adapter.onGet(path, (server) => server.reply(500, {'detail': 'Erreur serveur'}));

    await expectLater(OrdersApi(dio).orders(sellerId), throwsA(isA<ApiException>()));
  });
}
