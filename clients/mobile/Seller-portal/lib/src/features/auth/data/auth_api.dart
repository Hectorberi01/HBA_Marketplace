import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/config/app_config.dart';
import '../../../core/network/api_exception.dart';
import '../../../core/providers/core_providers.dart';
import '../../../shared/utils/formatters.dart';

/// Jetons renvoyés par l'authentification.
///
/// PLUS DE `name` : LA CONNEXION N'EN A JAMAIS RENVOYÉ.
///
/// Le champ existait et cherchait `sellerName`, `shopName`, `fullName`. Or
/// `POST /api/auth/login` répond `{ mfaRequired, tokens: { accessToken,
/// accessTokenExpiresOnUtc, refreshToken, refreshTokenExpiresOnUtc } }` — aucun
/// nom, sous aucune orthographe. La valeur stockée était donc TOUJOURS vide, et
/// l'application affichait « Ma boutique » à tout le monde, indéfiniment.
///
/// Le nom de la boutique est une donnée du vendeur : il se lit sur
/// `GET /api/merchants/me` (voir `core/identity/seller_identity.dart`), ce qui a
/// l'avantage de suivre un renommage sans attendre une reconnexion.
class AuthTokens {
  AuthTokens({required this.accessToken, required this.refreshToken});

  final String accessToken;
  final String refreshToken;

  /// Le login imbrique les jetons sous `tokens` ; le refresh les rend à plat.
  /// On accepte les deux formes plutôt que de dépendre de l'appelant.
  factory AuthTokens.fromJson(Map data) {
    final t = (data['tokens'] is Map) ? data['tokens'] as Map : data;
    return AuthTokens(
      accessToken: Json.str(t['accessToken'] ?? t['access_token']),
      refreshToken: Json.str(t['refreshToken'] ?? t['refresh_token']),
    );
  }

  bool get isEmpty => accessToken.isEmpty;
}

/// Informations société déclarées pour la boutique.
///
/// Miroir du contrat `SellerCompanyInfo` de merchant-service : tous les champs
/// sont optionnels, c'est du déclaratif de pré-remplissage. Ce n'est PAS une
/// preuve — la vérification reste le KYB (pièces + validation admin).
class CompanyInfo {
  const CompanyInfo({
    this.legalName,
    this.rccm,
    this.ifu,
    this.address,
    this.commune,
    this.activity,
    this.managerName,
    this.phone,
  });

  final String? legalName;
  final String? rccm;
  final String? ifu;
  final String? address;

  /// CODE de commune (« abomey-calavi »), pas un libellé libre : c'est la valeur
  /// que merchant-service stocke, et le libellé accentué est résolu par lui.
  final String? commune;
  final String? activity;
  final String? managerName;
  final String? phone;

  /// Vrai si au moins un champ est renseigné (sinon on envoie `null` au serveur).
  bool get isEmpty =>
      [legalName, rccm, ifu, address, commune, activity, managerName, phone]
          .every((v) => v == null || v.trim().isEmpty);

  Map<String, dynamic> toJson() => {
        'legalName': legalName,
        'rccm': rccm,
        'ifu': ifu,
        'address': address,
        'commune': commune,
        'activity': activity,
        'managerName': managerName,
        'phone': phone,
      };
}

/// Données transmises à l'écran de saisie du code (via GoRouter `extra`).
///
/// `userId` A DISPARU DE CE TYPE, ET CE N'EST PAS UN NETTOYAGE.
///
/// La vérification se fait désormais PAR ADRESSE (`POST /api/auth/email/verify`,
/// contrat `{ email, code }`). L'écran obtenait l'identifiant de deux sources :
/// l'inscription, et le renvoi de code. Cette seconde source était un oracle sur
/// une route anonyme — obtenir un identifiant prouve que le compte existe — et
/// elle a été supprimée côté serveur : `/api/auth/email/resend` répond 204 dans
/// tous les cas.
///
/// `shopName` et `company` restent ici parce que la boutique se crée APRÈS la
/// connexion (voir `SellerIdentityApi.registerShop`) : l'écran doit les garder
/// sous la main jusque-là.
class SellerVerifyArgs {
  const SellerVerifyArgs({
    required this.email,
    required this.shopName,
    this.company,
  });

