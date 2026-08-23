import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../core/config/app_config.dart';
import '../../core/identity/seller_identity.dart';
import '../../core/network/api_base.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';
import '../activities/activities_data.dart';
import '../activities/selected_activity.dart';

/// Mise en vente : le prix auquel le vendeur propose un produit.
///
/// Deux prix coexistent, et les confondre serait grave :
///  • `sellerPrice` — ce que le vendeur ENCAISSE (son prix net) ;
///  • `productPrice` — ce que l'acheteur PAIE (net + commission + frais provider).
/// Le vendeur saisit le premier ; l'app affiche toujours les deux.
class Offer {
  Offer({
    required this.id,
    required this.productId,
    required this.productName,
    required this.variantId,
    required this.storeId,
    required this.sku,
    required this.currency,
    required this.status,
    required this.condition,
    required this.handlingTime,
    required double? sellerPrice,
    required double? productPrice,
    required this.commissionAmount,
    required this.providerFeeAmount,
    required double basePriceAmount,
    this.compareAt,
    this.discountEndsOn,
  })  : _sellerPrice = sellerPrice,
        _productPrice = productPrice,
        _basePriceAmount = basePriceAmount;

  final String id;
  final String productId;
  final String productName;

  /// La DÉCLINAISON vendue, et non le produit.
  ///
  /// C'EST ELLE QUI IDENTIFIE UNE MISE EN VENTE, PAS LE SKU.
  ///
  /// Le monolithe créait une offre à partir d'un `productId` et d'un `sku`
  /// SAISIS SÉPARÉMENT, sans vérifier que la référence appartenait bien à ce
  /// produit : on pouvait mettre en vente le produit A avec le SKU du produit B,
  /// et Inventory décrémentait alors le stock du mauvais article. catalog-service
  /// n'accepte plus que le `variantId`, d'où il déduit lui-même la référence.
  final String variantId;

  /// La boutique qui vend. Un vendeur peut en avoir plusieurs, et l'unicité
  /// « une offre par déclinaison » se compte PAR BOUTIQUE, pas par vendeur.
  final String storeId;

  /// Référence de la déclinaison, rendue par le serveur — jamais saisie.
  ///
  /// Vide si la déclinaison a disparu entre-temps : un affichage sans référence
  /// est préférable à une référence inventée.
  final String sku;
  final String currency;
  final String status; // active | paused | closed
  final String condition;
  final int handlingTime;
  final double? commissionAmount;
  final double? providerFeeAmount;

  final double? _sellerPrice;
  final double? _productPrice;
  final double _basePriceAmount;

  /// Prix acheteur d'ORIGINE (barré) quand une remise est active. Null sinon.
  final double? compareAt;

  /// Fin de la promo (affichage). Null = sans échéance.
  final DateTime? discountEndsOn;

  /// Vrai si une remise est active (prix barré présent et supérieur au prix courant).
  bool get hasDiscount => compareAt != null && compareAt! > productPrice;

  /// Prix payé par l'acheteur. Repli sur `basePriceAmount` si le BFF ne renvoie
  /// pas encore la décomposition (compat ascendante).
  double get productPrice => _productPrice ?? _basePriceAmount;

  /// Prix net vendeur. À défaut, on le reconstitue en retirant la majoration —
  /// jamais en affichant le prix acheteur, qui gonflerait ses revenus perçus.
  double get sellerPrice => _sellerPrice ?? (productPrice / AppConfig.priceMultiplier);

