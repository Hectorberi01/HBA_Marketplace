import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:package_info_plus/package_info_plus.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../auth/application/auth_controller.dart';
import '../../shared/utils/formatters.dart';

class Profile {
  Profile({
    required this.firstName,
    required this.lastName,
    required this.email,
    required this.phoneNumber,
    required this.acceptedTermsVersion,
    this.mfaEnabled = false,
  });

  final String firstName;
  final String lastName;
  final String email;
  final String phoneNumber;

  /// Version des conditions générales acceptée par ce compte. Null = jamais rien
  /// accepté (compte neuf, ou créé avant la mise en place du dispositif).
  final String? acceptedTermsVersion;

  /// Double authentification (TOTP) active sur ce compte.
  final bool mfaEnabled;

  String get fullName => '$firstName $lastName'.trim();

  factory Profile.fromJson(Map d) => Profile(
        firstName: Json.str(d['firstName']),
        lastName: Json.str(d['lastName']),
        email: Json.str(d['email']),
        phoneNumber: Json.str(d['phoneNumber'] ?? d['phone']),
        acceptedTermsVersion: (d['acceptedTermsVersion']?.toString().isNotEmpty ?? false)
            ? d['acceptedTermsVersion'].toString()
            : null,
        mfaEnabled: d['mfaEnabled'] == true,
      );
}

class WishlistItem {
  WishlistItem({
    required this.productId,
    required this.name,
    required this.imageUrl,
    required this.price,
    required this.currency,
    this.priceAlert = false,
    this.stockAlert = false,
  });
  final String productId;
  final String name;
  final String? imageUrl;
  final double price;
  final String currency;

  /// Alerte baisse de prix / retour en stock activée pour ce favori.
  final bool priceAlert;
  final bool stockAlert;

  factory WishlistItem.fromJson(Map d) => WishlistItem(
        productId: Json.str(d['productId'] ?? d['id']),
        name: Json.str(d['productName'] ?? d['name'], 'Produit'),
        imageUrl: (d['imageUrl'] ?? d['thumbnailUrl'])?.toString(),
        price: Json.asDouble(d['price'] ?? d['minPrice']),
        currency: Json.str(d['currency'], AppConfig.defaultCurrency),
        priceAlert: Json.asBool(d['priceAlert']),
        stockAlert: Json.asBool(d['stockAlert']),
      );
}

class NotificationItem {
  NotificationItem({required this.id, required this.subject, required this.body, required this.channel, required this.createdAt, required this.read});
  final String id;
  final String subject;
  final String body;
  final String channel;
  final DateTime? createdAt;
  final bool read;

  factory NotificationItem.fromJson(Map d) => NotificationItem(
        id: Json.str(d['id'] ?? d['notificationId']),
        subject: Json.str(d['subject'] ?? d['title'], 'Notification'),
        body: Json.str(d['body'] ?? d['message']),
        channel: Json.str(d['channel'], 'InApp'),
        createdAt: Json.asDate(d['createdAtUtc'] ?? d['createdAt']),
        read: Json.str(d['status']).toLowerCase() == 'read' || Json.asBool(d['read']),
      );
}

/// Amorçage MFA (TOTP) : le secret à enregistrer dans une app d'authentification
/// (Google Authenticator, etc.) et l'URI otpauth (QR) équivalente.
class MfaSetup {
  MfaSetup({required this.secret, required this.otpAuthUri});
  final String secret;
  final String otpAuthUri;
  factory MfaSetup.fromJson(Map d) => MfaSetup(
        secret: Json.str(d['secret']),
        otpAuthUri: Json.str(d['otpAuthUri'] ?? d['otpauthUri'] ?? d['uri']),
      );
}

/// Préférence de notification pour une catégorie (clé alignée sur le backend).
class NotifPref {
  NotifPref({required this.key, required this.enabled});

  final String key;
  final bool enabled;

  factory NotifPref.fromJson(Map d) =>
      NotifPref(key: Json.str(d['key']), enabled: Json.asBool(d['enabled'], true));
}

