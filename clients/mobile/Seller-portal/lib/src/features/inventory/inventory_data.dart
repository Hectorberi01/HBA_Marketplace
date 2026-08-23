import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_base.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

/// Article de stock : une quantité, pour un SKU, dans un lieu donné.
///
/// Le stock se tient par SKU — donc par VARIANTE, pas par produit. Un produit
/// sans déclinaison n'a rien à stocker : c'est une fiche, pas un article vendable.
class InventoryItem {
  InventoryItem({
    required this.id,
    required this.sku,
    required this.locationId,
    required this.onHand,
    required this.reserved,
    required this.available,
    required this.reorderThreshold,
    required this.isLowStock,
  });

  final String id;
  final String sku;
  final String locationId;

  /// Physiquement en rayon.
  final int onHand;

  /// Déjà promis à des commandes en cours.
  final int reserved;

  /// Réellement vendable (onHand − reserved). C'est le seul chiffre qui compte
  /// pour savoir si l'on peut encore vendre.
  final int available;

  final int reorderThreshold;
  final bool isLowStock;

  factory InventoryItem.fromJson(Map d) => InventoryItem(
        id: Json.str(d['id']),
        sku: Json.str(d['sku']),
        locationId: Json.str(d['locationId']),
        onHand: Json.asInt(d['onHand']),
        reserved: Json.asInt(d['reserved']),
        available: Json.asInt(d['available']),
        reorderThreshold: Json.asInt(d['reorderThreshold']),
        isLowStock: Json.asBool(d['isLowStock']),
      );
}

class InventoryApi extends ApiBase {
  const InventoryApi(super.dio);

  static const _p = AppConfig.inventory;

  /// Stock d'un SKU, tous lieux confondus.
  ///
  /// LE CHEMIN N'EST PAS `/by-sku/`, ET C'EST TOUT L'ÉCART.
  ///
  /// L'ancien appel visait `/seller/inventory/by-sku/{sku}` du BFF du monolithe.
  /// inventory-service sert `GET /api/inventory/items/sku/{sku}`. La forme de la
  /// réponse, elle, est identique — `InventoryItemSummary` porte exactement les
  /// huit champs lus par [InventoryItem].
  ///
  /// ═══════════════════════════════════════════════════════════════════════════
  /// LE SKU EST ÉCHAPPÉ, ET C'EST LE SEUL SEGMENT DE TOUTE L'APPLICATION QUI
  ///    VIENNE D'UNE SAISIE LIBRE.
  ///
  /// Partout ailleurs, un segment interpolé est un identifiant rendu par le
  /// serveur : un GUID, donc sans surprise. Le SKU, lui, est tapé par le vendeur
  /// dans l'assistant de création de produit, avec pour seule validation qu'il ne
  /// soit pas vide.
  ///
  /// Or `{sku}` est un paramètre de SEGMENT SIMPLE côté service
  /// (`InventoryEndpoints.cs`, `MapGet("/items/sku/{sku}")`) : il ne peut pas
  /// contenir de barre oblique. Une référence de la forme « REF/2024-A » —
  /// parfaitement banale — produisait `/items/sku/REF/2024-A`, un segment de
  /// plus, aucune route correspondante, et un 404 MUET : la tuile de stock
  /// restait vide sans que rien ne relie la cause au libellé saisi. Un espace ou
  /// un `#` cassaient l'URI encore plus tôt, côté Dio.
  ///
  /// Le client typé de la passerelle échappait déjà, lui
  /// (`InventoryClient.cs` : `Uri.EscapeDataString(sku)`). L'écart entre les deux
  /// n'a jamais été un choix.
  /// ═══════════════════════════════════════════════════════════════════════════
  Future<List<InventoryItem>> bySku(String sku) => guard(() async {
        final resp = await dio.get('$_p/items/sku/${Uri.encodeComponent(sku)}');
        return Json.list(resp.data).map(InventoryItem.fromJson).toList();
      });