  /// ═══════════════════════════════════════════════════════════════════════════
  /// CETTE LECTURE A CHANGÉ DE SOURCE : `OfferDto` DE catalog-service.
  ///
  /// Les noms ne sont plus ceux du BFF du monolithe, et le remplacement n'est pas
  /// mécanique — chacun demande une décision :
  ///
  ///   • `productPrice`  ← `effectivePrice`. Le serveur compare lui-même
  ///     l'échéance de la promo à l'heure courante ; refaire ce calcul ici ferait
  ///     exister DEUX règles de promotion, qui divergeraient sur un fuseau ou une
  ///     seconde près, et l'app afficherait un prix que la caisse ne pratique pas.
  ///
  ///   • `compareAt`     ← `buyerPrice`, MAIS SEULEMENT SI UNE PROMO COURT.
  ///     Hors promo, `buyerPrice` et `effectivePrice` sont égaux : les recopier
  ///     tous les deux afficherait un prix barré identique au prix courant.
  ///     Le test porte sur `promotionalPrice`, la seule marque non ambiguë.
  ///
  ///   • `sku`, `productName` — désormais SERVIS, plus jamais saisis ni devinés.
  ///
  ///   • `basePriceAmount` — le repli historique. Le contrat rend toujours
  ///     `buyerPrice`, donc il ne sert plus ; il reste alimenté pour que
  ///     [productPrice] garde une valeur sensée si un champ manquait.
  /// ═══════════════════════════════════════════════════════════════════════════
  factory Offer.fromJson(Map d) {
    final buyer = Json.asDouble(d['buyerPrice']);
    final promo = d['promotionalPrice'] == null ? null : Json.asDouble(d['promotionalPrice']);

    return Offer(
      id: Json.str(d['id']),
      productId: Json.str(d['productId']),
      productName: Json.str(d['productName'], 'Produit'),
      variantId: Json.str(d['variantId']),
      storeId: Json.str(d['storeId']),
      sku: Json.str(d['sku']),
      currency: Json.str(d['currency'], AppConfig.defaultCurrency),
      status: Json.str(d['status'], 'active'),
      condition: Json.str(d['condition'], 'new'),
      handlingTime: Json.asInt(d['handlingTimeDays']),
      sellerPrice: d['sellerPrice'] == null ? null : Json.asDouble(d['sellerPrice']),
      productPrice: d['effectivePrice'] == null ? null : Json.asDouble(d['effectivePrice']),
      commissionAmount: d['commissionAmount'] == null ? null : Json.asDouble(d['commissionAmount']),
      providerFeeAmount: d['providerFeeAmount'] == null ? null : Json.asDouble(d['providerFeeAmount']),
      basePriceAmount: buyer,
      compareAt: promo == null ? null : buyer,
      discountEndsOn: Json.asDate(d['promotionEndsOnUtc']),
    );
  }
}

/// État du produit vendu, tel que le backend l'attend (« new | used |
/// refurbished »). Les valeurs techniques ne sont JAMAIS montrées au vendeur :
/// il choisit un libellé, on envoie le code.
const kOfferConditions = <({String value, String label})>[
  (value: 'new', label: 'Neuf'),
  (value: 'used', label: 'Occasion'),
  (value: 'refurbished', label: 'Reconditionné'),
];

/// Libellé lisible et LOCALISÉ d'une condition (repli sur la valeur brute si le
/// backend en introduit une nouvelle — mieux vaut afficher un code qu'un vide).
String conditionLabel(AppLocalizations l, String value) {
  switch (value.toLowerCase()) {
    case 'new':
      return l.condNew;
    case 'used':
      return l.condUsed;
    case 'refurbished':
      return l.condRefurbished;
  }
  return value.isEmpty ? '—' : value;
}

/// Options localisées pour un sélecteur d'état (valeur technique + libellé traduit).
List<({String value, String label})> offerConditionOptions(AppLocalizations l) =>
    [for (final c in kOfferConditions) (value: c.value, label: conditionLabel(l, c.value))];

/// Lieu d'expédition (obligatoire pour mettre en vente).
class ShipLocation {
  ShipLocation({required this.id, required this.label});
  final String id;
  final String label;

  /// Libellé : commune + POINT DE REPÈRE, pas la rue.
  ///
  /// Le repère est ce qui distingue deux entrepôts d'une même commune, et ce que
  /// le coursier utilise réellement — au Bénin, beaucoup de lieux n'ont pas de rue.
  /// Même règle que dans la console vendeur : deux libellés différents pour la même
  /// donnée sèmeraient le doute.
  factory ShipLocation.fromJson(Map d) {
    final commune = Json.str(d['communeName']);
    final landmark = Json.str(d['landmark']);
    final line = Json.str(d['line']);
    final detail = landmark.isNotEmpty ? landmark : line;
    final label = detail.isNotEmpty ? '$commune — $detail' : commune;
    return ShipLocation(id: Json.str(d['id']), label: label.isEmpty ? 'Lieu' : label);
  }
}

