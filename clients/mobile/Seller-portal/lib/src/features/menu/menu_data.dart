import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_base.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

/// Le restaurant du compte connecté (`PartnerRestaurantView`).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// C'EST LE SEUL ENDROIT OÙ LIRE SON `restaurantId`, ET IL N'Y EN A QU'UN.
///
/// `GET /api/food/partner/me` résout l'appartenance depuis le jeton. Un compte
/// est membre d'AU PLUS UN établissement (`GetStaffMembershipAsync` rend une
/// appartenance unique, et la base le verrouille) : le multi-restaurant n'existe
/// pas encore. Aucun écran ne doit fabriquer cet identifiant, pour la même raison
/// que le `sellerId` — les routes le comparent à l'appartenance du jeton et
/// répondent 404 sinon.
/// ═════════════════════════════════════════════════════════════════════════════
class PartnerRestaurant {
  const PartnerRestaurant({
    required this.restaurantId,
    required this.name,
    required this.status,
    required this.role,
    required this.isFounder,
    required this.isActive,
    required this.permissions,
    required this.payoutSellerId,
    required this.acceptsOrdersNow,
    required this.blockedReason,
  });

  final String restaurantId;
  final String name;
  final String status;
  final String role;
  final bool isFounder;
  final bool isActive;
  final List<String> permissions;

  /// Vendeur de reversement rattaché. `null` tant que le restaurateur ne l'a pas
  /// relié — et sans lui, aucune finance n'est lisible.
  final String? payoutSellerId;

  final bool acceptsOrdersNow;

  /// Chaîne vide quand rien ne bloque.
  final String blockedReason;

  bool can(String permission) => permissions.contains(permission);

  factory PartnerRestaurant.fromJson(Map d) => PartnerRestaurant(
        restaurantId: Json.str(d['restaurantId']),
        name: Json.str(d['name'], 'Mon restaurant'),
        status: Json.str(d['status']),
        role: Json.str(d['role']),
        isFounder: Json.asBool(d['isFounder']),
        isActive: Json.asBool(d['isActive']),
        permissions: (d['permissions'] is List)
            ? (d['permissions'] as List).map((e) => e.toString()).toList()
            : const <String>[],
        payoutSellerId: (d['payoutSellerId']?.toString().isNotEmpty ?? false)
            ? d['payoutSellerId'].toString()
            : null,
        acceptsOrdersNow: Json.asBool(d['acceptsOrdersNow']),
        blockedReason: Json.str(d['blockedReason']),
      );
}

/// Un choix dans un groupe d'options (`OptionView`).
class DishOption {
  const DishOption({
    required this.id,
    required this.name,
    required this.priceDelta,
    required this.isAvailable,
  });

  final String id;
  final String name;

  /// ÉCART AU PRIX DE BASE, POSITIF OU NÉGATIF — pas un prix.
  final double priceDelta;

  final bool isAvailable;

  factory DishOption.fromJson(Map d) => DishOption(
        id: Json.str(d['id']),
        name: Json.str(d['name']),
        priceDelta: Json.asDouble(d['priceDelta']),
        isAvailable: Json.asBool(d['isAvailable']),
      );
}

/// Groupe d'options (`OptionGroupView`).
class DishOptionGroup {
  const DishOptionGroup({
    required this.id,
    required this.name,
    required this.minSelections,
    required this.maxSelections,
    required this.isRequired,
    required this.options,
  });

  final String id;
  final String name;
  final int minSelections;
  final int maxSelections;

  /// « OBLIGATOIRE » BLOQUE L'AJOUT AU PANIER CÔTÉ CLIENT. Ce n'est pas un
  /// ornement : un groupe obligatoire mal réglé rend le plat incommandable.
  final bool isRequired;

  final List<DishOption> options;

  factory DishOptionGroup.fromJson(Map d) => DishOptionGroup(
        id: Json.str(d['id']),
        name: Json.str(d['name']),
        minSelections: Json.asInt(d['minSelections']),
        maxSelections: Json.asInt(d['maxSelections']),
        isRequired: Json.asBool(d['isRequired']),
        options: Json.list(d['options']).map(DishOption.fromJson).toList(),
      );
}

