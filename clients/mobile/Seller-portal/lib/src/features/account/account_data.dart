import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_base.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

/// Le compte connecté, tel qu'identity-service le connaît (`UserSummary`).
class AccountMe {
  AccountMe({
    required this.firstName,
    required this.lastName,
    required this.email,
    required this.phoneNumber,
    required this.emailVerified,
    required this.mfaEnabled,
    required this.acceptedTermsVersion,
  });

  final String firstName;
  final String lastName;
  final String email;
  final String phoneNumber;

  /// `shopName` A DISPARU D'ICI, ET IL N'Y A JAMAIS ÉTÉ.
  ///
  /// Le modèle le lisait sur `GET /seller/account/me` du BFF du monolithe.
  /// `UserSummary` d'identity-service est un contrat de COMPTE : il ne sait rien
  /// des boutiques. La valeur retombait donc toujours sur « Ma boutique ».
  ///
  /// Le nom de la boutique se lit sur `GET /api/merchants/me` — voir
  /// `sellerNameProvider` dans `features/auth/application/auth_controller.dart`,
  /// qui a l'avantage de suivre un renommage sans attendre une reconnexion.
  final bool emailVerified;

  final bool mfaEnabled;

  /// Version des conditions générales acceptée par ce compte. Null = jamais rien
  /// accepté (compte neuf, ou compte créé avant la mise en place du dispositif).
  final String? acceptedTermsVersion;

  String get fullName => '$firstName $lastName'.trim();

  String get initials {
    final f = firstName.isEmpty ? '' : firstName[0];
    final l = lastName.isEmpty ? '' : lastName[0];
    final ini = '$f$l'.toUpperCase();
    return ini.isEmpty ? '?' : ini;
  }

  factory AccountMe.fromJson(Map d) => AccountMe(
        firstName: Json.str(d['firstName']),
        lastName: Json.str(d['lastName']),
        email: Json.str(d['email']),
        phoneNumber: Json.str(d['phoneNumber']),
        emailVerified: Json.asBool(d['emailVerified']),
        mfaEnabled: Json.asBool(d['mfaEnabled']),
        acceptedTermsVersion: (d['acceptedTermsVersion']?.toString().isNotEmpty ?? false)
            ? d['acceptedTermsVersion'].toString()
            : null,
      );
}

/// Notification reçue par le vendeur.
class SellerNotification {
  SellerNotification({
    required this.id,
    required this.title,
    required this.message,
    required this.createdAt,
    required this.readAt,
  });

  final String id;
  final String title;
  final String message;
  final DateTime? createdAt;
  final DateTime? readAt;

  bool get isRead => readAt != null;

  factory SellerNotification.fromJson(Map d) => SellerNotification(
        id: Json.str(d['id']),
        title: Json.str(d['subject'], 'Notification'),
        message: Json.str(d['body']),
        createdAt: Json.asDate(d['createdAtUtc']),
        readAt: Json.asDate(d['readAtUtc']),
      );
}

/// Données d'amorçage MFA (TOTP) : le secret à enregistrer dans une app
/// d'authentification, et l'URI otpauth (QR) équivalente.
class MfaSetup {
  MfaSetup({required this.secret, required this.otpAuthUri});

  final String secret;
  final String otpAuthUri;

  factory MfaSetup.fromJson(Map d) => MfaSetup(
        secret: Json.str(d['secret']),
        otpAuthUri: Json.str(d['otpAuthUri'] ?? d['otpauthUri'] ?? d['uri']),
      );
}