  /// ═══════════════════════════════════════════════════════════════════════════
  /// LE VENDEUR ÉCRIT DE NOUVEAU SON PROPRE STOCK (VEN11, phase 2).
  ///
  /// Ces quatre gestes vivaient sous `MapAdminGroup` : un vendeur authentifié
  /// recevait 403 sur son PROPRE stock. Ce n'était pas un mauvais réglage mais
  /// une impossibilité — inventory-service ne savait pas résoudre compte →
  /// vendeur, et ne pouvait donc vérifier aucune propriété.
  ///
  /// Il le sait désormais (`AddMerchantsGrpcClient`), et les sept écritures sont
  /// passées dans un groupe authentifié gardé par `DenyUnlessOwnerAsync`.
  ///
  /// LA PROPRIÉTÉ D'UN ARTICLE PASSE PAR SON LIEU, ET CELA SE VOIT D'ICI.
  ///
  /// `InventoryItem` ne porte aucun propriétaire : c'est
  /// `FulfillmentLocation.OwnerId` qui désigne un vendeur. Toucher un article
  /// d'un lieu qui n'est pas le sien rend donc **404**, jamais 403 — un 403
  /// confirmerait que l'article existe.
  ///
  /// LES RÉSERVATIONS RESTENT FERMÉES, et définitivement : elles appartiennent
  /// à la saga de commande et passent par gRPC. Aucune méthode de cette classe ne
  /// les appelle.
  /// ═══════════════════════════════════════════════════════════════════════════
  Future<String> createItem({
    required String sku,
    required String locationId,
    required int onHand,
    int reorderThreshold = 0,
  }) =>
      guard(() async {
        final resp = await dio.post('$_p/items', data: {
          'sku': sku,
          'locationId': locationId,
          'onHand': onHand,
          'reorderThreshold': reorderThreshold,
        });
        return Json.str(Json.map(resp.data)['id']);
      });

  /// Entrée de stock : une livraison reçue. [quantity] est POSITIVE.
  Future<void> receive(String itemId, int quantity) => guard(() async {
        await dio.post('$_p/items/$itemId/receive', data: {'quantity': quantity});
      });

  /// Correction d'inventaire. [delta] est un ÉCART, signé — et non la nouvelle
  /// quantité. Un `-3` retire trois unités ; un `12` en ajoute douze. Envoyer la
  /// quantité constatée à la place de l'écart fausserait le stock de tout ce
  /// qu'il contenait déjà.
  Future<void> adjust(String itemId, int delta) => guard(() async {
        await dio.post('$_p/items/$itemId/adjust', data: {'delta': delta});
      });

  Future<void> setThreshold(String itemId, int threshold) => guard(() async {
        await dio.put('$_p/items/$itemId/reorder-threshold', data: {'threshold': threshold});
      });

  /// Déclare un lieu d'expédition.
  ///
  /// `ownerId` N'EST PAS ENVOYÉ, ET NE DOIT PAS L'ÊTRE. Le serveur l'impose
  /// depuis le jeton pour un vendeur : le laisser au client permettrait de créer
  /// un lieu au nom d'un autre, puis d'y écrire du stock en toute légitimité.
  Future<String> createLocation({
    required String type,
    required String commune,
    String? quartier,
    String? landmark,
    String? line,
    String? contactPhone,
  }) =>
      guard(() async {
        final resp = await dio.post('$_p/locations', data: {
          'type': type,
          'commune': commune,
          'quartier': quartier,
          'landmark': landmark,
          'line': line,
          'contactPhone': contactPhone,
        });
        return Json.str(Json.map(resp.data)['id']);
      });

  /// Supprime un lieu d'expédition.
  ///
  /// LE SERVICE REFUSE SI DU STOCK Y EST ENCORE POSÉ — c'est
  /// `DeleteFulfillmentLocationCommand` qui le décide, et il rend un conflit
  /// explicite. Ce n'est pas une panne à masquer : supprimer le lieu laisserait
  /// des articles sans adresse, et aucune course ne pourrait plus être créée
  /// pour les colis qui s'y trouvent.
  Future<void> deleteLocation(String locationId) => guard(() async {
        await dio.delete('$_p/locations/$locationId');
      });
}

final inventoryApiProvider = Provider<InventoryApi>((ref) => InventoryApi(ref.watch(dioProvider)));

/// Stock d'un SKU, tous lieux confondus.
final inventoryBySkuProvider =
    FutureProvider.family<List<InventoryItem>, String>((ref, sku) => ref.watch(inventoryApiProvider).bySku(sku));
