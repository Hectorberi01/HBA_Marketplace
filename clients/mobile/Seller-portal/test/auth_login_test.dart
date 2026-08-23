import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http_mock_adapter/http_mock_adapter.dart';

import 'package:hba_express_pro/src/features/auth/data/auth_api.dart';
import 'package:hba_express_pro/src/core/network/api_exception.dart';

/// Flux critique n°1 — CONNEXION vendeur (`POST /api/auth/login`).
///
/// C'est la porte d'entrée de l'app : si elle casse, plus personne ne rentre.
/// Ces tests figent le contrat exact attendu de la passerelle (HTTP simulé,
/// aucun appel réseau réel) :
///   • le jeton peut arriver imbriqué sous `tokens` OU à plat → les deux formes
///     doivent produire un `AuthTokens` exploitable ;
///   • un compte protégé par MFA (jetons absents + `mfaRequired`) doit lever une
///     erreur identifiable (`code == 'mfa_required'`), pas un message générique ;
///   • un échec serveur (401) doit remonter en `ApiException` lisible, avec le
///     message précis du backend, et jamais une `DioException` brute.
///
/// LE CHEMIN A CHANGÉ : `/seller/auth/login` VISAIT LE BFF DU MONOLITHE.
///
/// La passerelle n'expose rien sous `/seller` : ces tests passaient sur un
/// adaptateur simulé pendant que l'application, elle, prenait des 404. Le chemin
/// réel est `AppConfig.auth` + `/login`, soit `/api/auth/login`.
void main() {
  late Dio dio;
  late DioAdapter adapter;

  setUp(() {
    dio = Dio(BaseOptions(baseUrl: 'https://test.local'));
    adapter = DioAdapter(dio: dio);
  });

  test('login OK — jetons imbriqués sous "tokens"', () async {
    adapter.onPost(
      '/api/auth/login',
      (server) => server.reply(200, {
        'tokens': {'accessToken': 'AT-123', 'refreshToken': 'RT-456'},
      }),
      data: Matchers.any,
    );

    final tokens = await AuthApi(dio).login('vendeur@test.fr', 'secret');

    expect(tokens.accessToken, 'AT-123');
    expect(tokens.refreshToken, 'RT-456');
    expect(tokens.isEmpty, isFalse);

    // ASSERTION RETIRÉE : `expect(tokens.name, 'Ma Super Boutique')`.
    //
    // `AuthTokens.name` n'existe plus, et il n'avait aucun amont : la réponse de
    // `POST /api/auth/login` est `{ mfaRequired, tokens: { accessToken,
    // accessTokenExpiresOnUtc, refreshToken, refreshTokenExpiresOnUtc } }` —
    // aucun nom, sous aucune orthographe (`sellerName`, `shopName`, `fullName`
    // étaient tous cherchés en vain). Le test figeait donc un champ que le
    // serveur ne rend pas, à partir d'une réponse simulée qui l'inventait.
    //
    // Le nom de la boutique se lit sur `GET /api/merchants/me` — il est couvert
    // par `SellerIdentity`, pas par l'authentification.
  });

  test('login OK — jetons à plat (forme du refresh)', () async {
    adapter.onPost(
      '/api/auth/login',
      (server) => server.reply(200, {'accessToken': 'AT-flat', 'refreshToken': 'RT-flat'}),
      data: Matchers.any,
    );

    final tokens = await AuthApi(dio).login('vendeur@test.fr', 'secret');

    expect(tokens.accessToken, 'AT-flat');
    expect(tokens.refreshToken, 'RT-flat');
  });

  test('login MFA — jetons absents + mfaRequired lève un code identifiable', () async {
    adapter.onPost(
      '/api/auth/login',
      (server) => server.reply(200, {'mfaRequired': true}),
      data: Matchers.any,
    );

    await expectLater(
      AuthApi(dio).login('vendeur@test.fr', 'secret'),
      throwsA(isA<ApiException>().having((e) => e.code, 'code', 'mfa_required')),
    );
  });

  test('login KO — 401 remonte en ApiException lisible (message backend + statut)', () async {
    adapter.onPost(
      '/api/auth/login',
      (server) => server.reply(401, {'detail': 'Identifiants invalides', 'code': 'invalid_credentials'}),
      data: Matchers.any,
    );

    await expectLater(
      AuthApi(dio).login('vendeur@test.fr', 'mauvais'),
      throwsA(isA<ApiException>()
          .having((e) => e.statusCode, 'statusCode', 401)
          .having((e) => e.message, 'message', 'Identifiants invalides')),
    );
  });

  test('login KO — jetons vides sans MFA lève une erreur explicite', () async {
    adapter.onPost(
      '/api/auth/login',
      (server) => server.reply(200, {'tokens': {'accessToken': '', 'refreshToken': ''}}),
      data: Matchers.any,
    );

    await expectLater(
      AuthApi(dio).login('vendeur@test.fr', 'secret'),
      throwsA(isA<ApiException>()),
    );
  });

  test('refresh — échange le refresh token contre une session fraîche', () async {
    adapter.onPost(
      '/api/auth/refresh',
      (server) => server.reply(200, {'accessToken': 'AT-new', 'refreshToken': 'RT-new'}),
      data: Matchers.any,
    );

    final tokens = await AuthApi(dio).refresh('RT-old');

    expect(tokens.accessToken, 'AT-new');
    expect(tokens.refreshToken, 'RT-new');
  });
}