/// Un plat (`MenuItemView`).
class Dish {
  const Dish({
    required this.id,
    required this.name,
    required this.description,
    required this.imageMediaId,
    required this.legacyImageUrl,
    required this.imageUrl,
    required this.basePrice,
    required this.currency,
    required this.isOrderable,
    required this.hasImage,
    required this.backAt,
    required this.optionGroups,
  });

  final String id;
  final String name;
  final String? description;

  /// UN IDENTIFIANT, PAS UNE URL. La photo se lit via media-service.
  /// `legacyImageUrl` ne subsiste que pour les plats antérieurs à la bascule.
  final String? imageMediaId;
  final String? legacyImageUrl;

  /// L'adresse à afficher, repli déjà fait par le serveur (`displayImageUrl`).
  ///
  /// NE PAS RECALCULER `imageMediaId != null ? ... : legacyImageUrl` ICI.
  /// C'est ce que faisaient les trois applications, chacune à sa façon. Le serveur
  /// tranche une fois ; l'app affiche.
  final String? imageUrl;

  /// Prix hors options.
  final double basePrice;
  final String currency;

  /// Commandable en ce moment.
  ///
  /// FAUX POUR QUATRE RAISONS QUI N'APPELLENT PAS LE MÊME GESTE : épuisé
  /// aujourd'hui, groupe d'options insatisfiable, carte hors créneau,
  /// ou PHOTO MANQUANTE. Ne jamais l'afficher seul comme « indisponible » : voir
  /// `hasImage`, et `dishStatus()` qui fait le tri.
  final bool isOrderable;

  /// L'article porte-t-il une photo ?
  ///
  /// LA PHOTO EST OBLIGATOIRE POUR VENDRE (règle serveur, `MenuItem.HasImage`).
  /// Un plat sans photo n'est jamais commandé — mais il n'est pas « épuisé » : il
  /// attend un geste du restaurateur. Ce champ est ce qui permet de le dire.
  final bool hasImage;

  /// Retour prévu de disponibilité. `null` si indisponible sans échéance.
  final DateTime? backAt;

  final List<DishOptionGroup> optionGroups;

  factory Dish.fromJson(Map d) => Dish(
        id: Json.str(d['id']),
        name: Json.str(d['name'], 'Plat'),
        description: (d['description']?.toString().isNotEmpty ?? false)
            ? d['description'].toString()
            : null,
        imageMediaId: (d['imageMediaId']?.toString().isNotEmpty ?? false)
            ? d['imageMediaId'].toString()
            : null,
        legacyImageUrl: (d['legacyImageUrl']?.toString().isNotEmpty ?? false)
            ? d['legacyImageUrl'].toString()
            : null,
        // REPLI SUR `legacyImageUrl`, PARCE QUE `displayImageUrl` EST NEUF.
        //
        // Contre un food-service pas encore redéployé, la clé est absente : sans ce
        // repli, aucune photo ne s'affichait — pas même celles des plats hérités,
        // qui marchaient AVANT ce changement. Une régression sur l'ancien serveur
        // est pire que l'absence de la nouveauté.
        imageUrl: (d['displayImageUrl']?.toString().isNotEmpty ?? false)
            ? d['displayImageUrl'].toString()
            : ((d['legacyImageUrl']?.toString().isNotEmpty ?? false)
                ? d['legacyImageUrl'].toString()
                : null),
        basePrice: Json.asDouble(d['basePrice']),
        currency: Json.str(d['currency'], AppConfig.defaultCurrency),
        isOrderable: Json.asBool(d['isOrderable']),

        // ═══════════════════════════════════════════════════════════════════════
        // LE REPLI SE FAIT SUR `imageMediaId` / `legacyImageUrl`, PAS SUR
        //    `displayImageUrl`.
        //
        // Première version de ce repli : `displayImageUrl != null`. Elle était
        // inutile — CE CHAMP EST AUSSI NOUVEAU. Contre un serveur pas encore
        // redéployé, les deux clés manquent, `hasImage` valait donc faux pour TOUS
        // les plats, la carte entière affichait « Photo manquante », et le bouton
        // principal proposait d'ajouter une photo à une route qui n'existe pas
        // encore — 404 à l'arrivée.
        //
        // Un repli doit s'appuyer sur ce qui existait AVANT le changement. Ici :
        // les deux champs que le contrat rendait déjà, et qui reproduisent
        // exactement la règle serveur `MenuItem.HasImage`.
        // ═══════════════════════════════════════════════════════════════════════
        hasImage: d.containsKey('hasImage')
            ? Json.asBool(d['hasImage'])
            : ((d['imageMediaId']?.toString().isNotEmpty ?? false) ||
                (d['legacyImageUrl']?.toString().isNotEmpty ?? false)),
        backAt: Json.asDate(d['backAtUtc']),
        optionGroups: Json.list(d['optionGroups']).map(DishOptionGroup.fromJson).toList(),
      );
}

