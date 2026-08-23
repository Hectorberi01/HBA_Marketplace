import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http_mock_adapter/http_mock_adapter.dart';

import 'package:client_mp_mobile/src/features/auth/data/auth_api.dart';
import 'package:client_mp_mobile/src/core/network/api_exception.dart';

/// Flux critique — CONNEXION acheteur (`POST /mobile/auth/login`).
///
/// Fige le contrat attendu du BFF (HTTP simulé, aucun appel réseau) :
///   • jeton imbriqué sous `tokens` OU à plat -> AuthTokens exploitable ;
///   • MFA (jetons absents + mfaRequired) -> erreur identifiable ;
///   • 401 -> ApiException lisible, jamais une DioException brute.
void main() {
  late Dio dio;
  late DioAdapter adapter;

  setUp(() {
    dio = Dio(BaseOptions(baseUrl: 'https://test.local'));
    adapter = DioAdapter(dio: dio);
  });

  test('login OK — jetons imbriqués sous "tokens" + nom', () async {
    adapter.onPost(
      '/mobile/auth/login',
      (server) => server.reply(200, {
        'tokens': {'accessToken': 'AT-1', 'refreshToken': 'RT-1'},
        'fullName': 'Awa Koné',
      }),
      data: Matchers.any,
    );

    final tokens = await AuthApi(dio).login('a@b.fr', 'secret');

    expect(tokens.accessToken, 'AT-1');
    expect(tokens.refreshToken, 'RT-1');
    expect(tokens.name, 'Awa Koné');
  });

  test('login OK — jetons à plat', () async {
    adapter.onPost(
      '/mobile/auth/login',
      (server) => server.reply(200, {'accessToken': 'AT-flat', 'refreshToken': 'RT-flat'}),
      data: Matchers.any,
    );

    final tokens = await AuthApi(dio).login('a@b.fr', 'secret');
    expect(tokens.accessToken, 'AT-flat');
  });

  test('login MFA — jetons absents + mfaRequired lève un code identifiable', () async {
    adapter.onPost(
      '/mobile/auth/login',
      (server) => server.reply(200, {'mfaRequired': true}),
      data: Matchers.any,
    );

    await expectLater(
      AuthApi(dio).login('a@b.fr', 'secret'),
      throwsA(isA<ApiException>().having((e) => e.code, 'code', 'mfa_required')),
    );
  });

  test('login KO — 401 remonte en ApiException avec le statut', () async {
    adapter.onPost(
      '/mobile/auth/login',
      (server) => server.reply(401, {'detail': 'Identifiants invalides'}),
      data: Matchers.any,
    );

    await expectLater(
      AuthApi(dio).login('a@b.fr', 'mauvais'),
      throwsA(isA<ApiException>().having((e) => e.statusCode, 'statusCode', 401)),
    );
  });
}