/// ═════════════════════════════════════════════════════════════════════════════
/// LES MISES EN VENTE VIVENT DANS catalog-service (phase 3).
///
/// IL N'Y A PAS DE `/api/offers`, ET IL N'Y EN AURA PAS.
///
/// Les offres ont été GREFFÉES dans catalog-service plutôt qu'extraites dans un
/// `products-service` : `Product`, `Variant` et `ProductOffer` sont un même
/// invariant — une offre porte une déclinaison, qui porte le SKU, qui porte le
/// stock. Les séparer aurait imposé un appel réseau pour chaque vérification
/// d'appartenance. Les routes sont donc sous `/api/catalog/seller`.
///
/// TOUTES LES ÉCRITURES SONT GARDÉES PAR `DenyUnlessOwnerAsync` CÔTÉ SERVEUR.
///
/// Le `sellerId` n'est JAMAIS envoyé par l'application : le serveur le tire du
/// jeton. Un identifiant d'offre d'autrui rend 404, pas 403 — savoir qu'une offre
/// existe est déjà une information.
///
/// LES LIEUX D'EXPÉDITION RESTENT CHEZ inventory-service.
///
/// `GET /api/inventory/owners/{ownerId}/locations` — lecture et écriture ouvertes
/// au vendeur depuis VEN11. Créer et supprimer un lieu sont dans `InventoryApi`,
/// pas ici : deux chemins vers la même route finiraient par se contredire.
/// ═════════════════════════════════════════════════════════════════════════════
class OffersApi extends ApiBase {
  const OffersApi(super.dio);

  static const String _seller = '${AppConfig.catalog}/seller';

  /// Les mises en vente d'UNE boutique.
  ///
  /// IL N'Y A PAS DE ROUTE « TOUTES MES OFFRES », ET C'EST DÉLIBÉRÉ CÔTÉ
  /// SERVEUR : la garde porte sur la boutique, dont on vérifie qu'elle appartient
  /// bien au vendeur du jeton. Une route par vendeur aurait obligé à refaire ce
  /// contrôle ailleurs. Le regroupement, lui, se fait ici — voir [offersProvider].
  Future<List<Offer>> byStore(String storeId) => guard(() async {
        final resp = await dio.get('$_seller/stores/$storeId/offers');
        return Json.list(resp.data).map(Offer.fromJson).toList();
      });

  /// Lieux d'expédition du vendeur — branché sur inventory-service.
  ///
  /// `ownerId` est le `sellerId` résolu par le socle d'identité : c'est ce que
  /// `FulfillmentLocationSummary.OwnerId` porte côté serveur. Aucun écran ne doit
  /// le fabriquer (cf. `core/identity/seller_identity.dart`).
  Future<List<ShipLocation>> locations(String sellerId) => guard(() async {
        final resp = await dio.get('${AppConfig.inventory}/owners/$sellerId/locations');
        return Json.list(resp.data).map(ShipLocation.fromJson).toList();
      });

  /// CRÉER ET SUPPRIMER UN LIEU NE SONT PLUS ICI — VOIR `InventoryApi`.
  ///
  /// Les lieux d'expédition n'ont jamais appartenu à Products/Offers : ce sont
  /// des `FulfillmentLocation` d'inventory-service, et la LECTURE ci-dessus y
  /// allait déjà (`GET /api/inventory/owners/{id}/locations`). Seule l'écriture
  /// manquait, et elle est ouverte depuis VEN11.
  ///
  /// Les deux méthodes vivent donc dans `InventoryApi`, avec le reste du stock.
  /// En garder une copie ici donnerait deux chemins vers la même route, et deux
  /// commentaires qui finiraient par se contredire — c'est exactement ce que la
  /// note de tête de `not_migrated.dart` reproche aux inventaires dispersés.