/// ═════════════════════════════════════════════════════════════════════════════
/// COMPTE ET NOTIFICATIONS — TROIS SERVICES, ET PLUS UN SEUL `/seller/account`.
///
///   • identité, mot de passe, double authentification, déconnexion
///     → identity-service, `/api/identity/account/me/*` (SANS réécriture : le
///       service porte déjà le préfixe `/api/identity`) ;
///   • boîte de réception, préférences, appareils push
///     → communication-service, `/api/notifications/*` (SANS réécriture) ;
///   • fermeture et réactivation du compte VENDEUR
///     → merchant-service, `/api/merchants/{sellerId}/*` — ce ne sont pas des
///       gestes de compte mais de BOUTIQUE, et c'est pourquoi ils exigent un
///       `sellerId`.
///
/// DEUX PRÉFIXES POUR L'AUTHENTIFICATION, ET CE N'EST PAS UNE INCOHÉRENCE.
/// `/api/auth/*` porte les gestes d'un VISITEUR (inscription, connexion, mot de
/// passe oublié) ; `/api/identity/account/me/*` ceux d'un compte OUVERT. Voir
/// `features/auth/data/auth_api.dart`.
/// ═════════════════════════════════════════════════════════════════════════════
class AccountApi extends ApiBase {
  const AccountApi(super.dio);

  static const _account = '${AppConfig.identity}/account/me';
  static const _notifications = AppConfig.notifications;
  static const _merchants = AppConfig.merchants;

  /// Nombre de notifications demandées.
  ///
  /// Contrairement au portefeuille, `take` est ici FACULTATIF (`int take = 50`)
  /// et borné par le serveur à 1..200 — hors bornes, il retombe à 50. On l'envoie
  /// quand même : le défaut d'un service n'est pas un contrat.
  static const int _notificationsPageSize = 50;

  /// Initie l'activation MFA : renvoie le secret TOTP + l'URI otpauth.
  Future<MfaSetup> mfaSetup() => guard(() async {
        final resp = await dio.post('$_account/mfa/setup');
        return MfaSetup.fromJson(Json.map(resp.data));
      });

  /// Confirme l'activation MFA avec un code de l'app d'authentification.
  Future<void> mfaConfirm(String code) => guard(() async {
        await dio.post('$_account/mfa/confirm', data: {'code': code});
      });

  /// Désactive la MFA (exige un code valide).
  Future<void> mfaDisable(String code) => guard(() async {
        await dio.post('$_account/mfa/disable', data: {'code': code});
      });

  Future<AccountMe> me() => guard(() async {
        final resp = await dio.get(_account);
        return AccountMe.fromJson(Json.map(resp.data));
      });

  /// LES TROIS CHAMPS SONT OBLIGATOIRES, Y COMPRIS LE TÉLÉPHONE.
  ///
  /// `UpdateProfileRequest(string FirstName, string LastName, string PhoneNumber)`
  /// les déclare non nullables, et cette route REMPLACE le profil. `phoneNumber`
  /// était ici optionnel : un écran qui ne modifiait que le nom envoyait `null` et
  /// effaçait le numéro du compte — celui-là même qui sert à joindre le vendeur
  /// pour une livraison.
  Future<void> updateProfile({
    required String firstName,
    required String lastName,
    required String phoneNumber,
  }) =>
      guard(() async {
        await dio.put(_account, data: {
          'firstName': firstName,
          'lastName': lastName,
          'phoneNumber': phoneNumber,
        });
      });

  /// Enregistre l'acceptation d'une version des conditions générales.
  ///
  /// `UserSummary` porte `AcceptedTermsVersion` et `AcceptedTermsOnUtc` : on
  /// savait LIRE ce qui avait été accepté, sans pouvoir l'écrire.
  /// `AcceptTermsCommand` était pourtant complète dans identity-service, sans un
  /// seul appelant. La route est ouverte (VEN10).
  ///
  /// [version] EST CELLE QU'ON A AFFICHÉE, PAS « LA DERNIÈRE ».
  ///
  /// C'est exactement le texte que le vendeur a eu sous les yeux. Envoyer une
  /// version que le serveur choisirait lui-même reviendrait à faire signer un
  /// document qu'on n'a pas montré — et le jour du litige, on ne saurait pas ce
  /// qui a été accepté.
  Future<void> acceptTerms(String version) => guard(() async {
        await dio.post('$_account/accept-terms', data: {'version': version});
      });

  Future<void> changePassword({required String currentPassword, required String newPassword}) =>
      guard(() async {
        await dio.post('$_account/change-password', data: {
          'currentPassword': currentPassword,
          'newPassword': newPassword,
        });
      });