/// ═════════════════════════════════════════════════════════════════════════════
/// CE QU'UN PLAT MONTRE AU RESTAURATEUR, EN UN SEUL MOT.
///
/// `isOrderable` NE SE TRADUIT PAS PAR « DISPONIBLE / INDISPONIBLE ».
///
/// Le serveur le met à faux pour quatre raisons distinctes, et l'écran les
/// affichait toutes comme « Indisponible ». Or elles n'appellent pas le même
/// geste :
///
///   • ÉPUISÉ AUJOURD'HUI se rétablit seul demain matin. Ne rien faire est la
///     bonne réponse.
///   • RETIRÉ DE LA CARTE attend une remise en vente explicite.
///   • HORS CRÉNEAU n'est pas un problème du tout : le menu du soir à 11 h est
///     normal. L'afficher en rouge ferait paniquer pour rien.
///   • PHOTO MANQUANTE attend un geste, et rien ne le dit tant qu'on n'a pas
///     `hasImage` : le restaurateur voyait « Indisponible » sur un plat qu'il
///     venait de créer, sans aucun moyen de comprendre.
///
/// C'est le même piège que « masquée » contre « hors créneau » sur les cartes :
/// deux causes, un seul mot, et un restaurateur qui attend que ça passe.
/// ═════════════════════════════════════════════════════════════════════════════
enum DishStatus {
  /// Vendable maintenant.
  enVente,

  /// Le serveur refuse la vente FAUTE DE PHOTO. Action requise.
  photoManquante,

  /// Épuisé pour la journée. Revient seul.
  epuiseAujourdhui,

  /// Retiré jusqu'à nouvel ordre. Ne revient pas seul.
  retire,
}

extension DishStatusX on Dish {
  /// L'ORDRE DES TESTS EST LA RÈGLE MÉTIER, PAS UNE COMMODITÉ.
  ///
  /// La photo passe AVANT l'épuisement : un plat sans photo ET épuisé doit
  /// afficher « photo manquante », parce que c'est la seule des deux causes qui
  /// ne se réglera pas d'elle-même. L'inverse ferait attendre demain un plat qui
  /// ne reviendra jamais.
  ///
  /// ON NE DISTINGUE PAS « HORS CRÉNEAU » ICI, et c'est faute d'information :
  /// `isOrderable` fond déjà le créneau de la CARTE dans son calcul, et le plat
  /// ne porte pas ce motif. La carte, elle, le sait (`MenuCard.isServedNow`) —
  /// c'est à son niveau que l'écran doit le dire.
  DishStatus get status {
    if (!hasImage) return DishStatus.photoManquante;
    if (isOrderable) return DishStatus.enVente;

    // `backAt` non nul = une échéance existe, donc « épuisé aujourd'hui ». Sans
    // échéance, c'est un retrait jusqu'à nouvel ordre. C'est exactement ce que
    // `ItemAvailability.UnavailableUntilUtc` encode côté serveur.
    return backAt != null ? DishStatus.epuiseAujourdhui : DishStatus.retire;
  }
}

/// Une section de carte (`MenuSectionView`) — ce que la maquette appelle
/// « catégorie ».
class MenuSection {
  const MenuSection({
    required this.id,
    required this.name,
    required this.description,
    required this.isActive,
    required this.items,
  });