class AccountApi {
  AccountApi(this._dio);
  final Dio _dio;
  static const _p = AppConfig.apiPrefix;

  /// Initie l'activation MFA : renvoie le secret TOTP + l'URI otpauth.
  Future<MfaSetup> mfaSetup() => _wrap(() async {
        final resp = await _dio.post('$_p/account/me/mfa/setup');
        return MfaSetup.fromJson(resp.data as Map);
      });

  /// Confirme l'activation avec un premier code de l'app d'authentification.
  Future<void> mfaConfirm(String code) => _wrap(() async {
        await _dio.post('$_p/account/me/mfa/confirm', data: {'code': code});
      });

  /// Désactive la MFA (exige un code valide).
  Future<void> mfaDisable(String code) => _wrap(() async {
        await _dio.post('$_p/account/me/mfa/disable', data: {'code': code});
      });

  Future<Profile> profile() => _wrap(() async {
        final resp = await _dio.get('$_p/account/profile');
        return Profile.fromJson(resp.data as Map);
      });

  /// Enregistre l'acceptation d'une VERSION précise des conditions.
  ///
  /// On envoie la version réellement affichée — celle que cette app embarque.
  /// Laisser le serveur consigner « la version courante » reviendrait à faire
  /// signer un texte qu'on n'a pas montré.
  Future<void> acceptTerms(String version) => _wrap(() async {
        await _dio.post('$_p/account/accept-terms', data: {'version': version});
      });

  Future<void> updateProfile(String firstName, String lastName, String phone) => _wrap(() async {
        await _dio.put('$_p/account/profile',
            data: {'firstName': firstName, 'lastName': lastName, 'phoneNumber': phone});
      });

  Future<List<WishlistItem>> wishlist() => _wrap(() async {
        final resp = await _dio.get('$_p/wishlist');
        final data = resp.data;
        final items = data is Map ? (data['items'] ?? data['products']) : data;
        return Json.list(items).map(WishlistItem.fromJson).toList();
      });

  Future<void> addWishlist(String productId) => _wrap(() async {
        await _dio.post('$_p/wishlist/items',
            data: {'productId': productId, 'offerId': null, 'priceAlert': false, 'stockAlert': false});
      });

  Future<void> removeWishlist(String productId) => _wrap(() async {
        await _dio.delete('$_p/wishlist/items/$productId');
      });

  /// Active/désactive les alertes (baisse de prix, retour en stock) d'un favori.
  Future<void> setWishlistAlerts(String productId, {required bool priceAlert, required bool stockAlert}) =>
      _wrap(() async {
        await _dio.put('$_p/wishlist/items/$productId/alerts',
            data: {'priceAlert': priceAlert, 'stockAlert': stockAlert});
      });

  Future<List<NotificationItem>> notifications() => _wrap(() async {
        final resp = await _dio.get('$_p/notifications');
        final data = resp.data;
        final items = data is Map ? (data['items'] ?? data['notifications']) : data;
        return Json.list(items).map(NotificationItem.fromJson).toList();
      });

  Future<void> markNotificationRead(String id) => _wrap(() async {
        await _dio.patch('$_p/notifications/$id/read');
      });

  /// Supprime une notification (balayage vers la gauche).
  Future<void> deleteNotification(String id) => _wrap(() async {
        await _dio.delete('$_p/notifications/$id');
      });

  /// Préférences de notification : état activé/coupé par catégorie de push.
  Future<List<NotifPref>> notificationPreferences() => _wrap(() async {
        final resp = await _dio.get('$_p/notifications/preferences');
        final data = resp.data;
        final cats = Json.list(data is Map ? data['categories'] : data);
        return cats.map(NotifPref.fromJson).toList();
      });

  /// Envoie la liste des catégories dont le push est coupé (opt-out).
  Future<void> updateNotificationPreferences(List<String> mutedCategories) => _wrap(() async {
        await _dio.put('$_p/notifications/preferences', data: {'mutedCategories': mutedCategories});
      });