  /// Met une DÉCLINAISON en vente dans une boutique.
  ///
  /// `variantId`, PAS `sku` — ET C'EST UNE CORRECTION DE SÉCURITÉ.
  ///
  /// L'ancienne route prenait `productId` et `sku` saisis séparément, sans
  /// vérifier que la référence appartenait à ce produit : on pouvait mettre en
  /// vente le produit A en déclarant le SKU du produit B, et Inventory
  /// décrémentait le stock du mauvais article à chaque commande. catalog-service
  /// n'accepte plus que l'identifiant de déclinaison, dont il déduit lui-même la
  /// référence. Ne pas revenir en arrière pour simplifier un écran.
  ///
  /// AUCUN `sellerId` DANS LE CORPS : le serveur le tire du jeton.
  Future<String> create({
    required String productId,
    required String variantId,
    required String storeId,
    required double sellerPrice,
    required String shipFromLocationId,
    String currency = AppConfig.defaultCurrency,
    String condition = 'new',
    String fulfillmentType = 'Fbs',
    int handlingTime = 2,
  }) =>
      guard(() async {
        final resp = await dio.post('$_seller/offers', data: {
          'productId': productId,
          'variantId': variantId,
          'storeId': storeId,
          'sellerPrice': sellerPrice,
          'currency': currency,
          'condition': condition,
          'fulfillmentType': fulfillmentType,
          'shipFromLocationId': shipFromLocationId,
          'handlingTimeDays': handlingTime,
        });
        return Json.str(Json.map(resp.data)['id']);
      });

  /// Retire définitivement une mise en vente.
  ///
  /// `DELETE`, MAIS LA LIGNE SURVIT. Le serveur ARCHIVE : une commande passée
  /// référence cette offre, et l'effacer laisserait un historique qui pointe vers
  /// rien. L'état est terminal — on ne réactive pas une offre archivée, on en
  /// recrée une.
  Future<void> delete(String id) => guard(() async {
        await dio.delete('$_seller/offers/$id');
      });

  /// Change le prix NET vendeur. Le prix acheteur est recalculé côté serveur.
  ///
  /// `currency` N'EST PAS ENVOYÉE : une offre ne change pas de devise en cours
  /// de route. Le paramètre reste dans la signature parce que les écrans le
  /// passent, mais l'accepter côté serveur aurait permis de rebaptiser un prix en
  /// une autre monnaie sans rien recalculer.
  ///
  /// UNE REMISE EN COURS EST ANNULÉE par ce geste : elle avait été consentie
  /// sur l'ancien prix.
  Future<void> changePrice(String id, double sellerPrice, String currency) => guard(() async {
        await dio.put('$_seller/offers/$id/price', data: {'sellerPrice': sellerPrice});
      });

  /// « active » ou « paused » — deux routes distinctes côté serveur.
  ///
  /// CE N'EST PAS UN CHAMP `status` QU'ON ÉCRIRAIT. Le serveur expose des
  /// TRANSITIONS (`/activate`, `/pause`), parce que tous les états ne se valent
  /// pas : `Suspended` est posé par la modération et `OutOfStock` par Inventory —
  /// un vendeur ne doit pouvoir ni s'en extraire ni s'y mettre.
  Future<void> changeStatus(String id, String status) => guard(() async {
        final action = status.toLowerCase() == 'active' ? 'activate' : 'pause';
        await dio.post('$_seller/offers/$id/$action');
      });

