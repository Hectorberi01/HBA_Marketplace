import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../providers/core_providers.dart';

/// Réglage du thème (Système / Clair / Sombre), persisté localement pour être
/// conservé entre deux lancements.
class ThemeModeController extends Notifier<ThemeMode> {
  static const _key = 'theme_mode';

  /// ───────────────────────────────────────────────────────────────────────────
  /// LE DÉFAUT EST LE MODE CLAIR, PAS CELUI DU SYSTÈME.
  ///
  /// Suivre le téléphone paraît poli, mais la conséquence est qu'une grande partie
  /// des utilisateurs découvre la boutique en sombre — alors que les photos de
  /// produits, les visuels de la fiche store et tout ce qui a été relu l'ont été
  /// en clair. Le mode sombre reste disponible d'un geste dans « Mon Compte » ;
  /// il est choisi, plus subi.
  ///
  /// Un utilisateur ayant déjà choisi « Système » garde son réglage : seul le cas
  /// « aucun choix enregistré » bascule sur clair.
  /// ───────────────────────────────────────────────────────────────────────────
  static const _fallback = ThemeMode.light;

  @override
  ThemeMode build() {
    _load();
    return _fallback;
  }

  Future<void> _load() async {
    final v = await ref.read(secureStorageProvider).read(key: _key);
    state = switch (v) {
      'light' => ThemeMode.light,
      'dark' => ThemeMode.dark,
      // Choix EXPLICITE de « Système » : on le respecte.
      'system' => ThemeMode.system,
      // Rien d'enregistré (première ouverture) : mode clair.
      _ => _fallback,
    };
  }

  Future<void> set(ThemeMode mode) async {
    state = mode;
    await ref.read(secureStorageProvider).write(key: _key, value: mode.name);
  }
}

final themeModeProvider = NotifierProvider<ThemeModeController, ThemeMode>(ThemeModeController.new);
