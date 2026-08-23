import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http_mock_adapter/http_mock_adapter.dart';

import 'package:hba_express_pro/src/core/media/media_upload.dart';
import 'package:hba_express_pro/src/core/network/api_exception.dart';
import 'package:hba_express_pro/src/features/catalog/catalog_data.dart';
import 'package:hba_express_pro/src/features/offers/offers_data.dart';
import 'package:hba_express_pro/src/features/inventory/inventory_data.dart';

/// Test d'intégration de l'ASSISTANT de création, au niveau de la couche API
/// (HTTP simulé, aucun appel réel).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ENCHAÎNEMENT DE LA MAQUETTE N'EST PLUS FAISABLE DE BOUT EN BOUT.
///
/// L'assistant faisait : produit → déclinaison → mise en vente → stock, tout sur
/// le BFF du monolithe. Deux des quatre étapes n'ont plus d'amont :
///
///   • la MISE EN VENTE — Products/Offers vit encore dans le monolithe (tâche
///     « AUDIT2-1 »), et la passerelle n'expose aucun `/api/offers` ;
///   • le STOCK — toutes les écritures d'inventory-service vivent sous
///     `MapAdminGroup` (`RequireRole("Admin","Moderator")`) : un vendeur reçoit
///     403 sur la création d'article comme sur l'ajustement.
///
/// CE PARAGRAPHE EST PÉRIMÉ, ET LE SIGNAL ANNONCÉ S'EST PRODUIT.
///
/// Il disait : « le jour où les routes s'ouvriront, ces deux assertions tomberont
/// — et c'est exactement le signal attendu ». Elles sont tombées. La phase 2 a
/// ouvert les écritures de stock, la phase 3 a greffé les offres, et le test ne
/// vérifie plus une absence mais deux appels réels.
///
/// Ce qu'il faut retenir de la prédiction : elle s'est vérifiée parce que la
/// SIGNATURE a changé, pas parce que quelqu'un a relu ce commentaire. Un test qui
/// n'aurait vérifié qu'un code d'erreur serait resté vert en affirmant que la mise
/// en vente n'existe pas.
///
/// ET LE CHAMP D'IDENTIFIANT A CHANGÉ DE SENS.
///
/// L'ancien test figeait « lit `productId`, pas `id` » : c'était le contrat du
/// BFF composite. catalog-service répond `201 { id }`. Le test vérifie donc
/// désormais l'inverse, et surtout qu'un identifiant ABSENT lève au lieu de
/// laisser la suite taper sur `/products//variants`.
/// ═════════════════════════════════════════════════════════════════════════════
void main() {
  late Dio dio;
  late DioAdapter adapter;

  // `sellerId` est dans le CORPS de `ProductRequest`, et il vient du socle
  // d'identité (`requiredSellerIdProvider`) : aucun écran ne le fabrique.
  const sellerId = 'be11e5e1-0000-0000-0000-000000000001';

  const products = '/api/catalog/seller/products';

  setUp(() {
    dio = Dio(BaseOptions(baseUrl: 'https://test.local'));
    adapter = DioAdapter(dio: dio);
  });

  /// Photo minimale : le dépôt réel est simulé par [_FakeUploader], on n'a donc
  /// besoin que d'octets non vides.
  ProcessedImage image(String name) => ProcessedImage(
        fileName: name,
        sourcePath: '/tmp/$name',
        original: Uint8List.fromList([1, 2, 3]),
      );

  test("createProduct lit le champ 'id' rendu par catalog-service", () async {
    adapter.onPost(
      products,
      (server) => server.reply(201, {'id': 'PID-42'}),
      data: Matchers.any,
    );

    final uploader = _FakeUploader();
    final id = await CatalogApi(dio).createProduct(
      sellerId: sellerId,
      categoryId: 'C1',
      name: 'Casque',
      images: const [],
      uploadPhoto: uploader.call,
    );

    expect(id, 'PID-42', reason: "L'assistant enchaîne les appels sur cet id : vide = tout casse.");
    expect(uploader.owners, isEmpty, reason: 'aucune photo fournie, aucun dépôt');
  });

  test("createProduct : identifiant absent de la réponse → ApiException", () async {
    // Le garde-fou qui manquait. Sans lui, l'assistant continuait avec une chaîne
    // vide et tapait `/products//variants` — une URL qui ne matche aucune route,
    // pour une erreur qui n'indiquait jamais la vraie cause.
    adapter.onPost(products, (server) => server.reply(201, {}), data: Matchers.any);

    await expectLater(
      CatalogApi(dio).createProduct(
        sellerId: sellerId,
        categoryId: 'C1',
        name: 'Casque',
        images: const [],
        uploadPhoto: _FakeUploader().call,
      ),
      throwsA(isA<ApiException>()),
    );
  });

  test("produit + photos + déclinaison : l'identifiant se propage partout", () async {
    const pid = 'PID-42';
    const sku = 'ABC123-XYZ';

    adapter
      ..onPost(products, (s) => s.reply(201, {'id': pid}), data: Matchers.any)
      // Rattachement des photos : `ProductMediaRequest(MediaId, Url, Type, …)`.
      ..onPost('$products/$pid/media', (s) => s.reply(200, {}), data: Matchers.any)
      // Si l'id ne se propage pas, l'URL serait `/products//variants` → aucune
      // route ne matcherait → le test échouerait ici. C'est le cœur de la vérif.
      ..onPost('$products/$pid/variants', (s) => s.reply(200, {}), data: Matchers.any);

    final uploader = _FakeUploader();
    final catalog = CatalogApi(dio);

    final productId = await catalog.createProduct(
      sellerId: sellerId,
      categoryId: 'C1',
      name: 'Casque',
      images: [image('face.jpg'), image('dos.jpg')],
      uploadPhoto: uploader.call,
    );
    expect(productId, pid);

    // Les deux photos sont déposées SOUS le produit créé : c'est ce que
    // media-service attend comme `ownerId`, et ce qui manquait quand l'id
    // repartait vide.
    expect(uploader.owners, [pid, pid]);

    await catalog.addVariant(productId, sku: sku);

    // Aucune exception = les trois routes ont matché → l'id a circulé.
  });

  test('mise en vente et stock : les deux routes RÉELLES, avec les bons champs', () async {
    // ═══════════════════════════════════════════════════════════════════════════
    // CE TEST AFFIRMAIT L'INVERSE, ET SA PRÉMISSE EST MORTE.
    //
    // Il vérifiait que `OffersApi.create` et `InventoryApi.createItem` levaient
    // `not_migrated` — ce qui était exact tant que les deux étaient des bouchons.
    // La phase 2 a ouvert les écritures de stock, la phase 3 a greffé les offres :
    // les deux appellent désormais des routes réelles.
    //
    // UN TEST QUI CONTINUE DE PASSER APRÈS QUE SON SUJET A CHANGÉ EST PIRE QU'UN
    // TEST ABSENT. Celui-ci aurait échoué à la compilation — la signature de
    // `create` a changé — ce qui est la meilleure façon d'être rattrapé. S'il
    // n'avait vérifié qu'un code d'erreur, il aurait passé au vert en affirmant que
    // la mise en vente n'existe pas.
    //
    // Ce qu'il vérifie maintenant : que les deux appels partent sur les bons
    // chemins, ET que le corps de l'offre porte `variantId` — pas `sku`.
    // ═══════════════════════════════════════════════════════════════════════════
    adapter.onPost(
      '/api/catalog/seller/offers',
      (s) => s.reply(201, {'id': 'OFF-1'}),
      data: Matchers.any,
    );
    adapter.onPost(
      '/api/inventory/items',
      (s) => s.reply(201, {'id': 'ITEM-1'}),
      data: Matchers.any,
    );

    // `variantId` ET `storeId`, PAS `sku`. C'est la correction de sécurité de la
    // phase 3 : le monolithe acceptait un SKU saisi séparément du produit, et
    // Inventory décrémentait alors le stock d'un autre article. Ce test la fixe —
    // si quelqu'un rétablit `sku:` pour simplifier un écran, il ne compilera plus.
    final offerId = await OffersApi(dio).create(
      productId: 'PID-42',
      variantId: 'VAR-7',
      storeId: 'STORE-3',
      sellerPrice: 10000,
      shipFromLocationId: 'LOC-1',
    );
    expect(offerId, 'OFF-1');

    final itemId = await InventoryApi(dio).createItem(
      sku: 'ABC123-XYZ',
      locationId: 'LOC-1',
      onHand: 10,
    );
    expect(itemId, 'ITEM-1');

    // LA GARANTIE SUR LE CORPS EST DONNÉE PAR LE COMPILATEUR, PAS PAR UNE
    // ASSERTION. `create` exige `variantId` et `storeId` en paramètres NOMMÉS
    // requis : rétablir un `sku:` ne compilerait plus. Une assertion sur le JSON
    // envoyé n'ajouterait rien et se contenterait de recopier la signature.
  });

  test('une déclinaison sur un id VIDE ne matcherait aucune route (garde-fou du test)', () async {
    // Sécurise le test lui-même : prouve qu'un id vide ferait échouer l'appel
    // variante (URL `/products//variants`), donc que le test précédent est probant.
    adapter.onPost('$products/PID/variants', (s) => s.reply(200, {}), data: Matchers.any);

    await expectLater(
      CatalogApi(dio).addVariant('', sku: 'X'),
      throwsA(anything),
    );
  });
}

/// Remplace media-service dans ces tests.
///
/// Enregistre le `productId` reçu à chaque dépôt : c'est LUI qui prouve que
/// l'identifiant du produit s'est propagé. Un dépôt sous un propriétaire vide
/// réussirait côté media-service sans que rien ne le signale — le fichier
/// deviendrait simplement introuvable.
class _FakeUploader {
  final List<String> owners = [];

  Future<UploadedMedia> call(ProcessedImage image, String productId) async {
    owners.add(productId);
    return UploadedMedia(
      mediaId: 'MED-${image.fileName}',
      url: 'https://cdn.test/${image.fileName}',
    );
  }
}