  /// Pose une remise, exprimée en prix NET VENDEUR après remise.
  ///
  /// LE POURCENTAGE EST RÉSOLU ICI, LE BARÈME NE L'EST PAS.
  ///
  /// La feuille propose « −10 % » ou « −500 F » ; le serveur, lui, ne connaît
  /// qu'un prix promotionnel absolu. Convertir un pourcentage en montant est une
  /// affaire d'interface — l'aperçu que le vendeur voit à l'écran doit être
  /// exactement ce qui part. En revanche la COMMISSION reste côté serveur : on
  /// envoie un net, jamais un prix acheteur (voir `ProductOffer.ApplyPromotion`).
  Future<void> applyDiscount(String id,
          {required String type, required double value, DateTime? endsOn, required double sellerPrice}) =>
      guard(() async {
        final promo = type == 'Percentage' ? sellerPrice * (1 - value / 100) : sellerPrice - value;
        await dio.put('$_seller/offers/$id/promotion', data: {
          // Arrondi à l'unité : le franc CFA n'a pas de subdivision en
          // circulation, et le serveur arrondit de son côté — envoyer des
          // décimales ferait diverger l'aperçu du prix réellement posé.
          'promotionalSellerPrice': promo.roundToDouble(),
          'endsOnUtc': endsOn?.toUtc().toIso8601String(),
        });
      });

  Future<void> removeDiscount(String id) => guard(() async {
        await dio.delete('$_seller/offers/$id/promotion');
      });

  /// Le délai de préparation annoncé à l'acheteur (0 à 30 jours).
  Future<void> changeHandlingTime(String id, int days) => guard(() async {
        await dio.put('$_seller/offers/$id/handling-time', data: {'handlingTimeDays': days});
      });
}

final offersApiProvider = Provider<OffersApi>((ref) => OffersApi(ref.watch(dioProvider)));

/// Les mises en vente visibles dans le contexte courant.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LE `storeId` VIENT DES ACTIVITÉS, PAS D'UN APPEL DÉDIÉ.
///
/// `GetMerchantActivitiesHandler` construit chaque activité EXPRESS à partir de
/// `store.Id` : l'identifiant d'activité EST l'identifiant de boutique. La liste
/// est déjà chargée pour l'écran d'aiguillage, donc ce fournisseur ne coûte aucun
/// aller-retour de plus. Ajouter un `GET /api/merchants/{id}/stores` ferait une
/// seconde source pour la même donnée, qui divergerait le jour où l'une des deux
/// filtrerait les boutiques fermées.
///
/// LES RESTAURANTS SONT EXCLUS. Leur identifiant est un `RestaurantId`, pas un
/// `StoreId` : l'envoyer rendrait 404 (au mieux) sur une route de boutique. Un
/// plat se gère par la carte, pas par une mise en vente.
///
/// EN VUE CONSOLIDÉE, ON INTERROGE TOUTES LES BOUTIQUES, EN PARALLÈLE.
///
/// Le serveur n'expose volontairement pas de route « toutes mes offres » — la
/// garde d'appartenance porte sur la boutique. Le regroupement revient donc au
/// client. `Future.wait` plutôt qu'une boucle : trois boutiques feraient sinon
/// trois allers-retours en série sur un réseau où chacun coûte cher.
/// ═════════════════════════════════════════════════════════════════════════════
final offersProvider = FutureProvider<List<Offer>>((ref) async {
  final activities = await ref.watch(activitiesProvider.future);
  final selected = ref.watch(selectedActivityIdProvider);

  final boutiques = activities.data
      .where((a) => a.universe == HbaUniverse.express)
      .where((a) => selected == null || selected.isEmpty || a.id == selected)
      .map((a) => a.id)
      .toList();

  if (boutiques.isEmpty) {
    // Un compte sans boutique n'a pas d'offres — ce n'est pas une erreur, et
    // lever ici ferait afficher un échec là où l'écran doit dire « rien encore ».
    return const <Offer>[];
  }

  final api = ref.watch(offersApiProvider);
  final lots = await Future.wait(boutiques.map(api.byStore));
  return [for (final lot in lots) ...lot];
});

/// Entrepôts du vendeur connecté.
///
/// Dépend de [requiredSellerIdProvider] : tant que l'identité n'est pas résolue,
/// ce fournisseur reste en chargement ; pour un compte sans boutique, il porte
/// une erreur NOMMÉE. Aucune requête ne part avec un identifiant vide.
final locationsProvider = FutureProvider<List<ShipLocation>>((ref) async {
  final sellerId = await ref.watch(requiredSellerIdProvider.future);
  return ref.watch(offersApiProvider).locations(sellerId);
});
