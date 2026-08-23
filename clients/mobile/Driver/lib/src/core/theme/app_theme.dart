import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// CHARTE HBA — couleurs, métriques, thème.
///
/// DUPLIQUÉ DEPUIS `Seller-portal/lib/src/core/theme/app_theme.dart`.
///
/// Ce n'est pas un oubli : le partage a été écarté sciemment pour démarrer sans
/// toucher au portail vendeur. La conséquence est réelle et il faut la nommer —
/// le jour où le vert HBA change, il change à DEUX endroits aujourd'hui, TROIS
/// avec l'application client. Les trois divergeront sans que rien ne le signale,
/// parce qu'aucun test ne compare des couleurs entre deux dépôts.
///
/// À extraire dans un paquet `hba_design` dès que le second écart apparaît.
/// ═════════════════════════════════════════════════════════════════════════════
class AppTheme {
  const AppTheme._();

  // Palette de la charte, hexadécimaux relevés sur les maquettes.
  static const Color brandGreen = Color(0xFF087A59);
  static const Color brandGreenDark = Color(0xFF05583F);
  static const Color brandGreenSoft = Color(0xFFE6F4EF);
  static const Color charcoal = Color(0xFF14202B);
  static const Color slate = Color(0xFF5A6B7B);
  static const Color surfaceBg = Color(0xFFF4F6F5);

  /// Ambre : l'ATTENTION. Document qui expire, étape en cours, bonus à portée.
  ///
  /// JAMAIS POUR UNE ACTION PRINCIPALE.
  ///
  /// L'employer sur un bouton la viderait de son sens partout ailleurs — et
  /// c'est cette couleur qui doit dire au livreur qu'une assurance expire dans
  /// douze jours.
  static const Color amber = Color(0xFFB4741A);
  static const Color amberSoft = Color(0xFFFDF1E3);

  static const Color danger = Color(0xFFC0392B);
  static const Color dangerSoft = Color(0xFFFDECEC);
  static const Color info = Color(0xFF2F6FED);
  static const Color infoSoft = Color(0xFFEAF1FE);

  // Métriques.
  static const double fieldHeight = 56;
  static const double primaryButtonHeight = 56;

  /// 48 px, ET C'EST UN PLANCHER, PAS UNE SUGGESTION.
  ///
  /// Un livreur touche son écran d'une main, en mouvement, souvent avec des
  /// gants ou les doigts mouillés. C'est le contexte d'usage le plus hostile de
  /// toute la plateforme : la cible tactile y compte davantage que dans les deux
  /// autres applications.
  static const double minTapTarget = 48;

  static const double radiusCard = 16;
  static const double radiusField = 12;

  static ThemeData light() => _build(Brightness.light);

  static ThemeData _build(Brightness brightness) {
    const colors = AppColors(
      surface: Colors.white,
      bg: surfaceBg,
      ink: charcoal,
      subtle: slate,
      line: Color(0xFFE3E7E9),
    );

    final base = ThemeData(brightness: brightness);
    final textTheme = GoogleFonts.plusJakartaSansTextTheme(base.textTheme)
        .apply(bodyColor: colors.ink, displayColor: colors.ink);

    return ThemeData(
      useMaterial3: true,
      brightness: brightness,
      scaffoldBackgroundColor: colors.bg,
      textTheme: textTheme,
      colorScheme: ColorScheme.fromSeed(
        seedColor: brandGreen,
        brightness: brightness,
      ).copyWith(primary: brandGreen, surface: colors.surface),
      extensions: const [colors],
      splashFactory: InkRipple.splashFactory,
    );
  }
}

/// Couleurs contextuelles, accessibles par `AppColors.of(context)`.
class AppColors extends ThemeExtension<AppColors> {
  const AppColors({
    required this.surface,
    required this.bg,
    required this.ink,
    required this.subtle,
    required this.line,
  });

  final Color surface;
  final Color bg;
  final Color ink;
  final Color subtle;
  final Color line;

  static AppColors of(BuildContext context) =>
      Theme.of(context).extension<AppColors>()!;

  @override
  AppColors copyWith({
    Color? surface,
    Color? bg,
    Color? ink,
    Color? subtle,
    Color? line,
  }) =>
      AppColors(
        surface: surface ?? this.surface,
        bg: bg ?? this.bg,
        ink: ink ?? this.ink,
        subtle: subtle ?? this.subtle,
        line: line ?? this.line,
      );

  @override
  AppColors lerp(ThemeExtension<AppColors>? other, double t) {
    if (other is! AppColors) return this;
    return AppColors(
      surface: Color.lerp(surface, other.surface, t)!,
      bg: Color.lerp(bg, other.bg, t)!,
      ink: Color.lerp(ink, other.ink, t)!,
      subtle: Color.lerp(subtle, other.subtle, t)!,
      line: Color.lerp(line, other.line, t)!,
    );
  }
}
