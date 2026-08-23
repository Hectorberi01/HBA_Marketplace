import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_base.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../../core/theme/app_theme.dart';
import '../../shared/utils/formatters.dart';

/// Univers produit. Détermine la couleur et le vocabulaire, jamais la logique.
///
/// IL VIENT DU CHAMP `type` DU BFF, PAS D'UNE CONVENTION LOCALE.
///
/// `MerchantActivityDto.Type` vaut « STORE » ou « RESTAURANT », en majuscules
/// (constantes du handler). Toute autre valeur est traitée comme une boutique —
/// un univers inconnu ne doit pas faire tomber l'écran d'aiguillage, qui est le
/// premier après la connexion.
enum HbaUniverse {
  express('HBAEXPRESS', 'HBAExpress'),
  food('HBA FOOD', 'HBA Food');

  const HbaUniverse(this.badge, this.label);

  /// Libellé du badge, en capitales comme sur la maquette.
  final String badge;

  /// Libellé lisible, pour les filtres et les titres.
  final String label;

  /// LIBELLÉ DU 3ᵉ ONGLET, QUI CHANGE AVEC L'UNIVERS.
  ///
  /// « Produits » pour une boutique, « Menu » pour un restaurant. Un
  /// restaurateur ne pense pas ses plats comme des « produits », et un
  /// commerçant n'a pas de « menu ».
  String get tabLabel => this == HbaUniverse.express ? 'Produits' : 'Menu';

  IconData get tabIcon => this == HbaUniverse.express
      ? Icons.inventory_2_outlined
      : Icons.restaurant_menu_outlined;

  Color get accent =>
      this == HbaUniverse.express ? AppTheme.brandGreen : AppTheme.foodAmber;

  Color get soft =>
      this == HbaUniverse.express ? AppTheme.brandGreenSoft : AppTheme.foodAmberSoft;

  static HbaUniverse fromType(String type) =>
      type.toUpperCase() == 'RESTAURANT' ? HbaUniverse.food : HbaUniverse.express;
}

/// Une activité gérée par le compte partenaire (`MerchantActivityDto`).
class SellerActivity {
  const SellerActivity({
    required this.id,
    required this.name,
    required this.universe,
    required this.role,
    required this.status,
    required this.logoUrl,
    required this.isOpenNow,
  });

  final String id;
  final String name;
  final HbaUniverse universe;

  /// « OWNER » pour une boutique (le handler le force), rôle réel en majuscules
  /// pour un restaurant.
  final String role;

  /// Statut brut de la boutique ou du restaurant. Les deux univers n'ont PAS le
  /// même vocabulaire — `Active`/`Suspended` d'un côté, `InService`/`Paused` de
  /// l'autre — et il ne faut pas les fondre : seul [isOpenNow] est comparable.
  final String status;

  /// TOUJOURS `null` POUR UN RESTAURANT.
  ///
  /// Le handler le force (`GetMerchantActivitiesHandler`, ligne 105) : food-service
  /// ne rend qu'un `mediaId`, pas d'URL. L'écran doit donc savoir se rabattre sur
  /// les initiales, et non attendre une image qui n'arrivera jamais côté Food.
  final String? logoUrl;

  /// `null` quand l'amont n'a pas su le dire — à ne pas confondre avec « fermé ».
  final bool? isOpenNow;

  /// Initiales de repli quand il n'y a pas de logo.
  ///
  /// DÉRIVÉES, ET DONC PARFOIS DIFFÉRENTES DE LA MAQUETTE. Celle-ci écrit
  /// « TS » pour « HBA Tech Store » ; un découpage rend « HT ». Écrire des
  /// initiales à la main supposerait de les stocker quelque part — le contrat ne
  /// les porte pas, et personne ne les saisirait.
  String get initials {
    final words = name.trim().split(RegExp(r'\s+')).where((w) => w.isNotEmpty).toList();
    if (words.isEmpty) return '?';
    if (words.length == 1) {
      return words.first.substring(0, words.first.length >= 2 ? 2 : 1).toUpperCase();
    }
    return '${words[0][0]}${words[1][0]}'.toUpperCase();
  }

  /// « Boutique » ou « Restaurant ».
  String get kind => universe == HbaUniverse.food ? 'Restaurant' : 'Boutique';

