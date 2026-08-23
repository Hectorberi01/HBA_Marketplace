import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'package:hba_express_pro/l10n/app_localizations.dart';
import '../../../core/theme/app_theme.dart';
import '../settings_data.dart';

/// Réglages : thème (système / clair / sombre) ET langue (système / fr / en).
class SettingsScreen extends ConsumerWidget {
  const SettingsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l = AppLocalizations.of(context);
    final mode = ref.watch(themeModeProvider);
    final locale = ref.watch(localeProvider);
    final colors = AppColors.of(context);

    final themeOptions = <(ThemeMode, String, String, IconData)>[
      (ThemeMode.system, l.settingsThemeAuto, l.settingsThemeAutoDesc, Icons.brightness_auto_outlined),
      (ThemeMode.light, l.settingsThemeLight, l.settingsThemeLightDesc, Icons.light_mode_outlined),
      (ThemeMode.dark, l.settingsThemeDark, l.settingsThemeDarkDesc, Icons.dark_mode_outlined),
    ];

    // Langue : null = « automatique » (suit le téléphone). Les langues réelles
    // s'affichent dans LEUR propre nom (convention des sélecteurs de langue).
    final langOptions = <(Locale?, String, String?, IconData)>[
      (null, l.settingsLanguageSystem, l.settingsLanguageSystemDesc, Icons.language_outlined),
      (const Locale('fr'), 'Français', null, Icons.translate_outlined),
      (const Locale('en'), 'English', null, Icons.translate_outlined),
    ];

    return Scaffold(
      appBar: AppBar(title: Text(l.settingsAppearanceTitle)),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 32),
        children: [
          _label(l.settingsTheme, colors),
          // ─────────────────────────────────────────────────────────────────────
          // `RadioGroup` REMPLACE `groupValue` / `onChanged` SUR CHAQUE TUILE.
          //
          // Ces deux paramètres sont dépréciés depuis Flutter 3.32. La valeur
          // sélectionnée et le rappel de changement vivent désormais UNE SEULE
          // FOIS, sur l'ancêtre : les tuiles ne portent plus que leur propre
          // valeur. Outre la dépréciation, cela supprime la possibilité d'écrire
          // deux `groupValue` divergents dans une même liste.
          // ─────────────────────────────────────────────────────────────────────
          _card(colors, [
            RadioGroup<ThemeMode>(
              groupValue: mode,
              onChanged: (v) => ref.read(themeModeProvider.notifier).set(v ?? ThemeMode.system),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  for (var i = 0; i < themeOptions.length; i++) ...[
                    if (i > 0) Divider(height: 1, color: colors.line),
                    RadioListTile<ThemeMode>(
                      value: themeOptions[i].$1,
                      activeColor: AppTheme.brandGreen,
                      secondary: Icon(themeOptions[i].$4, color: colors.subtle),
                      title: Text(themeOptions[i].$2,
                          style: TextStyle(fontWeight: FontWeight.w600, color: colors.ink)),
                      subtitle: Text(themeOptions[i].$3,
                          style: TextStyle(fontSize: 12, color: colors.subtle)),
                    ),
                  ],
                ],
              ),
            ),
          ]),
          const SizedBox(height: 22),
          _label(l.settingsLanguage, colors),
          _card(colors, [
            RadioGroup<String>(
              groupValue: locale?.languageCode ?? 'system',
              // Le rappel est désormais UNIQUE pour tout le groupe : il ne peut
              // plus capturer l'option de la tuile touchée, comme le faisait
              // l'ancien `onChanged` par tuile. On reconstruit donc le `Locale`
              // depuis le code reçu — « system » signifiant « suivre le
              // téléphone », d'où le `null`.
              onChanged: (code) => ref.read(localeProvider.notifier).set(
                    code == null || code == 'system' ? null : Locale(code),
                  ),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  for (var i = 0; i < langOptions.length; i++) ...[
                    if (i > 0) Divider(height: 1, color: colors.line),
                    RadioListTile<String>(
                      value: langOptions[i].$1?.languageCode ?? 'system',
                      activeColor: AppTheme.brandGreen,
                      secondary: Icon(langOptions[i].$4, color: colors.subtle),
                      title: Text(langOptions[i].$2,
                          style: TextStyle(fontWeight: FontWeight.w600, color: colors.ink)),
                      subtitle: langOptions[i].$3 == null
                          ? null
                          : Text(langOptions[i].$3!,
                              style: TextStyle(fontSize: 12, color: colors.subtle)),
                    ),
                  ],
                ],
              ),
            ),
          ]),
        ],
      ),
    );
  }

  Widget _label(String text, AppColors colors) => Padding(
        padding: const EdgeInsets.fromLTRB(4, 0, 4, 8),
        child: Text(text, style: TextStyle(fontWeight: FontWeight.w800, color: colors.ink)),
      );

  Widget _card(AppColors colors, List<Widget> children) => Container(
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: colors.line),
        ),
        child: Column(children: children),
      );
}