  final String id;
  final String name;
  final String? description;
  final bool isActive;
  final List<Dish> items;

  factory MenuSection.fromJson(Map d) => MenuSection(
        id: Json.str(d['id']),
        name: Json.str(d['name']),
        description: (d['description']?.toString().isNotEmpty ?? false)
            ? d['description'].toString()
            : null,
        isActive: Json.asBool(d['isActive']),
        items: Json.list(d['items']).map(Dish.fromJson).toList(),
      );
}

/// Une carte (`MenuView`).
///
/// LA CARTE EST À DEUX NIVEAUX : CARTE → SECTION → PLAT.
///
/// La maquette n'en montre qu'un — une barre de catégories (« Populaires »,
/// « Plats », « Boissons », « Desserts ») au-dessus d'une liste de plats. Le
/// domaine en a deux : un restaurant peut avoir une carte « Midi » et une carte
/// « Soir », chacune avec ses sections et son créneau horaire. Aplatir les deux
/// niveaux ferait disparaître les créneaux et mélangerait les deux services.
class Menu {
  const Menu({
    required this.id,
    required this.name,
    required this.description,
    required this.isActive,
    required this.isServedNow,
    required this.servedFrom,
    required this.servedUntil,
    required this.sections,
  });

  final String id;
  final String name;
  final String? description;
  final bool isActive;

  /// La carte est-elle dans son créneau en ce moment ?
  final bool isServedNow;

  /// « HH:mm ». `null` = servie en permanence.
  final String? servedFrom;
  final String? servedUntil;

  final List<MenuSection> sections;

  factory Menu.fromJson(Map d) => Menu(
        id: Json.str(d['id']),
        name: Json.str(d['name'], 'Carte'),
        description: (d['description']?.toString().isNotEmpty ?? false)
            ? d['description'].toString()
            : null,
        isActive: Json.asBool(d['isActive']),
        isServedNow: Json.asBool(d['isServedNow']),
        servedFrom: (d['servedFrom']?.toString().isNotEmpty ?? false)
            ? d['servedFrom'].toString()
            : null,
        servedUntil: (d['servedUntil']?.toString().isNotEmpty ?? false)
            ? d['servedUntil'].toString()
            : null,
        sections: Json.list(d['sections']).map(MenuSection.fromJson).toList(),
      );
}

/// La carte complète d'un restaurant (`RestaurantMenuView`).
class RestaurantMenu {
  const RestaurantMenu({
    required this.restaurantId,
    required this.name,
    required this.acceptsOrdersNow,
    required this.blockedReason,
    required this.preparationMinutes,
    required this.menus,
  });

  final String restaurantId;
  final String name;
  final bool acceptsOrdersNow;
  final String blockedReason;
  final int preparationMinutes;
  final List<Menu> menus;

  /// Plats indisponibles, toutes cartes confondues. Compté ici plutôt que lu :
  /// le contrat ne porte aucun compteur, mais la donnée est exacte puisque
  /// l'audience « Owner » rend TOUT, y compris les plats épuisés.
  int get unavailableDishes => menus
      .expand((m) => m.sections)
      .expand((s) => s.items)
      .where((i) => !i.isOrderable)
      .length;

  factory RestaurantMenu.fromJson(Map d) => RestaurantMenu(
        restaurantId: Json.str(d['restaurantId']),
        name: Json.str(d['name'], 'Mon restaurant'),
        acceptsOrdersNow: Json.asBool(d['acceptsOrdersNow']),
        blockedReason: Json.str(d['blockedReason']),
        preparationMinutes: Json.asInt(d['preparationMinutes']),
        menus: Json.list(d['menus']).map(Menu.fromJson).toList(),
      );
}