  factory SellerActivity.fromJson(Map d) => SellerActivity(
        id: Json.str(d['id']),
        name: Json.str(d['name'], 'Activité'),
        universe: HbaUniverse.fromType(Json.str(d['type'])),
        role: Json.str(d['role']),
        status: Json.str(d['status']),
        logoUrl:
            (d['logoUrl']?.toString().isNotEmpty ?? false) ? d['logoUrl'].toString() : null,
        isOpenNow: d['isOpenNow'] is bool ? d['isOpenNow'] as bool : null,
      );
}

/// Ce que le BFF n'a pas pu obtenir sur ce rendu (`BffWarning`).
///
/// UNE RÉPONSE 200 PEUT ÊTRE INCOMPLÈTE, ET C'EST TOUT L'INTÉRÊT DU BFF.
///
/// L'enveloppe porte `warnings` et `isPartial` : quand un service amont est à
/// terre, la passerelle rend quand même ce qu'elle a, en le disant. Ignorer ces
/// deux champs — ce que ferait un parseur qui lirait `data` seul — présenterait
/// une liste amputée comme complète, et un solde absent comme un solde nul.
class BffWarning {
  const BffWarning({required this.source, required this.code});

  /// `Merchant`, `Food`, `Order`, `Financial`…
  final String source;

  /// `SERVICE_UNAVAILABLE` ou `NOT_CONFIGURED`.
  final String code;

  bool get isUnavailable => code == 'SERVICE_UNAVAILABLE';

  factory BffWarning.fromJson(Map d) => BffWarning(
        source: Json.str(d['source']),
        code: Json.str(d['code']),
      );

  /// Phrase destinée au vendeur. Nomme le sujet, pas le microservice.
  String get message {
    switch (source) {
      case 'Financial':
        return 'Vos soldes ne sont pas joignables pour le moment.';
      case 'Order':
        return 'Vos dernières commandes ne sont pas joignables pour le moment.';
      case 'Food':
        return 'Vos restaurants ne sont pas joignables pour le moment.';
      case 'Merchant':
        return 'Vos boutiques ne sont pas joignables pour le moment.';
    }
    return 'Une partie des informations n\'a pas pu être chargée.';
  }
}

/// Réponse d'un BFF : la donnée, et ce qui lui manque.
class BffResult<T> {
  const BffResult({required this.data, required this.warnings});

  final T data;
  final List<BffWarning> warnings;

  bool get isPartial => warnings.isNotEmpty;

  static BffResult<T> parse<T>(dynamic raw, T Function(Map) fromData) {
    final map = Json.map(raw);
    return BffResult<T>(
      data: fromData(Json.map(map['data'])),
      warnings: Json.list(map['warnings']).map(BffWarning.fromJson).toList(),
    );
  }
}

/// ═════════════════════════════════════════════════════════════════════════════
/// LES ACTIVITÉS DU PARTENAIRE — `GET /api/v1/bff/merchant/activities`.
///
/// C'est le point d'entrée unique après connexion : il rend boutiques ET
/// restaurants, ce qu'aucun service seul ne sait faire.
///
/// IL EXIGE LE RÔLE `Seller` (politique `MerchantOnly`).
///
/// Un compte qui n'a que `FoodPartner` reçoit 403 — ce n'est PAS une panne, et
/// l'application ne doit pas le présenter comme telle. Un compte fraîchement
/// inscrit dont la boutique vient d'être créée est dans ce cas tant que son jeton
/// n'a pas été repris (cf. `AuthController`).
///
/// AUCUN CHIFFRE N'ACCOMPAGNE UNE ACTIVITÉ. C'EST LA GRANDE ABSENCE DE
///    L'ÉCRAN DE SÉLECTION.
///
/// La maquette montre, sous chaque activité, « 12 commandes aujourd'hui » et
/// « 3 commandes à préparer ». `MerchantActivityDto` ne porte QUE
/// `type / id / name / logoUrl / role / status / isOpenNow`. Ces compteurs
/// n'existent pas dans le contrat, et les obtenir exigerait un appel au tableau
/// de bord PAR ACTIVITÉ — soit N requêtes sur l'écran le plus précoce de
/// l'application, avant même que le partenaire ait choisi quoi que ce soit.
///
/// Les tuiles chiffrées sont donc retirées de l'écran de sélection, et non
/// remplies de zéros : « 0 commande aujourd'hui » sur une boutique qui en a douze
/// détournerait le partenaire de l'activité qu'il devait ouvrir. Le tableau de
/// bord, lui, les donne — une fois l'activité choisie.
///
/// LA MAQUETTE FILTRAIT SUR « ACTIVITÉ DU JOUR ». CE FILTRE DISPARAÎT.
///
/// `selectableActivities` ne gardait que les activités ayant des commandes
/// aujourd'hui. Sans compteur, ce tri n'est plus calculable — et le reproduire au
/// jugé cacherait une boutique bien active. On propose donc TOUTES les activités.
/// ═════════════════════════════════════════════════════════════════════════════
class ActivitiesApi extends ApiBase {
  const ActivitiesApi(super.dio);

