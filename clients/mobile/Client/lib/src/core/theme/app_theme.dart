import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

/// Thème Material 3 de la marketplace (vert primaire).
///
/// Les tokens de PALETTE (bg, ink, subtle, line, softGreen, surface) sont
/// sensibles au mode via [brightness] : régler `AppTheme.brightness` bascule
/// TOUTE l'app en clair/sombre sans toucher les centaines de points d'appel.
/// (Posé par app.dart selon le réglage utilisateur/système.)
class AppTheme {
  const AppTheme._();

  /// Mode courant. Modifié par app.dart avant de construire l'arbre.
  static Brightness brightness = Brightness.light;
  static bool get _dark => brightness == Brightness.dark;

  // Accents de marque : identiques dans les deux modes.
  static const Color brandGreen = Color(0xFF1F8A4C);
  static const Color brandGreenDark = Color(0xFF177A41);
  static const Color promoOrange = Color(0xFFF2A03D);
  static const Color star = Color(0xFFF5B301);
  static const Color danger = Color(0xFFE5484D);

  // Palette sensible au mode.
  static Color get ink => _dark ? const Color(0xFFEAEFEC) : const Color(0xFF18211C); // texte principal
  static Color get subtle => _dark ? const Color(0xFF9BA6A1) : const Color(0xFF7A8580); // texte secondaire
  static Color get bg => _dark ? const Color(0xFF0E1512) : const Color(0xFFF4F6F5); // fond d'écran
  static Color get line => _dark ? const Color(0xFF26302B) : const Color(0xFFE8ECEA); // bordures fines
  static Color get softGreen => _dark ? const Color(0xFF15321F) : const Color(0xFFE7F3EC); // pastilles vertes
  static Color get surface => _dark ? const Color(0xFF17201B) : Colors.white; // cartes / champs

  static const Color _seed = brandGreen;
  static const Color _accent = promoOrange;

  static ThemeData light() => _build(Brightness.light);
  static ThemeData dark() => _build(Brightness.dark);

  static ThemeData _build(Brightness b) {
    final isDark = b == Brightness.dark;
    final scheme = ColorScheme.fromSeed(
      seedColor: _seed,
      primary: _seed,
      secondary: _accent,
      brightness: b,
    ).copyWith(surface: isDark ? const Color(0xFF17201B) : Colors.white);

    final onSurface = isDark ? const Color(0xFFEAEFEC) : const Color(0xFF18211C);
    final baseTextTheme = ThemeData(brightness: b).textTheme;
    final textTheme = GoogleFonts.plusJakartaSansTextTheme(baseTextTheme).apply(
      bodyColor: onSurface,
      displayColor: onSurface,
    );

    final cardColor = isDark ? const Color(0xFF17201B) : Colors.white;

    // DÉRIVÉ DE `b`, PAS DU GETTER STATIQUE `AppTheme.line`.
    //
    // `app.dart` construit `theme:` ET `darkTheme:` dans la MÊME passe, alors que
    // `AppTheme.brightness` ne vaut qu'une seule chose à cet instant. Lire `line`
    // ici graverait donc la même couleur de séparateur dans les deux thèmes — et
    // reproduirait, par un autre chemin, le défaut que ce réglage corrige.
    final dividerColor = isDark ? const Color(0xFF26302B) : const Color(0xFFE8ECEA);

    return ThemeData(
      dividerColor: dividerColor,
      dividerTheme: DividerThemeData(color: dividerColor, thickness: 1, space: 1),
      colorScheme: scheme,
      useMaterial3: true,
      textTheme: textTheme,
      scaffoldBackgroundColor: isDark ? const Color(0xFF0E1512) : const Color(0xFFF7F8F8),
      appBarTheme: AppBarTheme(
        backgroundColor: scheme.surface,
        foregroundColor: scheme.onSurface,
        elevation: 0,
        centerTitle: false,
        titleTextStyle: GoogleFonts.plusJakartaSans(color: onSurface, fontSize: 19, fontWeight: FontWeight.w800),
      ),
      cardTheme: CardThemeData(
        elevation: 0,
        color: cardColor,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(16),
          side: BorderSide(color: scheme.outlineVariant),
        ),
        margin: EdgeInsets.zero,
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: cardColor,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: BorderSide(color: scheme.outlineVariant),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: BorderSide(color: scheme.outlineVariant),
        ),
        contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 14),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          minimumSize: const Size(0, 50),
          padding: const EdgeInsets.symmetric(horizontal: 20),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
          textStyle: GoogleFonts.plusJakartaSans(fontSize: 16, fontWeight: FontWeight.w700),
        ),
      ),
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: cardColor,
        // Material 3 superpose au fond une TEINTE dérivée de la couleur primaire,
        // proportionnelle à l'élévation. Le fond restait donc verdâtre malgré
        // `cardColor` = blanc. On neutralise la teinte et l'élévation : la barre
        // est franchement blanche, séparée du contenu par sa seule bordure.
        surfaceTintColor: Colors.transparent,
        shadowColor: Colors.transparent,
        elevation: 0,
        indicatorColor: scheme.primaryContainer,
        // 11 sp au lieu de 12 : « Commandes » débordait et passait sur deux lignes,
        // ses deux « m » le rendant plus large que « Rechercher » malgré une lettre
        // de moins. Cinq onglets ne laissent qu'environ 80 px chacun.
        labelTextStyle: WidgetStateProperty.all(
          GoogleFonts.plusJakartaSans(fontSize: 11, fontWeight: FontWeight.w700),
        ),
      ),
      // ─────────────────────────────────────────────────────────────────────
      // FEUILLES MODALES — MÊME CORRECTIF QUE LA BARRE DE NAVIGATION.
      //
      // Sans réglage, Material 3 superpose au fond une TEINTE dérivée de la
      // couleur primaire, proportionnelle à l'élévation. Le vert de marque
      // donnait donc des feuilles verdâtres et délavées — bien visible sur le
      // sélecteur de commune, qui occupe presque tout l'écran.
      //
      // Une seule déclaration ici corrige les 16 feuilles de l'application.
      // ─────────────────────────────────────────────────────────────────────
      bottomSheetTheme: BottomSheetThemeData(
        backgroundColor: cardColor,
        surfaceTintColor: Colors.transparent,
        modalBackgroundColor: cardColor,
        modalBarrierColor: Colors.black.withValues(alpha: 0.45),
        elevation: 0,
        modalElevation: 0,
        // On ne force PAS `showDragHandle` ici : plusieurs feuilles dessinent
        // déjà la leur, et l'activer globalement en ferait apparaître deux.
        // On se contente d'en harmoniser la couleur là où elle est demandée.
        dragHandleColor: dividerColor,
        shape: const RoundedRectangleBorder(
          borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
        ),
      ),
      chipTheme: ChipThemeData(
        backgroundColor: scheme.surfaceContainerHighest,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
      ),
      // Menus contextuels (tri, actions commande…). Sans ça, Material 3 applique
      // une teinte automatique dérivée du vert de marque → un fond verdâtre
      // délavé. On force un fond de carte net, teinte neutralisée, coins arrondis.
      popupMenuTheme: PopupMenuThemeData(
        color: cardColor,
        surfaceTintColor: Colors.transparent,
        elevation: 8,
        shadowColor: Colors.black.withValues(alpha: 0.18),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
        textStyle: GoogleFonts.plusJakartaSans(color: onSurface, fontSize: 15, fontWeight: FontWeight.w600),
      ),
    );
  }
}