/// ═════════════════════════════════════════════════════════════════════════════
/// LA CARTE — food-service, `/api/food/partner/restaurants/{id}/…`.
///
/// LA LECTURE PARTENAIRE MONTRE TOUT, LA VITRINE PUBLIQUE FILTRE.
///
/// `GET /api/food/partner/restaurants/{id}/menu` interroge l'audience `Owner` :
/// cartes hors créneau, sections masquées et plats épuisés sont rendus. C'est
/// exactement ce qu'il faut à un restaurateur — et c'est ce qui permet de
/// compter les plats indisponibles. Ne PAS utiliser la route publique
/// `/api/food/restaurants/{id}/menu`, qui les cache.
///
/// LA CARTE N'EST PLUS EN CRÉATION SEULE — CE QUI RESTE FERMÉ EST DÉLIBÉRÉ.
///
/// Jusqu'à VEN5, food-service portait soixante-dix commandes applicatives pour
/// vingt-sept routes : un restaurateur pouvait CRÉER une carte, une section, un
/// plat, une option, et CHANGER UN PRIX. Rien d'autre. Ni renommer, ni
/// supprimer, ni réordonner, ni marquer un plat épuisé, ni fermer un moment.
/// Le domaine était complet ; seule la couche HTTP manquait.
///
/// Douze routes ont été ouvertes. Restent volontairement fermées, faute d'écran
/// qui les demande : déplacer une section ou un plat vers une autre carte, les
/// créneaux de service (`SetMenuWindow`), la photo d'un plat, et le retrait
/// d'options. Les commandes existent — les rouvrir ne demandera qu'une ligne.
/// ═════════════════════════════════════════════════════════════════════════════

/// Les trois positions de l'interrupteur de disponibilité d'un plat.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// [soldOutToday] ET [unavailable] NE SONT PAS SYNONYMES, ET LES CONFONDRE
///    COÛTE CHER DANS LES DEUX SENS.
///
/// Marquer « indisponible » ce qui n'était qu'épuisé du jour fait disparaître un
/// plat de la vitrine jusqu'à ce que quelqu'un s'en aperçoive — parfois des
/// semaines. L'inverse remet en vente, le lendemain matin, un plat qu'on avait
/// retiré exprès.
///
/// L'écran doit donc présenter DEUX gestes distincts, et non un seul
/// interrupteur : « épuisé aujourd'hui » et « retirer de la carte ».
/// ═════════════════════════════════════════════════════════════════════════════
enum DishAvailability {
  /// En vente.
  available('available'),

  /// Épuisé pour le service en cours. REVIENT SEUL : l'échéance est calculée
  /// par food-service depuis les horaires de l'établissement, pas par
  /// l'application. Demander une date à un cuisinier en plein coup de feu, sur
  /// un téléphone, garantirait qu'il ne le fasse pas.
  soldOutToday('sold_out_today'),

  /// Retiré de la carte jusqu'à nouvel ordre. NE revient PAS seul.
  unavailable('unavailable');

  const DishAvailability(this.wire);

  /// Le code attendu par `PUT .../items/{id}/availability`. Le serveur rend
  /// 400 sur toute autre valeur — il ne se replie PAS sur « disponible », ce qui
  /// remettrait en vente un plat qu'on venait de retirer.
  final String wire;
}
class MenuApi extends ApiBase {
  const MenuApi(super.dio);

  static const _partner = '${AppConfig.food}/partner';

  /// Le restaurant du compte connecté.
  Future<PartnerRestaurant> me() => guard(() async {
        final resp = await dio.get('$_partner/me');
        return PartnerRestaurant.fromJson(Json.map(resp.data));
      });

  /// La carte, vue restaurateur. Exige la permission `restaurant.menu.manage`.
  Future<RestaurantMenu> menu(String restaurantId) => guard(() async {
        final resp = await dio.get('$_partner/restaurants/$restaurantId/menu');
        return RestaurantMenu.fromJson(Json.map(resp.data));
      });

  /// Crée une carte. Le CRÉNEAU n'est pas exposé : `CreateMenuRequest(Name,
  /// DisplayOrder)` seulement. Une carte créée depuis l'application est donc
  /// servie en permanence, et rien ne permet encore de la restreindre au midi.
  Future<String> createMenu(String restaurantId, {required String name, int displayOrder = 0}) =>
      guard(() async {
        final resp = await dio.post(
          '$_partner/restaurants/$restaurantId/menus',
          data: {'name': name, 'displayOrder': displayOrder},
        );
        return _createdId(resp.data, 'la carte');
      });

