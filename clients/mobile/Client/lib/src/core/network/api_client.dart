import 'package:dio/dio.dart';

import '../config/app_config.dart';
import '../storage/token_storage.dart';

/// Client HTTP (Dio) configuré pour le BFF Mobile.
///
/// - Ajoute automatiquement le `Authorization: Bearer` sur chaque requête.
/// - Sur une 401, tente UN rafraîchissement de jeton puis rejoue la requête.
/// - En cas d'échec du refresh, vide la session et notifie [onSessionExpired].
class ApiClient {
  ApiClient(this._storage, {this.onSessionExpired}) {
    dio = Dio(
      BaseOptions(
        baseUrl: AppConfig.baseUrl,
        connectTimeout: AppConfig.connectTimeout,
        receiveTimeout: AppConfig.receiveTimeout,
        contentType: Headers.jsonContentType,
        // Évite la page d'avertissement de ngrok sur les requêtes API (sans
        // effet quand on n'utilise pas ngrok).
        headers: const {'ngrok-skip-browser-warning': 'true'},
        // Seuls les 2xx/3xx sont "valides" : tout 4xx/5xx lève une DioException,
        // que les repos convertissent en ApiException lisible (et qui déclenche
        // l'intercepteur de refresh sur 401). Sans ça, un rejet serveur (ex. 409
        // « stock insuffisant ») passait pour un succès silencieux.
        validateStatus: (s) => s != null && s < 400,
      ),
    );

    dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) async {
          final token = await _storage.accessToken;
          if (token != null && token.isNotEmpty) {
            options.headers['Authorization'] = 'Bearer $token';
          }
          handler.next(options);
        },
        onError: (error, handler) async {
          final response = error.response;
          // `/auth/` COUVRE LES DEUX CHEMINS, ET C'EST VOULU.
          //
          // La passerelle expose l'authentification sous `/api/auth/*` ET sous
          // `/api/identity/auth/*` — le second est le chemin interne du service,
          // rendu joignable pour l'administration. Les deux contiennent `/auth/`.
          //
          // Ce qui compte ici est qu'une 401 SUR une route d'authentification ne
          // déclenche pas un rafraîchissement : le jeton n'est pas expiré, les
          // identifiants sont mauvais. Sans cette garde, un mot de passe erroné
          // consommait un refresh token à chaque tentative.
          final isAuthCall = error.requestOptions.path.contains('/auth/');

          // Une requête REJOUÉE ne l'est jamais deux fois. `_retry` repasse par ce
          // même intercepteur : sans ce marqueur, un point d'accès qui répond 401
          // de façon persistante enchaînait rafraîchissement → rejeu → 401 →
          // rafraîchissement… en boucle, chaque tour consommant un appel réseau et
          // faisant tourner le refresh token.
          final alreadyRetried = error.requestOptions.extra[_retriedFlag] == true;

          if (response?.statusCode == 401 && !isAuthCall && !alreadyRetried) {
            // Toutes les requêtes qui tombent en 401 en même temps attendent le
            // MÊME rafraîchissement (un seul appel /auth/refresh), puis rejouent.
            final refreshed = await _ensureRefreshed();
            if (refreshed) {
              try {
                return handler.resolve(await _retry(error.requestOptions));
              } on DioException catch (retryError) {
                // ─────────────────────────────────────────────────────────────
                // LE RAFRAÎCHISSEMENT A RÉUSSI, LE REJEU A QUAND MÊME ÉCHOUÉ.
                //
                // Ce cas retombait dans le flux d'erreur SANS rien signaler : ni
                // session vidée, ni `onSessionExpired`. Le routeur ne redirigeait
                // donc jamais vers la connexion, et l'écran affichait « Session
                // expirée » avec un bouton « Réessayer » qui rejouait exactement
                // la même séquence vouée à l'échec. L'application restait bloquée
                // là, sans aucune issue offerte à l'utilisateur.
                //
                // On ne ferme la session QUE sur un 401. Un rejeu qui échoue
                // pour cause de réseau coupé — le cas courant sur un mobile — ne
                // dit rien sur la validité de la session ; déconnecter là-dessus
                // ferait ressaisir le mot de passe à chaque passage sous un pont.
                // ─────────────────────────────────────────────────────────────
                if (retryError.response?.statusCode == 401) {
                  await _expireSession();
                }
              }
            } else {
              await _expireSession();
            }
          } else if (response?.statusCode == 401 && !isAuthCall && alreadyRetried) {
            // Rejeu déjà tenté et toujours refusé : inutile d'insister.
            await _expireSession();
          }

          handler.next(error);
        },
      ),
    );
  }

  late final Dio dio;
  final TokenStorage _storage;
  final void Function()? onSessionExpired;

  /// Marque une requête déjà rejouée après rafraîchissement.
  static const _retriedFlag = '__auth_retried';

  /// Ferme la session et prévient l'application, UNE SEULE FOIS.
  ///
  /// Plusieurs requêtes peuvent tomber en 401 ensemble (le panier, le badge, les
  /// notifications). Sans ce garde, chacune émettrait son signal, le routeur se
  /// réévaluerait autant de fois, et la connexion pourrait être empilée plusieurs
  /// fois sur la pile de navigation.
  bool _expiring = false;

  Future<void> _expireSession() async {
    if (_expiring) return;
    _expiring = true;
    try {
      await _storage.clear();
      onSessionExpired?.call();
    } finally {
      // Rouvert pour la session SUIVANTE : sans cela, une expiration après
      // reconnexion ne serait plus jamais signalée.
      _expiring = false;
    }
  }

  // Rafraîchissement partagé : garantit un seul appel /auth/refresh à la fois,
  // même si plusieurs requêtes reçoivent un 401 simultanément (évite d'invalider
  // le refresh token par des rotations concurrentes → déconnexion intempestive).
  Future<bool>? _refreshFuture;

  Future<bool> _ensureRefreshed() =>
      _refreshFuture ??= _tryRefresh().whenComplete(() => _refreshFuture = null);

  Future<bool> _tryRefresh() async {
    try {
      final refresh = await _storage.refreshToken;
      if (refresh == null || refresh.isEmpty) return false;

      // Client brut (sans intercepteur) mais AVEC les en-têtes nécessaires :
      // notamment ngrok-skip-browser-warning, sinon ngrok renvoie sa page HTML
      // d'avertissement au lieu du JSON → le refresh échoue et la session saute.
      final raw = Dio(BaseOptions(
        baseUrl: AppConfig.baseUrl,
        connectTimeout: AppConfig.connectTimeout,
        receiveTimeout: AppConfig.receiveTimeout,
        contentType: Headers.jsonContentType,
        headers: const {
          'ngrok-skip-browser-warning': 'true',
          'Accept': 'application/json',
        },
      ));
      final resp = await raw.post(
        '${AppConfig.auth}/refresh',
        data: {'refreshToken': refresh},
      );
      if (resp.statusCode == 200 && resp.data is Map) {
        final data = resp.data as Map;
        // Le refresh renvoie les jetons à plat ; on gère aussi la forme imbriquée.
        final t = (data['tokens'] is Map) ? data['tokens'] as Map : data;
        final access = (t['accessToken'] ?? t['access_token'])?.toString();
        final newRefresh =
            (t['refreshToken'] ?? t['refresh_token'])?.toString() ?? refresh;
        if (access != null && access.isNotEmpty) {
          await _storage.save(accessToken: access, refreshToken: newRefresh);
          return true;
        }
      }
      return false;
    } catch (_) {
      return false;
    }
  }

  Future<Response<dynamic>> _retry(RequestOptions options) async {
    final token = await _storage.accessToken;
    return dio.request(
      options.path,
      data: options.data,
      queryParameters: options.queryParameters,
      options: Options(
        method: options.method,
        headers: {...options.headers, 'Authorization': 'Bearer $token'},
        // Le marqueur voyage AVEC la requête rejouée. `dio.request` repasse par
        // l'intercepteur ; c'est lui qui empêche la boucle.
        extra: {...options.extra, _retriedFlag: true},
      ),
    );
  }
}
