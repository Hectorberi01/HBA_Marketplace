import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Stockage sécurisé des jetons (Keychain iOS / Keystore Android).
///
/// Les jetons ne transitent JAMAIS par SharedPreferences en clair : un accès au
/// jeton vendeur donnerait accès au catalogue, aux commandes et au portefeuille.
class TokenStorage {
  TokenStorage(this._storage);

  final FlutterSecureStorage _storage;

  static const _kAccess = 'access_token';
  static const _kRefresh = 'refresh_token';
  static const _kName = 'seller_name';

  Future<void> save({
    required String accessToken,
    required String refreshToken,
    String? name,
  }) async {
    await _storage.write(key: _kAccess, value: accessToken);
    await _storage.write(key: _kRefresh, value: refreshToken);
    if (name != null && name.isNotEmpty) {
      await _storage.write(key: _kName, value: name);
    }
  }

  Future<String?> get accessToken => _storage.read(key: _kAccess);
  Future<String?> get refreshToken => _storage.read(key: _kRefresh);

  /// N'EST PLUS ALIMENTÉ PAR LA CONNEXION — ET NE DOIT PAS L'ÊTRE.
  ///
  /// `POST /api/auth/login` ne renvoie aucun nom : ce champ restait
  /// systématiquement vide, et l'application affichait « Ma boutique » à tous
  /// les vendeurs. Le nom se lit désormais sur `GET /api/merchants/me`
  /// (`sellerNameProvider`). Seul le mode simulé écrit encore ici, faute
  /// d'amont — d'où la conservation de la clé, et de sa purge.
  Future<String?> get sellerName => _storage.read(key: _kName);

  Future<bool> get hasSession async => (await accessToken)?.isNotEmpty ?? false;

  Future<void> clear() async {
    await _storage.delete(key: _kAccess);
    await _storage.delete(key: _kRefresh);
    await _storage.delete(key: _kName);
  }
}