  /// Crée une section dans une carte.
  Future<String> createSection(
    String restaurantId, {
    required String menuId,
    required String name,
    int displayOrder = 0,
  }) =>
      guard(() async {
        final resp = await dio.post(
          '$_partner/restaurants/$restaurantId/menus/$menuId/categories',
          data: {'name': name, 'displayOrder': displayOrder},
        );
        return _createdId(resp.data, 'la section');
      });

  /// Crée un plat dans une section.
  ///
  /// SEULS LE NOM ET LE PRIX SONT ACCEPTÉS. `CreateMenuItemRequest(Name,
  /// BasePrice)` : ni description, ni photo, ni temps de préparation. L'assistant
  /// de création en quatre étapes ne peut donc enregistrer que la première.
  Future<String> createDish(
    String restaurantId, {
    required String sectionId,
    required String name,
    required double basePrice,
  }) =>
      guard(() async {
        final resp = await dio.post(
          '$_partner/restaurants/$restaurantId/categories/$sectionId/items',
          data: {'name': name, 'basePrice': basePrice},
        );
        return _createdId(resp.data, 'le plat');
      });

  /// Change le prix d'un plat.
  ///
  /// CE COMMENTAIRE DISAIT « LE SEUL CHAMP MODIFIABLE PAR HTTP ». Ce n'est plus
  /// vrai depuis #214 : [updateDish] porte le nom et la description, [setDishImage]
  /// la photo. Le prix garde sa route à lui parce que le domaine traite le
  /// changement de prix comme un fait commercial distinct — pas par manque d'autre
  /// chose.
  Future<void> changeDishPrice(
    String restaurantId, {
    required String dishId,
    required double basePrice,
  }) =>
      guard(() async {
        await dio.put(
          '$_partner/restaurants/$restaurantId/items/$dishId/price',
          data: {'basePrice': basePrice},
        );
      });

  Future<String> addOptionGroup(
    String restaurantId, {
    required String dishId,
    required String name,
    int minSelections = 0,
    int maxSelections = 1,
    int displayOrder = 0,
  }) =>
      guard(() async {
        final resp = await dio.post(
          '$_partner/restaurants/$restaurantId/items/$dishId/option-groups',
          data: {
            'name': name,
            'minSelections': minSelections,
            'maxSelections': maxSelections,
            'displayOrder': displayOrder,
          },
        );
        return _createdId(resp.data, "le groupe d'options");
      });

  Future<String> addOption(
    String restaurantId, {
    required String dishId,
    required String groupId,
    required String name,
    double priceDelta = 0,
  }) =>
      guard(() async {
        final resp = await dio.post(
          '$_partner/restaurants/$restaurantId/items/$dishId/option-groups/$groupId/options',
          data: {'name': name, 'priceDelta': priceDelta},
        );
        return _createdId(resp.data, "l'option");
      });

  // ── Édition de la carte ────────────────────────────────────────────────────
  //
  // TROIS MÉTHODES PAR NIVEAU, ET NON UN `rename(targetId)` UNIVERSEL.
  //
  // Ces méthodes remplacent quatre bouchons `NotMigrated` dont deux —
  // `rename(targetId)` et `delete(targetId)` — prétendaient traiter les trois
  // niveaux d'un seul geste. C'était déjà faux avant d'avoir une route : un
  // identifiant nu ne dit pas si l'on tient une carte, une section ou un plat,
  // et les trois chemins HTTP diffèrent. Le bouchon aurait été rebranché sur
  // l'un des trois, en silence.

  Future<void> renameMenu(
    String restaurantId, {
    required String menuId,
    required String name,
    String? description,
  }) =>
      guard(() async {
        await dio.put(
          '$_partner/restaurants/$restaurantId/menus/$menuId',
          data: {'name': name, 'description': description},
        );
      });

  /// Masque une carte de la vitrine SANS la supprimer.
  Future<void> setMenuVisible(String restaurantId, {required String menuId, required bool active}) =>
      guard(() async {
        await dio.put(
          '$_partner/restaurants/$restaurantId/menus/$menuId/visibility',
          data: {'active': active},
        );
      });