  /// Enregistre le jeton FCM de CET appareil sur le compte connecté.
  ///
  /// Le chemin est `/mobile/devices`, et NON `/mobile/account/devices` : le
  /// groupe du BFF Mobile est déjà préfixé par `/mobile`, et l'endpoint y est
  /// déclaré à la racine du groupe (MobileAccountEndpoints). Se tromper ici
  /// donnerait un 404 silencieux — et le client ne recevrait jamais aucun push,
  /// sans le moindre signe que quelque chose cloche.
  ///
  /// Sans cet appel, le serveur ne connaît AUCUN appareil pour cet acheteur : il
  /// crée la notification, cherche où l'envoyer, ne trouve rien, et abandonne.
  /// C'était exactement l'état de l'application.
  Future<void> registerDevice({required String token, required String platform}) => _wrap(() async {
        await _dio.post('$_p/devices', data: {'token': token, 'platform': platform});
      });

  /// Retire le jeton à la déconnexion. Indispensable : sans cela, l'appareil
  /// continuerait de recevoir les notifications du compte précédent.
  Future<void> unregisterDevice(String token) => _wrap(() async {
        await _dio.post('$_p/devices/unregister', data: {'token': token});
      });

  /// Supprime définitivement le compte (anonymisation côté serveur).
  ///
  /// ─────────────────────────────────────────────────────────────────────────────
  /// EXIGÉ PAR APPLE (Guideline 5.1.1(v)) dès lors que l'application permet de créer
  /// un compte. Sans ce parcours, l'app est REJETÉE à la soumission — et la politique
  /// de confidentialité promettait déjà ce droit, sans qu'aucun écran ne l'offre.
  ///
  /// Le mot de passe est exigé : l'action est irréversible, et un téléphone
  /// déverrouillé laissé sur une table ne doit pas suffire à l'exécuter.
  ///
  /// Le serveur REFUSE (409) si une commande est en cours, un litige ouvert, ou si le
  /// compte est rattaché à une boutique. Le message d'erreur explique précisément ce
  /// qui bloque : on l'affiche tel quel, sans le reformuler.
  /// ─────────────────────────────────────────────────────────────────────────────
  Future<void> deleteAccount(String password) => _wrap(() async {
        await _dio.delete('$_p/account', data: {'password': password});
      });

