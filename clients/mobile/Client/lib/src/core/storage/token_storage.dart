import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Stockage sécurisé des jetons d'authentification (Keychain / Keystore).
class TokenStorage {
  TokenStorage(this._storage);

  final FlutterSecureStorage _storage;

  static const _kAccess = 'access_token';
  static const _kRefresh = 'refresh_token';
  static const _kName = 'user_name';

  // Miroir biométrique (voir BiometricService). Écrit ICI pour que le refresh
  // token déverrouillable par Face ID suive AUTOMATIQUEMENT les rotations, où
  // qu'elles surviennent (connexion, intercepteur 401). Sans ce miroir, la
  // première rotation périmerait le token mémorisé et casserait la connexion
  // biométrique. Ces clés ne sont PAS effacées par clear() : la biométrie doit
  // survivre à la déconnexion.
  static const _kBioEnabled = 'bio_enabled';
  static const _kBioRefresh = 'bio_refresh';

  Future<void> save({
    required String accessToken,
    required String refreshToken,
    String? name,
  }) async {
    await _storage.write(key: _kAccess, value: accessToken);
    await _storage.write(key: _kRefresh, value: refreshToken);
    if (name != null) {
      await _storage.write(key: _kName, value: name);
    }
    // Tient le miroir biométrique à jour tant qu'il est activé.
    if ((await _storage.read(key: _kBioEnabled)) == 'true') {
      await _storage.write(key: _kBioRefresh, value: refreshToken);
    }
  }

  Future<String?> get accessToken => _storage.read(key: _kAccess);
  Future<String?> get refreshToken => _storage.read(key: _kRefresh);
  Future<String?> get userName => _storage.read(key: _kName);

  Future<bool> get hasSession async => (await accessToken)?.isNotEmpty ?? false;

  Future<void> clear() async {
    await _storage.delete(key: _kAccess);
    await _storage.delete(key: _kRefresh);
    await _storage.delete(key: _kName);
  }
}