  /// Supprime une carte.
  ///
  /// REFUSÉE PAR 409 TANT QUE LA CARTE PORTE DES SECTIONS. Ce n'est pas une
  /// panne : les sections référencent la carte sans lui appartenir, et la
  /// supprimer les orphelinerait — elles disparaîtraient de la vue du client
  /// comme de celle du restaurateur, avec tous leurs plats. L'écran doit
  /// présenter ce refus comme une marche à suivre : videz d'abord la carte.
  Future<void> deleteMenu(String restaurantId, {required String menuId}) => guard(() async {
        await dio.delete('$_partner/restaurants/$restaurantId/menus/$menuId');
      });

  Future<void> renameSection(
    String restaurantId, {
    required String sectionId,
    required String name,
    String? description,
  }) =>
      guard(() async {
        await dio.put(
          '$_partner/restaurants/$restaurantId/categories/$sectionId',
          data: {'name': name, 'description': description},
        );
      });

  Future<void> setSectionVisible(
    String restaurantId, {
    required String sectionId,
    required bool active,
  }) =>
      guard(() async {
        await dio.put(
          '$_partner/restaurants/$restaurantId/categories/$sectionId/visibility',
          data: {'active': active},
        );
      });

  /// Déplace une section dans l'ordre d'affichage.
  ///
  /// UNE SECTION À LA FOIS, jamais la liste entière : deux membres du
  /// personnel qui réordonnent la carte en même temps ne doivent pas s'écraser.
  Future<void> moveSection(
    String restaurantId, {
    required String sectionId,
    required int displayOrder,
  }) =>
      guard(() async {
        await dio.put(
          '$_partner/restaurants/$restaurantId/categories/$sectionId/position',
          data: {'displayOrder': displayOrder},
        );
      });

  Future<void> deleteSection(String restaurantId, {required String sectionId}) => guard(() async {
        await dio.delete('$_partner/restaurants/$restaurantId/categories/$sectionId');
      });

  /// Nom, description et rang d'un plat. PAS le prix — il a sa propre route,
  /// [changeDishPrice], parce qu'il a sa propre invariante côté domaine.
  Future<void> updateDish(
    String restaurantId, {
    required String dishId,
    required String name,
    String? description,

    /// `null` LAISSE LE RANG INCHANGÉ, ET C'EST LE DÉFAUT VOULU.
    ///
    /// La signature disait `int displayOrder = 0`, donc tout appel qui ne s'en
    /// souciait pas — les deux existants — renvoyait 0 et remontait le plat en tête
    /// de sa section à chaque correction de libellé. Le serveur ignore désormais un
    /// rang absent ; le paramètre reste pour l'écran de réordonnancement à venir.
    int? displayOrder,
  }) =>
      guard(() async {
        await dio.put(
          '$_partner/restaurants/$restaurantId/items/$dishId',
          data: {
            'name': name,
            'description': description,
            // ON N'ENVOIE PAS LA CLÉ QUAND ELLE EST NULLE. `'displayOrder': null`
            // se lie à `null` côté serveur, donc au bon comportement — mais
            // l'omettre est plus honnête, et résiste à un binder plus strict.
            if (displayOrder != null) 'displayOrder': displayOrder,
          },
        );
      });