  Future<BffResult<List<SellerActivity>>> activities() => guard(() async {
        final resp = await dio.get('${AppConfig.bffMerchant}/activities');
        final map = Json.map(resp.data);
        return BffResult<List<SellerActivity>>(
          data: Json.list(Json.map(map['data'])['activities'])
              .map(SellerActivity.fromJson)
              .toList(),
          warnings: Json.list(map['warnings']).map(BffWarning.fromJson).toList(),
        );
      });

  /// ═══════════════════════════════════════════════════════════════════════════
  /// OUVRE UNE BOUTIQUE DE PLUS — `POST /api/merchants/{sellerId}/stores`.
  ///
  /// CETTE ROUTE EXISTAIT DEPUIS LE DÉBUT, SANS AUCUN APPELANT.
  ///
  /// « Ajouter une activité » était grisé, avec ce motif écrit dans la feuille de
  /// bascule : « le geste a un amont côté boutique, mais AUCUN écran de création
  /// n'existe hors du parcours d'inscription ». La première moitié était vraie, la
  /// conclusion fausse — il manquait l'écran, pas la route. `CreateStoreCommand`,
  /// son gestionnaire et sa garde d'appartenance attendaient depuis le début.
  ///
  /// C'est le neuvième cas de ce genre relevé dans ce dépôt : une couche
  /// applicative complète, joignable, et que rien n'appelle.
  ///
  /// UN VENDEUR PEUT AVOIR PLUSIEURS BOUTIQUES — ce n'est pas une supposition :
  /// `GET .../stores` rend une LISTE, et la passerelle en fait autant d'activités.
  /// Le restaurant, lui, est unique par compte (`food.restaurant.already_registered`).
  ///
  /// SEUL UN VENDEUR `Active` PEUT OUVRIR UNE BOUTIQUE (409
  /// `sellers.store.seller_not_active`). C'est la sanction qui serait contournée
  /// sinon : un vendeur suspendu rouvrirait ailleurs, avec un stock que le retrait
  /// n'a jamais touché puisqu'il n'existait pas.
  /// ═══════════════════════════════════════════════════════════════════════════
  Future<String> createStore(
    String sellerId, {
    required String name,
    required String contactPhone,
    String? contactEmail,
  }) =>
      guard(() async {
        final resp = await dio.post(
          '${AppConfig.merchants}/$sellerId/stores',
          data: {
            'name': name,
            'contactPhone': contactPhone,
            if (contactEmail != null && contactEmail.isNotEmpty) 'contactEmail': contactEmail,
          },
        );
        final id = Json.str(Json.map(resp.data)['id']);
        if (id.isEmpty) {
          // Sans identifiant, l'appelant ne peut ni basculer dessus ni la
          // configurer : mieux vaut le dire que rendre une chaîne vide qui
          // échouera trois écrans plus loin.
          throw ApiException('Boutique créée, mais son identifiant est absent de la réponse.');
        }
        return id;
      });

