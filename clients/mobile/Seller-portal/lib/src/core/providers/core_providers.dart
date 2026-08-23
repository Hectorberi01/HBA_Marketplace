import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../network/api_client.dart';
import '../storage/token_storage.dart';

/// Stockage sécurisé (singleton).
final secureStorageProvider = Provider<FlutterSecureStorage>((ref) {
  return const FlutterSecureStorage(
    aOptions: AndroidOptions(encryptedSharedPreferences: true),
  );
});

/// Accès aux jetons.
final tokenStorageProvider = Provider<TokenStorage>((ref) {
  return TokenStorage(ref.watch(secureStorageProvider));
});

/// Signal de session expirée (incrémenté quand le refresh échoue) — écouté par
/// le contrôleur d'auth, qui bascule l'app vers l'écran de connexion.
final sessionExpiredProvider = StateProvider<int>((ref) => 0);

/// Client HTTP partagé.
final apiClientProvider = Provider<ApiClient>((ref) {
  final storage = ref.watch(tokenStorageProvider);
  return ApiClient(
    storage,
    onSessionExpired: () => ref.read(sessionExpiredProvider.notifier).state++,
  );
});

/// Dio prêt à l'emploi.
final dioProvider = Provider<Dio>((ref) => ref.watch(apiClientProvider).dio);