  /// Ferme la BOUTIQUE : les produits sont retirés de la vente. Le compte
  /// subsiste.
  ///
  /// CE N'EST PAS UN GESTE DE COMPTE, ET IL EXIGE UN `sellerId`.
  ///
  /// La route est `POST /api/merchants/{sellerId}/close`, dans merchant-service,
  /// avec la garde de propriété habituelle. L'ancien
  /// `POST /seller/account/me/close` mélangeait identité et boutique — commode
  /// tant qu'un compte valait une boutique, faux dès qu'il en porte plusieurs.
  Future<void> closeShop(String sellerId) => guard(() async {
        await dio.post('$_merchants/$sellerId/close');
      });

  /// Demande la réactivation d'une boutique fermée (validation admin).
  ///
  /// LE CHEMIN EST `/reactivation`, PAS `/request-reactivation`.
  Future<void> requestReactivation(String sellerId) => guard(() async {
        await dio.post('$_merchants/$sellerId/reactivation');
      });

  /// ═══════════════════════════════════════════════════════════════════════════
  /// SUPPRESSION DÉFINITIVE DU COMPTE — `DELETE /api/identity/account/me`.
  ///
  /// EXIGENCE APP STORE 5.1.1(v), ET ELLE EST BLOQUANTE.
  ///
  /// Une application qui permet de créer un compte doit permettre de le
  /// supprimer EN LIBRE-SERVICE, depuis l'application. Renvoyer vers un
  /// formulaire web ou une adresse de courriel ne satisfait pas la règle. Tant
  /// que cette route n'existait pas, l'application ne pouvait pas être soumise —
  /// quel que soit l'état du reste.
  ///
  /// LE MOT DE PASSE VOYAGE DANS LE CORPS, PAS DANS L'URL.
  ///
  /// En paramètre de requête, il finirait dans les journaux d'accès de la
  /// passerelle, dans les traces OpenTelemetry et dans l'historique du proxy. Un
  /// `DELETE` avec corps n'a pas de sémantique définie par la RFC 9110 — mais
  /// elle ne l'interdit pas, et ici elle est claire : c'est la preuve d'identité
  /// qu'exige la commande.
  ///
  /// 401 `identity.account.wrong_password` N'EST PAS UNE SAISIE INVALIDE.
  /// L'écran doit le présenter comme un refus d'identité — « ce n'est pas votre
  /// mot de passe » — et non comme une erreur de formulaire.
  ///
  /// IDEMPOTENTE : un compte déjà supprimé rend 204. Un second appui ne doit
  /// pas faire croire que la première suppression a échoué.
  /// ═══════════════════════════════════════════════════════════════════════════
  /// `_account` PORTE DÉJÀ `/me` — voir sa déclaration. Écrire
  /// `'$_account/me'` produirait `/api/identity/account/me/me`, un chemin que
  /// rien ne sert et qui rendrait 404.
  Future<void> deleteAccount(String password) => guard(() async {
        await dio.delete(_account, data: {'password': password});
      });

  Future<List<SellerNotification>> notifications() => guard(() async {
        final resp = await dio.get(
          _notifications,
          queryParameters: {'take': _notificationsPageSize},
        );
        final items = Json.list(resp.data).map(SellerNotification.fromJson).toList();
        items.sort((a, b) => (b.createdAt ?? DateTime(0)).compareTo(a.createdAt ?? DateTime(0)));
        return items;
      });

  /// Compteur de non-lues, tel que le serveur le calcule.
  ///
  /// Plus fiable que de compter la page ramenée : `notifications()` ne rend que
  /// les 50 dernières, et un vendeur qui en a 80 non lues verrait « 50 ».
  Future<int> unreadCount() => guard(() async {
        final resp = await dio.get('$_notifications/unread-count');
        return Json.asInt(Json.map(resp.data)['count']);
      });

  Future<void> markRead(String id) => guard(() async => dio.post('$_notifications/$id/read'));

  Future<void> markAllRead() => guard(() async => dio.post('$_notifications/read-all'));

