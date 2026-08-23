import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/config/app_config.dart';
import '../../../core/network/api_exception.dart';
import '../../../core/providers/core_providers.dart';

/// Jetons + nom renvoyés par l'authentification.
class AuthTokens {
  AuthTokens({required this.accessToken, required this.refreshToken, required this.name});

  final String accessToken;
  final String refreshToken;
  final String name;

  factory AuthTokens.fromJson(Map data) {
    // Le login renvoie { mfaRequired, tokens: { accessToken, refreshToken, … } } ;
    // le refresh renvoie les jetons à plat. On gère les deux formes.
    final t = (data['tokens'] is Map) ? data['tokens'] as Map : data;
    return AuthTokens(
      accessToken: (t['accessToken'] ?? t['access_token'] ?? '').toString(),
      refreshToken: (t['refreshToken'] ?? t['refresh_token'] ?? '').toString(),
      name: (data['fullName'] ?? data['name'] ?? data['displayName'] ?? t['fullName'] ?? '').toString(),
    );
  }
}

/// Appels d'authentification de la passerelle HBA (`/api/auth/*`).
///
/// DEUX PRÉFIXES, ET CE N'EST PAS UNE INCOHÉRENCE.
///
/// L'inscription, la connexion, les jetons et le mot de passe oublié sont sous
/// `/api/auth/*` : ce sont les gestes d'un visiteur, la passerelle les réécrit
/// vers identity-service.
///
/// La déconnexion, elle, appartient au COMPTE : elle vit sous
/// `/api/identity/account/me/logout`, avec le changement de mot de passe et la
/// double authentification. J'aurais pu ajouter un alias `/api/auth/logout`
/// côté passerelle pour n'avoir qu'un préfixe ici — ç'aurait été figer une
/// commodité de l'ancien client dans le nouveau contrat.
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
      _ensureOk(resp);
      final tokens = AuthTokens.fromJson(resp.data as Map);
      if (tokens.accessToken.isEmpty) {
        final mfa = resp.data is Map && (resp.data as Map)['mfaRequired'] == true;
        if (mfa) {
          // Le compte a la double authentification : l'écran de connexion doit
          // afficher le champ de code et réessayer avec mfaCode.
          throw ApiException('Code de double authentification requis.', code: 'mfa_required');
        }
        throw ApiException('Connexion impossible : jeton manquant.');
      }
      return tokens;
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Échange un refresh token contre une session fraîche. Utilisé par la
  /// connexion biométrique : on n'a plus besoin du mot de passe. `/auth/refresh`
  /// est anonyme et exclu de l'intercepteur 401, donc pas de boucle.
  Future<AuthTokens> refresh(String refreshToken) async {
    try {
      final resp = await _dio.post('$_base/refresh', data: {'refreshToken': refreshToken});
      _ensureOk(resp);
      final tokens = AuthTokens.fromJson(resp.data as Map);
      if (tokens.accessToken.isEmpty) {
        throw ApiException('Session expirée. Reconnectez-vous avec votre mot de passe.');
      }
      return tokens;
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Crée le compte et renvoie son identifiant.
  ///
  /// Ne connecte PLUS automatiquement : le compte naît en attente (e-mail à
  /// vérifier par code, puis validation par un administrateur). On enchaîne donc
  /// sur l'écran de saisie du code, pas sur la session.
  Future<String> register({
    required String firstName,
    required String lastName,
    required String email,
    String? phoneNumber,
    required String password,
  }) async {
    try {
      final resp = await _dio.post('$_base/register', data: {
        'firstName': firstName,
        'lastName': lastName,
        'email': email,
        'phoneNumber': phoneNumber,
        'password': password,
      });
      _ensureOk(resp);
      final data = resp.data;
      return data is Map ? (data['id'] ?? data['userId'] ?? '').toString() : '';
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Vérifie l'adresse e-mail à partir du code à 6 chiffres reçu par e-mail.
  ///
  /// PAR ADRESSE, PLUS PAR `userId`.
  ///
  /// L'ancien contrat exigeait l'identifiant du compte. L'écran l'obtenait de
  /// deux façons : l'inscription le rendait, et « renvoyer le code » le rendait
  /// aussi — cette seconde source était un oracle sur une route anonyme, et elle
  /// a disparu. L'adresse est la seule chose que l'utilisateur a toujours sous
  /// la main.
  ///
  /// Marque l'e-mail comme vérifié ; n'active PAS le compte.
  Future<void> confirmEmail({required String email, required String code}) async {
    try {
      final resp = await _dio.post('$_base/email/verify', data: {
        'email': email,
        'code': code,
      });
      _ensureOk(resp);
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Redemande un code de vérification.
  ///
  /// NE RENVOIE PLUS L'`userId` — LA SIGNATURE A CHANGÉ EXPRÈS.
  ///
  /// Le BFF du monolithe le renvoyait pour que l'écran de saisie l'utilise.
  /// C'était un oracle : obtenir un identifiant prouve que le compte existe, et
  /// la route est anonyme. On y énumérait donc les adresses inscrites.
  ///
  /// La passerelle répond 204 dans tous les cas — adresse inconnue, compte déjà
  /// vérifié, demande trop rapprochée. L'`userId` dont l'écran a besoin lui est
  /// rendu par l'INSCRIPTION ; c'est à l'appelant de le conserver.
  Future<void> resendEmailCode(String email) async {
    try {
      final resp = await _dio.post('$_base/email/resend', data: {'email': email});
      _ensureOk(resp);
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Déconnexion : révoque le refresh token côté serveur.
  ///
  /// Sous `/account/me/*` et non `/auth/*` — c'est un geste de compte, il exige
  /// donc un jeton valide. Reste au mieux : si l'appel échoue, la session locale
  /// est vidée quand même par l'appelant, sinon un réseau coupé empêcherait de
  /// se déconnecter.
  Future<void> logout(String refreshToken) async {
    try {
      await _dio.post('$_account/logout', data: {'refreshToken': refreshToken});
    } on DioException {
      // déconnexion best-effort
    }
  }

  /// Demande de réinitialisation. Toujours silencieuse.
  ///
  /// NE RENVOIE PLUS DE JETON — ET C'ÉTAIT UNE FAILLE MAJEURE.
  ///
  /// L'ancien BFF recopiait le jeton de réinitialisation EN CLAIR dans sa
  /// réponse, sur une route anonyme. N'importe qui saisissait l'adresse d'un
  /// administrateur, lisait le jeton, et changeait son mot de passe.
  ///
  /// Côté HBA, la commande ne rend plus rien du tout : la fuite est devenue
  /// impossible à réécrire, garantie par le type et non par la vigilance. Le
  /// jeton part par e-mail, et seulement là.
  Future<void> forgotPassword(String email) async {
    try {
      final resp = await _dio.post('$_base/password/forgot', data: {'email': email});
      _ensureOk(resp);
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Réinitialise le mot de passe à partir du jeton reçu.
  Future<void> resetPassword({required String email, required String token, required String newPassword}) async {
    try {
      final resp = await _dio.post('$_base/password/reset', data: {
        'email': email,
        'token': token,
        'newPassword': newPassword,
      });
      _ensureOk(resp);
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  void _ensureOk(Response resp) {
    final code = resp.statusCode ?? 0;
    if (code >= 400) {
      final data = resp.data;
      final msg = data is Map
          ? (data['detail'] ?? data['message'] ?? data['title'] ?? 'Échec de l\'authentification')
          : 'Échec de l\'authentification';
      throw ApiException(msg.toString(), statusCode: code);
    }
  }
}

final authApiProvider = Provider<AuthApi>((ref) => AuthApi(ref.watch(dioProvider)));

/// Données passées à l'écran de vérification d'e-mail (via GoRouter `extra`).
///
/// `userId` A DISPARU DE CE TYPE.
///
/// Il n'était là que parce que l'ancien contrat de vérification l'exigeait. La
/// passerelle vérifie par adresse ; le garder aurait obligé chaque appelant à
/// se procurer une donnée dont plus personne n'a besoin — et l'un des deux
/// chemins qui la fournissaient était une faille.
class EmailVerifyArgs {
  const EmailVerifyArgs({required this.email});

  final String email;
}