  /// ═══════════════════════════════════════════════════════════════════════════
  /// DÉPOSE UNE CANDIDATURE DE RESTAURANT — `POST /api/food/partner/restaurants`.
  ///
  /// CE N'EST PAS « OUVRIR UN RESTAURANT », C'EST LE DÉCLARER.
  ///
  /// L'établissement naît en brouillon : il faudra encore des horaires, un lieu,
  /// un vendeur de reversement, puis `POST .../submit` pour le soumettre. Promettre
  /// « votre restaurant est ouvert » ferait attendre des commandes qui ne
  /// viendront pas.
  ///
  /// UN SEUL ÉTABLISSEMENT PAR COMPTE — 409 `food.restaurant.already_registered`.
  /// L'appelant doit donc masquer le geste quand une activité RESTAURANT existe
  /// déjà, plutôt que de laisser découvrir la règle par un échec.
  ///
  /// LE FONDATEUR EST CRÉÉ DANS LA MÊME TRANSACTION côté serveur. Sans cela le
  /// créateur n'aurait aucun accès à ce qu'il vient de déposer — les routes de
  /// l'espace restaurateur autorisent sur l'APPARTENANCE, pas sur le propriétaire.
  /// ═══════════════════════════════════════════════════════════════════════════
  Future<String> registerRestaurant({
    required String name,
    String? description,
    required String phone,
  }) =>
      guard(() async {
        final resp = await dio.post(
          '${AppConfig.food}/partner/restaurants',
          data: {'name': name, 'description': description, 'phone': phone},
        );
        final id = Json.str(Json.map(resp.data)['id']);
        if (id.isEmpty) {
          throw ApiException('Établissement créé, mais son identifiant est absent de la réponse.');
        }
        return id;
      });

  // ── Ce qui se règle APRÈS la création ──────────────────────────────────────
  //
  // ═══════════════════════════════════════════════════════════════════════════
  // UNE ACTIVITÉ NAÎT INCOMPLÈTE, ET AUCUN APPEL UNIQUE NE LA COMPLÈTE.
  //
  // `POST .../stores` ne prend que nom + contacts ; `POST .../restaurants` que nom,
  // description et téléphone. Le lieu, les horaires et le logo sont TROIS routes de
  // plus, chacune sur son agrégat. Il n'y a pas de transaction pour les tenir.
  //
  // L'assistant les enchaîne donc et rend compte honnêtement de ce qui est passé —
  // le même parti que l'assistant produit. Prétendre à un enregistrement atomique
  // afficherait « échec » sur une boutique déjà créée, et le partenaire recréerait
  // un doublon.
  // ═══════════════════════════════════════════════════════════════════════════

  /// Crée le lieu d'où partent les colis (ou d'où l'on retire les plats).
  ///
  /// `type` VAUT « SellerAddress », PAS « Warehouse ».
  ///
  /// L'énumération n'a que deux valeurs : `SellerAddress` et `PlatformWarehouse`.
  /// Un commentaire de ce dépôt a affirmé que « Warehouse » était « la valeur
  /// attendue par le domaine » — c'était faux, et cela bloquait toute création de
  /// lieu, donc toute mise en vente, sans que l'erreur désigne la cause.
  ///
  /// `ownerId` N'EST PAS ENVOYÉ : le serveur l'ignore pour un non-administrateur
  /// et le remplace par le vendeur du jeton. L'envoyer laisserait croire qu'on
  /// choisit le propriétaire d'un lieu.
  ///
  /// COMMUNE ET QUARTIER PLUTÔT QU'UNE LIGNE LIBRE. À Cotonou ou Calavi, une
  /// adresse se donne ainsi — « Calavi, Tankpè, près du carrefour Aïtchédji » — et
  /// c'est ce que le livreur saura suivre. Une seule ligne de texte serait plus
  /// simple à saisir et inexploitable à la livraison.
  Future<String> createLocation({
    required String commune,
    required String quartier,
    String? landmark,
    String? line,
    required String contactPhone,
  }) =>
      guard(() async {
        final resp = await dio.post('${AppConfig.inventory}/locations', data: {
          'type': 'SellerAddress',
          'commune': commune,
          'quartier': quartier,
          'landmark': landmark,
          'line': line,
          'contactPhone': contactPhone,
        });
        final id = Json.str(Json.map(resp.data)['id']);
        if (id.isEmpty) {
          throw ApiException('Lieu créé, mais son identifiant est absent de la réponse.');
        }
        return id;
      });