  /// Enregistre le jeton d'appareil (FCM) pour recevoir les push.
  Future<void> registerDevice({required String token, required String platform}) => guard(() async {
        await dio.post('$_notifications/devices', data: {'token': token, 'platform': platform});
      });

  /// Retire le jeton à la déconnexion.
  ///
  /// C'EST UN `DELETE` AVEC UN CORPS, ET IL N'Y A PAS DE `/unregister`.
  ///
  /// L'appel partait en `POST /devices/unregister`, un chemin qui n'existe pas :
  /// le désabonnement échouait en 404, silencieusement (l'appelant l'avale), et
  /// l'appareil continuait de recevoir les notifications du vendeur précédent —
  /// sur un téléphone partagé ou revendu, c'est une fuite de données.
  ///
  /// `UnregisterDeviceRequest(string Token)` voyage dans le CORPS d'un `DELETE`.
  /// Dio le permet via `data:` ; beaucoup de clients HTTP ne le font pas par
  /// défaut, d'où cette note.
  Future<void> unregisterDevice(String token) => guard(() async {
        await dio.delete('$_notifications/devices', data: {'token': token});
      });

  /// Préférences de notification.
  ///
  /// ELLES NE PILOTENT QUE LE PUSH, ET IL N'Y A PAS D'AXE PAR CANAL.
  ///
  /// `NotificationPreference` ne stocke qu'une liste de catégories MUETTES : pas
  /// de réglage e-mail/SMS/in-app par catégorie. Un écran qui proposerait trois
  /// interrupteurs par ligne en ferait deux qui n'ont aucun effet.
  ///
  /// Les cinq clés sont fixes et l'ordre est garanti par le serveur
  /// (`NotificationCategories.All`) : `orders`, `returns`, `reviews`,
  /// `messages`, `account`. La liste revient TOUJOURS complète, même sans
  /// enregistrement en base.
  Future<List<NotifPref>> notificationPreferences() => guard(() async {
        final resp = await dio.get('$_notifications/preferences');
        final cats = Json.list(Json.map(resp.data)['categories']);
        return cats.map(NotifPref.fromJson).toList();
      });

  /// LECTURE POSITIVE, ÉCRITURE NÉGATIVE : IL FAUT INVERSER.
  ///
  /// La lecture rend `{ key, enabled }` ; l'écriture attend
  /// `{ mutedCategories: [...] }`, c'est-à-dire l'inverse. Renvoyer telles
  /// quelles les clés activées couperait exactement ce que le vendeur venait
  /// d'allumer. C'est à l'appelant de faire la bascule.
  Future<void> updateNotificationPreferences(List<String> mutedCategories) => guard(() async {
        await dio.put('$_notifications/preferences', data: {'mutedCategories': mutedCategories});
      });
}

final accountApiProvider = Provider<AccountApi>((ref) => AccountApi(ref.watch(dioProvider)));

final meProvider = FutureProvider<AccountMe>((ref) => ref.watch(accountApiProvider).me());

final notificationsProvider =
    FutureProvider<List<SellerNotification>>((ref) => ref.watch(accountApiProvider).notifications());

/// Préférence d'une catégorie de notification (push activé ou coupé).
class NotifPref {
  NotifPref({required this.key, required this.enabled});
  final String key;
  final bool enabled;

  factory NotifPref.fromJson(Map d) => NotifPref(
        key: Json.str(d['key']),
        enabled: Json.asBool(d['enabled']),
      );
}

final notificationPreferencesProvider =
    FutureProvider<List<NotifPref>>((ref) => ref.watch(accountApiProvider).notificationPreferences());

/// Non-lues — badge de la tuile Notifications.
///
/// COMPTÉ PAR LE SERVEUR, PAS DANS LA PAGE RAMENÉE.
///
/// La version précédente filtrait la liste locale. Or `GET /api/notifications`
/// n'en rend que les 50 dernières : un vendeur avec 80 non lues voyait « 50 »,
/// et le badge cessait de bouger. `GET /api/notifications/unread-count` compte
/// sur la base entière.
final unreadNotificationsProvider = FutureProvider<int>(
    (ref) => ref.watch(accountApiProvider).unreadCount());