  Future<T> _wrap<T>(Future<T> Function() fn) async {
    try {
      return await fn();
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final accountApiProvider = Provider<AccountApi>((ref) => AccountApi(ref.watch(dioProvider)));
/// Profil du compte connecté.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LE `watch` SUR LA SESSION EST ESSENTIEL — ce n'est pas une précaution de style.
///
/// Depuis l'ouverture du catalogue aux visiteurs (App Store 5.1.1), l'ACCUEIL est
/// public. Or l'accueil affiche « Bonjour, … » via `userNameProvider`, qui lit ce
/// profil. Un visiteur déclenchait donc cet appel, recevait un 401 — et Riverpod
/// CONSERVAIT cette erreur en cache.
///
/// Comme ce fournisseur n'observait pas la session, la connexion ne le recalculait
/// pas : l'écran « Modifier le profil » resservait indéfiniment « Session expirée »
/// à un utilisateur pourtant connecté. `userNameProvider` masquait le symptôme en
/// rattrapant l'exception, ce qui rendait la panne invisible jusqu'à cet écran.
///
/// Le `watch` fait repartir la requête à chaque changement d'état de session, et
/// le court-circuit visiteur évite le 401 (qui déclenchait au passage une tentative
/// de rafraîchissement, donc un effacement de session).
/// ─────────────────────────────────────────────────────────────────────────────
final profileProvider = FutureProvider<Profile>((ref) {
  if (ref.watch(authControllerProvider) != AuthStatus.authenticated) {
    throw ApiException('Connectez-vous pour accéder à votre profil.');
  }
  return ref.watch(accountApiProvider).profile();
});

class WishlistController extends AsyncNotifier<List<WishlistItem>> {
  @override
  Future<List<WishlistItem>> build() {
    // VISITEUR : aucune requête. Le catalogue est ouvert sans compte (App Store
    // 5.1.1) ; appeler l'API ici renverrait 401, ce qui déclencherait une tentative
    // de rafraîchissement puis un effacement de session — du bruit, pour une liste
    // qui est de toute façon vide sans compte.
    if (ref.watch(authControllerProvider) != AuthStatus.authenticated) {
      return Future.value(const <WishlistItem>[]);
    }
    return ref.watch(accountApiProvider).wishlist();
  }

  Future<void> add(String productId) async {
    await ref.read(accountApiProvider).addWishlist(productId);
    ref.invalidateSelf();
    await future;
  }

  Future<void> remove(String productId) async {
    await ref.read(accountApiProvider).removeWishlist(productId);
    ref.invalidateSelf();
    await future;
  }

  /// Active/désactive les alertes prix/stock d'un favori, puis recharge la liste.
  Future<void> setAlerts(String productId, {required bool priceAlert, required bool stockAlert}) async {
    await ref.read(accountApiProvider).setWishlistAlerts(productId, priceAlert: priceAlert, stockAlert: stockAlert);
    ref.invalidateSelf();
    await future;
  }

  /// Bascule l'état favori d'un produit, de façon optimiste : l'UI reflète le
  /// changement immédiatement, et revient en arrière si l'appel réseau échoue.
  Future<void> toggle(String productId) async {
    final current = state.valueOrNull ?? const <WishlistItem>[];
    final isFav = current.any((w) => w.productId == productId);

    state = AsyncData(isFav
        ? current.where((w) => w.productId != productId).toList()
        : [
            ...current,
            WishlistItem(productId: productId, name: '', imageUrl: null, price: 0, currency: AppConfig.defaultCurrency),
          ]);

    try {
      if (isFav) {
        await ref.read(accountApiProvider).removeWishlist(productId);
      } else {
        await ref.read(accountApiProvider).addWishlist(productId);
      }
    } catch (e) {
      state = AsyncData(current); // rollback
      rethrow;
    }
  }
}

final wishlistControllerProvider =
    AsyncNotifierProvider<WishlistController, List<WishlistItem>>(WishlistController.new);

/// Ensemble des identifiants produits en favori (lookup rapide pour les cartes).
final favoriteIdsProvider = Provider<Set<String>>((ref) {
  final wl = ref.watch(wishlistControllerProvider);
  return wl.valueOrNull?.map((w) => w.productId).toSet() ?? <String>{};
});

final notificationsProvider = FutureProvider<List<NotificationItem>>((ref) {
  // VISITEUR : pas de boîte de réception (voir WishlistController.build).
  if (ref.watch(authControllerProvider) != AuthStatus.authenticated) {
    return Future.value(const <NotificationItem>[]);
  }
  return ref.watch(accountApiProvider).notifications();
});

/// Nombre de notifications NON LUES (dérivé du fil), pour la pastille de la cloche.
/// 0 pendant le chargement.
final unreadNotificationCountProvider = Provider<int>((ref) {
  final list = ref.watch(notificationsProvider).valueOrNull ?? const <NotificationItem>[];
  return list.where((n) => !n.read).length;
});

/// Même précaution que `profileProvider` : observer la session pour ne pas figer
/// un échec obtenu avant la connexion.
final notificationPreferencesProvider = FutureProvider<List<NotifPref>>((ref) {
  if (ref.watch(authControllerProvider) != AuthStatus.authenticated) {
    return Future.value(const <NotifPref>[]);
  }
  return ref.watch(accountApiProvider).notificationPreferences();
});

/// Version affichée dans le pied de page du compte, lue depuis le bundle réel
/// (plus de valeur codée en dur qui divergeait du pubspec).
final appVersionProvider = FutureProvider<String>((ref) async {
  final info = await PackageInfo.fromPlatform();
  return 'Version ${info.version} (Build ${info.buildNumber})';
});