  final String email;
  final String shopName;
  final CompanyInfo? company;
}

/// Authentification de la passerelle HBA (`/api/auth/*`).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// IL N'Y A PLUS D'INSCRIPTION VENDEUR EN UN GESTE.
///
/// L'ancien contrat tenait en deux appels au BFF du monolithe :
/// `/seller/auth/register` créait le compte, `/seller/auth/verify` validait le
/// code, CRÉAIT LA BOUTIQUE et attribuait le rôle vendeur. Ni l'un ni l'autre
/// n'existe : la passerelle n'expose rien sous `/seller`, et ces appels
/// partaient en 404 sans que rien ne le dise.
///
/// Le parcours réel traverse deux services, dans cet ordre :
///
///   1. `POST /api/auth/register`        — crée le COMPTE (identity-service) ;
///   2. `POST /api/auth/email/verify`    — valide l'adresse par code ;
///   3. `POST /api/auth/login`           — ouvre la session ;
///   4. `POST /api/merchants`            — crée la BOUTIQUE (merchant-service),
///      ce qui déclenche l'attribution du rôle `Seller` ;
///   5. rafraîchissement du jeton        — sans quoi le jeton en main date de
///      l'étape 3 et ne porte PAS encore ce rôle (voir `SellerIdentityApi`).
///
/// Les étapes 4 et 5 vivent dans `core/identity/seller_identity.dart` : elles ne
/// relèvent pas d'identity-service, et les mélanger ici masquerait le fait que
/// deux services distincts sont en jeu.
///
/// DEUX PRÉFIXES, ET CE N'EST PAS UNE INCOHÉRENCE. L'inscription, la
/// connexion, les jetons et le mot de passe oublié sont les gestes d'un
/// VISITEUR : ils vivent sous `/api/auth/*`. La déconnexion appartient au
/// COMPTE — elle exige un jeton valide — et vit sous
/// `/api/identity/account/me/logout`, avec le changement de mot de passe et la
/// double authentification.
/// ═════════════════════════════════════════════════════════════════════════════
class AuthApi {
  AuthApi(this._dio);
  final Dio _dio;

  static const _base = AppConfig.auth;
  static const _account = '${AppConfig.identity}/account/me';

