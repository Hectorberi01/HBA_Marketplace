import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:local_auth/local_auth.dart';

import '../providers/core_providers.dart';

/// Session récupérée après déverrouillage biométrique : de quoi rouvrir une
/// session sans redemander le mot de passe.
class BiometricSession {
  const BiometricSession(this.email, this.refreshToken);
  final String email;
  final String refreshToken;
}

/// Connexion biométrique (Face ID / Touch ID).
///
/// SÉCURITÉ — on ne mémorise JAMAIS le mot de passe. On garde un *refresh token*
/// (jeton révocable, propre à l'app), maintenu à jour par [TokenStorage] à chaque
/// rotation. Après une biométrie réussie, ce jeton est échangé contre une session
/// fraîche via `/auth/refresh`. Un jeton qui fuite est révocable et inutilisable
/// ailleurs — contrairement au mot de passe, réutilisable sur d'autres services.
class BiometricService {
  BiometricService(this._storage);

  final FlutterSecureStorage _storage;
  final LocalAuthentication _auth = LocalAuthentication();

  static const _kEmail = 'bio_email';
  static const _kEnabled = 'bio_enabled';
  static const _kRefresh = 'bio_refresh';
  // Ancienne clé (mot de passe en clair) : purgée à l'activation/désactivation.
  static const _kLegacyPassword = 'bio_password';

  /// L'appareil supporte-t-il la biométrie ET est-elle configurée ?
  Future<bool> get isAvailable async {
    try {
      if (!await _auth.isDeviceSupported()) return false;
      if (!await _auth.canCheckBiometrics) return false;
      final types = await _auth.getAvailableBiometrics();
      return types.isNotEmpty;
    } catch (_) {
      return false;
    }
  }

  /// Libellé adapté au matériel : « Face ID », « Touch ID » ou « Biométrie ».
  Future<String> label() async {
    try {
      final types = await _auth.getAvailableBiometrics();
      if (types.contains(BiometricType.face)) return 'Face ID';
      if (types.contains(BiometricType.fingerprint) || types.contains(BiometricType.strong)) {
        return 'Touch ID';
      }
    } catch (_) {
      // ignoré : on retombe sur le libellé générique
    }
    return 'Biométrie';
  }

  /// La connexion biométrique est-elle armée (un refresh token est mémorisé) ?
  Future<bool> get isEnabled async => (await _storage.read(key: _kRefresh))?.isNotEmpty ?? false;

  /// Arme la connexion biométrique avec le refresh token courant. Le drapeau
  /// `bio_enabled` indique à [TokenStorage] de tenir ce jeton à jour ensuite.
  Future<void> enable(String email, String refreshToken) async {
    await _storage.write(key: _kEmail, value: email);
    await _storage.write(key: _kRefresh, value: refreshToken);
    await _storage.write(key: _kEnabled, value: 'true');
    await _storage.delete(key: _kLegacyPassword); // ne laisse aucun mot de passe hérité
  }

  Future<void> disable() async {
    await _storage.delete(key: _kEmail);
    await _storage.delete(key: _kRefresh);
    await _storage.delete(key: _kEnabled);
    await _storage.delete(key: _kLegacyPassword);
  }

  /// Authentifie par biométrie puis renvoie la session mémorisée (ou null si
  /// l'authentification a échoué / a été annulée / rien n'est mémorisé).
  Future<BiometricSession?> unlock(String reason) async {
    bool ok;
    try {
      ok = await _auth.authenticate(
        localizedReason: reason,
        options: const AuthenticationOptions(biometricOnly: true, stickyAuth: true),
      );
    } catch (_) {
      ok = false;
    }
    if (!ok) return null;

    final email = await _storage.read(key: _kEmail);
    final refresh = await _storage.read(key: _kRefresh);
    if (email == null || email.isEmpty || refresh == null || refresh.isEmpty) {
      return null;
    }
    return BiometricSession(email, refresh);
  }
}

final biometricServiceProvider =
    Provider<BiometricService>((ref) => BiometricService(ref.watch(secureStorageProvider)));