  Future<void> attachStoreLocation(
    String sellerId, {
    required String storeId,
    required String locationId,
  }) =>
      guard(() async {
        await dio.put(
          '${AppConfig.merchants}/$sellerId/stores/$storeId/location',
          data: {'fulfillmentLocationId': locationId},
        );
      });

  /// LE LIEU DOIT APPARTENIR AU DOSSIER DE REVERSEMENT DÉJÀ RATTACHÉ.
  ///
  /// `AttachRestaurantLocationAsync` relit le lieu dans Inventory et compare son
  /// propriétaire. D'où l'ordre imposé : rattacher le vendeur de reversement AVANT
  /// le lieu. L'inverse échoue avec un message qui parle du lieu, pas du vendeur.
  Future<void> attachRestaurantLocation(
    String restaurantId, {
    required String locationId,
  }) =>
      guard(() async {
        await dio.put(
          '${AppConfig.food}/partner/restaurants/$restaurantId/location',
          data: {'fulfillmentLocationId': locationId},
        );
      });

  Future<void> attachPayoutSeller(String restaurantId, {required String sellerId}) =>
      guard(() async {
        await dio.put(
          '${AppConfig.food}/partner/restaurants/$restaurantId/payout-seller',
          data: {'sellerId': sellerId},
        );
      });

  /// La grille horaire ENTIÈRE — elle REMPLACE la précédente.
  ///
  /// JOURS EN ANGLAIS INVARIANT (« Monday »…) : le serveur fait un
  /// `Enum.TryParse<DayOfWeek>`. « Lundi » rend 400 `day_invalid`.
  ///
  /// LES DEUX UNIVERS ONT DEUX ROUTES ET LE MÊME CORPS. `opening-hours` côté
  /// boutique, `service-hours` côté restaurant : la forme est identique, mais les
  /// fondre en une seule méthode « intelligente » cacherait qu'un jour l'une des
  /// deux peut diverger.
  Future<void> setStoreOpeningHours(
    String sellerId, {
    required String storeId,
    required List<Map<String, String>> hours,
  }) =>
      guard(() async {
        await dio.put(
          '${AppConfig.merchants}/$sellerId/stores/$storeId/opening-hours',
          data: {'hours': hours},
        );
      });

  Future<void> setRestaurantServiceHours(
    String restaurantId, {
    required List<Map<String, String>> hours,
  }) =>
      guard(() async {
        await dio.put(
          '${AppConfig.food}/partner/restaurants/$restaurantId/service-hours',
          data: {'hours': hours},
        );
      });

  /// CÔTÉ BOUTIQUE, LE LOGO PASSE PAR LE PROFIL, QUI PORTE AUSSI LE NOM.
  ///
  /// `StoreProfileRequest(Name, LogoUrl, Description)` : il faut donc RENVOYER le
  /// nom, sinon on le remplace par la chaîne vide. C'est le même piège que
  /// `displayOrder` sur les plats — un champ qu'on ne pense pas à transmettre et
  /// que le serveur écrase.
  Future<void> setStoreProfile(
    String sellerId, {
    required String storeId,
    required String name,
    String? logoUrl,
    String? description,
  }) =>
      guard(() async {
        await dio.put(
          '${AppConfig.merchants}/$sellerId/stores/$storeId/profile',
          data: {'name': name, 'logoUrl': logoUrl, 'description': description},
        );
      });

  /// CÔTÉ RESTAURANT, LA ROUTE VIENT D'ÊTRE OUVERTE — `Restaurant.SetMedia`
  /// n'avait aucun appelant. C'est pourquoi aucun logo de restaurant ne s'est
  /// jamais affiché dans le sélecteur d'activité.
  Future<void> setRestaurantLogo(
    String restaurantId, {
    required String? mediaId,
    required String? url,
  }) =>
      guard(() async {
        await dio.put(
          '${AppConfig.food}/partner/restaurants/$restaurantId/logo',
          data: {'logoMediaId': mediaId, 'logoPublicUrl': url},
        );
      });
}

final activitiesApiProvider =
    Provider<ActivitiesApi>((ref) => ActivitiesApi(ref.watch(dioProvider)));

final activitiesProvider = FutureProvider<BffResult<List<SellerActivity>>>(
    (ref) => ref.watch(activitiesApiProvider).activities());