  Future<AuthTokens> login(String email, String password, {String? mfaCode}) async {
    try {
      final resp = await _dio.post('$_base/login', data: {
        'email': email,
        'password': password,
        'mfaCode': mfaCode,
      });

      final data = Json.map(resp.data);
      final tokens = AuthTokens.fromJson(data);

      if (tokens.isEmpty) {
        // Pas de jeton et mfaRequired : le compte a la double authentification.
        if (Json.asBool(data['mfaRequired'])) {
          throw ApiException('Code de double authentification requis.', code: 'mfa_required');
        }
        throw ApiException('Connexion impossible : jeton manquant.');
      }
      return tokens;
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Échange un refresh token contre une session fraîche.
  ///
  /// Deux usages : la reconnexion sans mot de passe, et la reprise du jeton
  /// après un changement de rôle (création de boutique). `/auth/refresh` est
  /// anonyme et exclu de l'intercepteur 401 — pas de boucle possible.
  Future<AuthTokens> refresh(String refreshToken) async {
    try {
      final resp = await _dio.post('$_base/refresh', data: {'refreshToken': refreshToken});
      final tokens = AuthTokens.fromJson(Json.map(resp.data));
      if (tokens.isEmpty) {
        throw ApiException('Session expirée. Reconnectez-vous avec votre mot de passe.');
      }
      return tokens;
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Étape 1 — crée le COMPTE et renvoie son identifiant. N'ouvre pas de
  /// session, et ne crée AUCUNE boutique : à ce stade, le compte n'a que le
  /// rôle `Buyer`. Un e-mail contenant le code de vérification part.
  ///
  /// `phoneNumber` EST OBLIGATOIRE CÔTÉ SERVEUR (`RegisterRequest` le déclare
  /// non nullable) : l'envoyer vide fait échouer la validation, pas la requête.
  Future<String> register({
    required String email,
    required String password,
    required String firstName,
    required String lastName,
    required String phoneNumber,
  }) async {
    try {
      final resp = await _dio.post('$_base/register', data: {
        'firstName': firstName,
        'lastName': lastName,
        'email': email,
        'phoneNumber': phoneNumber,
        'password': password,
      });
      final data = Json.map(resp.data);
      return Json.str(data['id'] ?? data['userId']);
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Étape 2 — valide l'adresse avec le code à six chiffres reçu par e-mail.
  ///
  /// Marque l'e-mail comme vérifié, rien de plus : ni boutique, ni rôle vendeur.
  /// Contrairement à ses voisines anonymes, cette route DIT l'échec — elle exige
  /// un secret que l'attaquant n'a pas.
  Future<void> confirmEmail({required String email, required String code}) async {
    try {
      await _dio.post('$_base/email/verify', data: {'email': email, 'code': code});
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Redemande un code de vérification.
  ///
  /// NE RENVOIE PLUS D'`userId` — LA SIGNATURE A CHANGÉ EXPRÈS.
  ///
  /// Le BFF du monolithe le rendait, et l'écran de connexion s'en servait pour
  /// décider s'il y avait « un compte à vérifier ». C'était un oracle
  /// d'énumération sur une route anonyme. La passerelle répond 204 dans tous les
  /// cas — adresse inconnue, compte déjà vérifié, demande trop rapprochée — et
  /// le limiteur `otp` (5 essais / 5 min) est la vraie parade.
  ///
  /// L'appelant doit donc enchaîner sur l'écran de saisie du code SANS avoir
  /// appris quoi que ce soit sur l'existence du compte.
  Future<void> resendEmailCode(String email) async {
    try {
      await _dio.post('$_base/email/resend', data: {'email': email});
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Mot de passe oublié — étape 1. Toujours silencieuse (204), que le compte
  /// existe ou non : anti-énumération.
  Future<void> forgotPassword(String email) async {
    try {
      await _dio.post('$_base/password/forgot', data: {'email': email});
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Mot de passe oublié — étape 2.
  ///
  /// LE CHAMP S'APPELLE `token`, PAS `code` (`ResetPasswordRequest(Email,
  /// Token, NewPassword)`). Sous l'ancien nom, le corps était désérialisé avec
  /// un jeton NUL et la réinitialisation échouait en validation, sans que le
  /// message serveur n'indique le champ fautif.
  Future<void> resetPassword({
    required String email,
    required String token,
    required String newPassword,
  }) async {
    try {
      await _dio.post('$_base/password/reset', data: {
        'email': email,
        'token': token,
        'newPassword': newPassword,
      });
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Déconnexion : révoque le refresh token côté serveur.
  ///
  /// Sous `/account/me/*` et non `/auth/*` — c'est un geste de compte, il exige
  /// donc un jeton valide. Au mieux : si l'appel échoue, l'appelant vide quand
  /// même la session locale, sinon un réseau coupé empêcherait de se déconnecter.
  Future<void> logout(String refreshToken) async {
    try {
      await _dio.post('$_account/logout', data: {'refreshToken': refreshToken});
    } on DioException {
      // Volontairement muet : la révocation distante ne doit pas retenir le
      // vendeur dans l'application.
    }
  }
}

final authApiProvider = Provider<AuthApi>((ref) => AuthApi(ref.watch(dioProvider)));