  /// Change la disponibilité d'un plat — le geste le plus fréquent du métier.
  ///
  /// TROIS ÉTATS, PAS UN BOOLÉEN. La signature précédente prenait
  /// `required bool available`, ce qui rendait « épuisé aujourd'hui »
  /// inexprimable — or c'est justement l'état utile en plein service. Voir
  /// [DishAvailability] pour ce qui les sépare.
  /// Rattache une photo à un plat, ou la retire (les deux nuls).
  ///
  /// ═══════════════════════════════════════════════════════════════════════════
  /// DEUX APPELS, DANS CET ORDRE : DÉPOSER, PUIS RATTACHER.
  ///
  /// Le fichier part d'abord sur media-service (`MediaKind.restaurantMedia`,
  /// `MediaOwner.menuItem` — voir `media_upload.dart`), qui rend un identifiant ET
  /// une URL. Les deux sont transmis ici.
  ///
  /// CORRECTION D'UN COMMENTAIRE QUI DISAIT L'INVERSE.
  ///
  /// Cette méthode a d'abord affirmé que « food-service refuse une URL ». C'était
  /// vrai de la signature de l'époque et faux du raisonnement : l'URL n'est pas
  /// « une adresse tierce », c'est celle que NOTRE service média vient de rendre,
  /// et l'identifiant reste stocké à côté. Voir `MenuItem.ImagePublicUrl` pour ce
  /// que coûterait l'alternative — un appel gRPC vers media-service sur chaque
  /// affichage de carte.
  ///
  /// LES DEUX CHAMPS, ET LE SERVEUR L'EXIGE PAR SA SIGNATURE.
  ///
  /// `mediaId` sans `url` donne un plat dont on sait qu'il a une photo et qu'on ne
  /// peut pas afficher ; `url` sans `mediaId` donne un plat qui paraît complet et
  /// que le serveur refuse de vendre — LA PHOTO EST OBLIGATOIRE POUR VENDRE, et
  /// c'est `imageMediaId` qui en décide.
  ///
  /// L'URL est celle que `MediaApi.uploadBytes` a lue dans la réponse du dépôt.
  /// Ne jamais la fabriquer à la main.
  Future<void> setDishImage(
    String restaurantId, {
    required String dishId,
    required String? mediaId,
    required String? url,
  }) =>
      guard(() async {
        await dio.put(
          '$_partner/restaurants/$restaurantId/items/$dishId/image',
          data: {'imageMediaId': mediaId, 'imagePublicUrl': url},
        );
      });

  Future<void> setDishAvailability(
    String restaurantId, {
    required String dishId,
    required DishAvailability state,
  }) =>
      guard(() async {
        await dio.put(
          '$_partner/restaurants/$restaurantId/items/$dishId/availability',
          data: {'state': state.wire},
        );
      });

  /// Supprime définitivement un plat.
  ///
  /// CE N'EST PAS « ARRÊTER DE LE VENDRE » : pour cela,
  /// [DishAvailability.unavailable], qui le garde en base prêt à revenir. La
  /// suppression sert aux erreurs de saisie — le doublon, la faute de frappe.
  Future<void> deleteDish(String restaurantId, {required String dishId}) => guard(() async {
        await dio.delete('$_partner/restaurants/$restaurantId/items/$dishId');
      });

  // ── Interruption du service ────────────────────────────────────────────────

  /// Suspend la prise de commande pour [minutes] minutes.
  ///
  /// LA DURÉE EST OBLIGATOIRE, ET C'EST UNE PROTECTION. Une fermeture sans
  /// échéance qu'on oublie de lever retire l'établissement de la vitrine pour la
  /// soirée entière sans que personne s'en aperçoive. Un coup de feu ou une
  /// panne de gaz durent un temps que le restaurateur sait estimer ; passé ce
  /// délai, le service reprend seul.
  Future<void> pauseService(String restaurantId, {required int minutes}) => guard(() async {
        await dio.post(
          '$_partner/restaurants/$restaurantId/pause',
          data: {'minutes': minutes},
        );
      });

  Future<void> resumeService(String restaurantId) => guard(() async {
        await dio.post('$_partner/restaurants/$restaurantId/resume');
      });

  static String _createdId(dynamic data, String what) {
    final id = Json.str(Json.map(data)['id']);
    if (id.isEmpty) {
      throw ApiException("Création réussie, mais l'identifiant de $what est absent de la réponse.");
    }
    return id;
  }
}

final menuApiProvider = Provider<MenuApi>((ref) => MenuApi(ref.watch(dioProvider)));

/// Le restaurant du compte connecté.
///
/// NE PAS CONFONDRE AVEC `sellerIdentityProvider`. Un compte peut être vendeur
/// (boutique) ET partenaire Food (restaurant) : ce sont deux identités distinctes,
/// résolues par deux services, avec deux rôles de jeton différents.
final partnerRestaurantProvider =
    FutureProvider<PartnerRestaurant>((ref) => ref.watch(menuApiProvider).me());

final restaurantMenuProvider = FutureProvider.family<RestaurantMenu, String>(
    (ref, restaurantId) => ref.watch(menuApiProvider).menu(restaurantId));
