import 'dart:math';

import 'package:dio/dio.dart';

import '../config/app_config.dart';
import '../storage/token_storage.dart';

/// Client HTTP (Dio) configuré pour la passerelle HBA.
///
/// - Ajoute `Authorization: Bearer` sur chaque requête.
/// - Pose un identifiant de corrélation, repris par la passerelle et les
///   services dans leurs journaux.
/// - Sur une 401 **ou une 403**, tente UN rafraîchissement de jeton puis rejoue
///   la requête. Le 403 compte parce qu'un jeton dont le compte n'existe plus
///   reste valablement signé : il n'est jamais rejeté en 401, seulement refusé
///   en 403 sur chaque écran. Voir le raisonnement complet dans `onError`.
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
        // effet quand on n'utilise pas ngrok). Repris de l'app cliente : sans
        // cet en-tête, ngrok répond une page HTML là où le code attend du JSON.
        headers: const {'ngrok-skip-browser-warning': 'true'},
        // Seuls les 2xx/3xx sont valides : un rejet serveur (409 « stock
        // insuffisant », 422…) doit lever, pas passer pour un succès silencieux.
        validateStatus: (s) => s != null && s < 400,
      ),
    );

    // IL N'Y A PLUS DE COUPE-CIRCUIT DE SIMULATION EN TÊTE DE CHAÎNE.
    //
    // `MockInterceptor` fabriquait des réponses avant même que le jeton soit
    // posé. Il a été supprimé avec `core/mock/` : toute requête part maintenant
    // réellement sur la passerelle. Un écran sans amont ne doit PAS être servi
    // par un intercepteur, mais lever depuis sa couche de données — voir
    // `NotMigrated.call`.
    dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) async {
          final token = await _storage.accessToken;
          if (token != null && token.isNotEmpty) {
            options.headers['Authorization'] = 'Bearer $token';
          }

          // ─────────────────────────────────────────────────────────────────
          // CORRÉLATION : LE SEUL FIL ENTRE UN VENDEUR QUI APPELLE ET UN
          // JOURNAL SERVEUR.
          //
          // La passerelle ACCEPTE cet en-tête et le propage aux services
          // (`CorrelationIdMiddleware`, liste blanche de sortie), puis le
          // renvoie dans la réponse. Sans lui, elle en fabrique un : la trace
          // existe côté serveur, mais l'application ne la connaît pas, et
          // « ça n'a pas marché ce matin » reste introuvable dans les
          // journaux d'une plateforme à treize services.
          //
          // UN PAR REQUÊTE, ET NON UN PAR SESSION : un identifiant réutilisé
          // regrouperait sous une même trace tout ce qu'un vendeur a fait de la
          // journée, ce qui revient à ne rien corréler du tout.
          //
          // Ne PAS écraser une valeur déjà posée par l'appelant : c'est ce qui
          // permet de rejouer une requête sans perdre son fil (voir `_retry`).
          // ─────────────────────────────────────────────────────────────────
          options.headers.putIfAbsent(_correlationHeader, _newCorrelationId);

          handler.next(options);
        },
        onError: (error, handler) async {
          final response = error.response;
          // `/auth/` COUVRE LES DEUX CHEMINS, ET C'EST VOULU.
          //
          // La passerelle expose l'authentification sous `/api/auth/*` ET sous
          // `/api/identity/auth/*` — le second est le chemin interne du
          // service, laissé joignable pour l'administration et gardé par les
          // mêmes limiteurs. Les deux contiennent `/auth/`.
          //
          // Ce qui compte est qu'une 401 SUR une route d'authentification ne
          // déclenche pas de rafraîchissement : le jeton n'est pas expiré, les
          // identifiants sont mauvais. Sans cette garde, un mot de passe erroné
          // consommait un refresh token à chaque tentative.
          final isAuthCall = error.requestOptions.path.contains('/auth/');

          // Une requête REJOUÉE ne l'est jamais deux fois. `_retry` repasse par ce
          // même intercepteur : sans ce marqueur, un point d'accès qui répond 401
          // de façon persistante enchaînait rafraîchissement → rejeu → 401 →
          // rafraîchissement… en boucle, en faisant tourner le refresh token.
          final alreadyRetried = error.requestOptions.extra[_retriedFlag] == true;

          // ═══════════════════════════════════════════════════════════════════
          // LE 403 EST TRAITÉ COMME LE 401 — PARCE QU'UN JETON MORT NE
          //    PRODUIT PAS 401, IL PRODUIT 403.
          //
          // Un JWT n'est pas vérifié contre la base : la passerelle contrôle sa
          // SIGNATURE et lit ses revendications, rien de plus. Deux situations
          // très courantes en découlent, et aucune ne donne 401 :
          //
          //   • LA BASE A ÉTÉ RECRÉÉE (`down -v`). Le jeton est toujours signé
          //     par la même clé, donc toujours accepté ; son `sub` désigne un
          //     compte qui n'existe plus, et ses rôles sont ceux d'avant. Chaque
          //     écran répond 403, l'application se croit connectée, et il n'y a
          //     AUCUN moyen d'en sortir : le bouton « Se déconnecter » vit dans
          //     l'onglet Compte, derrière un sélecteur d'activité qui 403 lui
          //     aussi. C'est exactement l'impasse observée.
          //
          //   • LE RÔLE VIENT D'ÊTRE ATTRIBUÉ. `Seller` et `FoodPartner` sont
          //     greffés par identity-service en réaction à un événement : le
          //     jeton obtenu à la connexion est ANTÉRIEUR et ne les porte pas.
          //     Trois commentaires de cette application décrivaient déjà ce
          //     403 « transitoire » en renvoyant la charge à l'écran d'accueil.
          //     Aucun écran ne s'en occupait.
          //
          // LE RAFRAÎCHISSEMENT TRANCHE LES DEUX, parce que LUI passe par la
          // base : `POST /api/auth/refresh` cherche le jeton de rafraîchissement
          // en table.
          //
          //   • il échoue  → la session est réellement morte  → on la ferme ;
          //   • il réussit → le nouveau jeton porte les rôles à jour → on rejoue.
          //
          // ET SI LE REJEU REND ENCORE 403, ON NE FERME PAS LA SESSION.
          //
          // C'est alors un refus d'autorisation LÉGITIME — un vendeur qui touche
          // la boutique d'un autre, un compte marchand sur une route
          // `RestaurantOnly`. Déconnecter dans ce cas serait pire que le mal :
          // on éjecterait de l'application pour un écran interdit.
          //
          // Coût : un aller-retour supplémentaire sur un vrai 403, borné par
          // `_retriedFlag`. C'est peu payé pour une impasse en moins.
          // ═══════════════════════════════════════════════════════════════════
          final status = response?.statusCode;

          if (status == 403 && !isAuthCall && !alreadyRetried) {
            if (await _ensureRefreshed()) {
              try {
                return handler.resolve(await _retry(error.requestOptions));
              } on DioException catch (retryError) {
                // Seul le 401 prouve la mort de la session. Un 403 au rejeu est
                // un refus légitime, un échec réseau ne prouve rien.
                if (retryError.response?.statusCode == 401) {
                  await _expireSession();
                }
              }
            } else {
              // Pas de jeton de rafraîchissement, ou refusé par le serveur :
              // il n'y a plus de session à récupérer.
              await _expireSession();
            }
          } else if (status == 401 && !isAuthCall && !alreadyRetried) {
            // Toutes les requêtes qui tombent en 401 en même temps attendent le
            // MÊME rafraîchissement, puis rejouent. Sans ce partage, des
            // rotations concurrentes du refresh token invalideraient la session.
            final refreshed = await _ensureRefreshed();
            if (refreshed) {
              try {
                return handler.resolve(await _retry(error.requestOptions));
              } on DioException catch (retryError) {
                // ─────────────────────────────────────────────────────────────
                // LE RAFRAÎCHISSEMENT A RÉUSSI, LE REJEU A QUAND MÊME ÉCHOUÉ.
                //
                // Ce cas retombait dans le flux d'erreur SANS rien signaler : ni
                // session vidée, ni `onSessionExpired`. L'écran affichait donc
                // « Session expirée » avec un bouton « Réessayer » qui rejouait
                // la même séquence vouée à l'échec — sans jamais proposer de se
                // reconnecter. Pour un vendeur en train de traiter une commande,
                // c'est une impasse.
                //
                // On ne ferme la session QUE sur un 401 : un rejeu qui échoue
                // faute de réseau ne dit rien sur la validité de la session.
                // ─────────────────────────────────────────────────────────────
                if (retryError.response?.statusCode == 401) {
                  await _expireSession();
                }
              }
            } else {
              await _expireSession();
            }
          } else if (status == 401 && !isAuthCall && alreadyRetried) {
            // Rejeu déjà tenté et toujours refusé : inutile d'insister.
            //
            // Volontairement 401 SEUL. Un 403 déjà rejoué est un refus
            // d'autorisation légitime : il doit remonter à l'écran, pas
            // déconnecter le vendeur.
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

  /// Nom exact attendu par la passerelle (`CorrelationIdMiddleware.HeaderName`).
  /// Une autre orthographe serait ignorée en silence, et la passerelle
  /// fabriquerait son propre identifiant : la corrélation deviendrait décorative.
  static const _correlationHeader = 'X-Correlation-ID';

  static final _random = Random();

  /// Identifiant court, sans dépendance : la passerelle borne la valeur à 128
  /// caractères et refuse tout ce qui sort d'un jeu restreint — inutile d'aller
  /// chercher un UUID pour cela.
  static String _newCorrelationId() {
    final now = DateTime.now().microsecondsSinceEpoch.toRadixString(16);
    final salt = _random.nextInt(1 << 32).toRadixString(16).padLeft(8, '0');
    return 'seller-$now-$salt';
  }

  /// Ferme la session et prévient l'application, une seule fois à la fois.
  /// Plusieurs requêtes tombent en 401 ensemble (tableau de bord, commandes,
  /// notifications) : sans ce garde, chacune émettrait son signal.
  bool _expiring = false;

  Future<void> _expireSession() async {
    if (_expiring) return;
    _expiring = true;
    try {
      await _storage.clear();
      onSessionExpired?.call();
    } finally {
      _expiring = false;
    }
  }

  Future<bool>? _refreshFuture;

  Future<bool> _ensureRefreshed() =>
      _refreshFuture ??= _tryRefresh().whenComplete(() => _refreshFuture = null);

  Future<bool> _tryRefresh() async {
    try {
      final refresh = await _storage.refreshToken;
      if (refresh == null || refresh.isEmpty) return false;

      // Client brut, SANS intercepteur : sinon un 401 sur le refresh lui-même
      // relancerait un refresh, en boucle. Mais AVEC les en-têtes nécessaires —
      // notamment `ngrok-skip-browser-warning`, sinon ngrok renvoie sa page HTML
      // à la place du JSON, le refresh échoue et la session saute.
      final raw = Dio(BaseOptions(
        baseUrl: AppConfig.baseUrl,
        connectTimeout: AppConfig.connectTimeout,
        receiveTimeout: AppConfig.receiveTimeout,
        contentType: Headers.jsonContentType,
        headers: {
          'ngrok-skip-browser-warning': 'true',
          'Accept': 'application/json',
          _correlationHeader: _newCorrelationId(),
        },
      ));
      // `/api/auth/refresh`, ET NON `/seller/auth/refresh`.
      //
      // L'ancien chemin visait le BFF du monolithe : la passerelle n'expose rien
      // sous `/seller`. Chaque rafraîchissement partait donc en 404, `_tryRefresh`
      // rendait `false`, et la session était fermée — le vendeur était renvoyé à
      // l'écran de connexion à la première expiration de jeton, sans explication.
      final resp = await raw.post(
        '${AppConfig.auth}/refresh',
        data: {'refreshToken': refresh},
      );
      if (resp.statusCode == 200 && resp.data is Map) {
        final data = resp.data as Map;
        // Le refresh renvoie les jetons à plat ; on gère aussi la forme imbriquée.
        final t = (data['tokens'] is Map) ? data['tokens'] as Map : data;
        final access = (t['accessToken'] ?? t['access_token'])?.toString();
        final newRefresh = (t['refreshToken'] ?? t['refresh_token'])?.toString() ?? refresh;
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
        // Le marqueur voyage AVEC la requête rejouée : c'est lui qui empêche la
        // boucle rafraîchissement → rejeu → 401 → rafraîchissement.
        extra: {...options.extra, _retriedFlag: true},
      ),
    );
  }
}
