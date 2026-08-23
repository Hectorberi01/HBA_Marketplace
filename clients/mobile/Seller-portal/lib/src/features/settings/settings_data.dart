import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Préférence de thème (clair / sombre / système), persistée sur l'appareil.
///
/// Stockée dans le coffre sécurisé (déjà utilisé pour la session) : pas de
/// dépendance supplémentaire. La valeur par défaut est « système » — l'app suit
/// alors le réglage du téléphone.
const _kThemeModeKey = 'pref_theme_mode';

class ThemeModeNotifier extends Notifier<ThemeMode> {
  final FlutterSecureStorage _storage = const FlutterSecureStorage();

  @override
  ThemeMode build() {
    // Chargement asynchrone : on part de « système » puis on ajuste dès lecture.
    _load();
    return ThemeMode.system;
  }

  Future<void> _load() async {
    final raw = await _storage.read(key: _kThemeModeKey);
    final mode = switch (raw) {
      'light' => ThemeMode.light,
      'dark' => ThemeMode.dark,
      _ => ThemeMode.system,
    };
    if (mode != state) state = mode;
  }

  Future<void> set(ThemeMode mode) async {
    state = mode;
    await _storage.write(key: _kThemeModeKey, value: mode.name);
  }
}

final themeModeProvider = NotifierProvider<ThemeModeNotifier, ThemeMode>(ThemeModeNotifier.new);

/// Langue choisie (null = suit le téléphone). Persistée sur l'appareil, comme le
/// thème. Seules les langues réellement traduites sont proposées (fr, en).
const _kLocaleKey = 'pref_locale';

class LocaleNotifier extends Notifier<Locale?> {
  final FlutterSecureStorage _storage = const FlutterSecureStorage();

  @override
  Locale? build() {
    _load();
    return null; // Démarrage : on suit le système, puis on ajuste dès lecture.
  }

  Future<void> _load() async {
    final raw = await _storage.read(key: _kLocaleKey);
    final loc = switch (raw) {
      'fr' => const Locale('fr'),
      'en' => const Locale('en'),
      _ => null,
    };
    if (loc != state) state = loc;
  }

  /// [locale] null → repasse en « automatique » (suit le téléphone).
  Future<void> set(Locale? locale) async {
    state = locale;
    if (locale == null) {
      await _storage.delete(key: _kLocaleKey);
    } else {
      await _storage.write(key: _kLocaleKey, value: locale.languageCode);
    }
  }
}

final localeProvider = NotifierProvider<LocaleNotifier, Locale?>(LocaleNotifier.new);
