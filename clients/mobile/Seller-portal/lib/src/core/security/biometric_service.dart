import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:local_auth/local_auth.dart';

import '../providers/core_providers.dart';

/// Identifiants récupérés après déverrouillage biométrique.
class BiometricCredentials {
  const BiometricCredentials(this.email, this.password);
  final String email;
  final String password;
}

/// Connexion biométrique (Face ID / Touch ID).
///
/// Après une connexion réussie, le vendeur peut mémoriser ses identifiants dans le
/// stockage sécurisé (Keychain/Keystore) ; ils ne sont ensuite relus qu'APRÈS une
/// authentification biométrique réussie. Rien n'est jamais écrit en clair ailleurs.
class BiometricService {
  BiometricService(this._storage);

  final FlutterSecureStorage _storage;
  final LocalAuthentication _auth = LocalAuthentication();

  static const _kEmail = 'bio_email';
  static const _kPassword = 'bio_password';

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

  /// Des identifiants sont-ils mémorisés pour la connexion biométrique ?
  Future<bool> get isEnabled async => (await _storage.read(key: _kEmail))?.isNotEmpty ?? false;

  Future<void> enable(String email, String password) async {
    await _storage.write(key: _kEmail, value: email);
    await _storage.write(key: _kPassword, value: password);
  }

  Future<void> disable() async {
    await _storage.delete(key: _kEmail);
    await _storage.delete(key: _kPassword);
  }

  /// Authentifie par biométrie puis renvoie les identifiants stockés (ou null si
  /// l'authentification a échoué / a été annulée / rien n'est mémorisé).
  Future<BiometricCredentials?> unlock(String reason) async {
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
    final password = await _storage.read(key: _kPassword);
    if (email == null || email.isEmpty || password == null || password.isEmpty) {
      return null;
    }
    return BiometricCredentials(email, password);
  }
}

final biometricServiceProvider =
    Provider<BiometricService>((ref) => BiometricService(ref.watch(secureStorageProvider)));
