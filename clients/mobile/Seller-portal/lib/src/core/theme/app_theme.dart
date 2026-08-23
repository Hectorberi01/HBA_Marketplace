import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

/// Couleurs SÉMANTIQUES qui changent selon le mode (clair/sombre).
///
/// Le thème Material gère déjà les surfaces standard (barres, cartes, champs…).
/// Cette extension couvre les surfaces PEINTES À LA MAIN dans les écrans (fonds de
/// cartes personnalisées, séparateurs, textes) : on les résout via
/// `AppColors.of(context)` pour qu'elles suivent le mode.
@immutable
class AppColors extends ThemeExtension<AppColors> {
  const AppColors({
    required this.surface,
    required this.bg,
    required this.ink,
    required this.subtle,
    required this.line,
    required this.softGreen,
  });

  /// Fond des cartes / feuilles (blanc en clair).
  final Color surface;

  /// Fond d'écran général.
  final Color bg;

  /// Texte principal.
  final Color ink;

  /// Texte secondaire.
  final Color subtle;

  /// Traits / bordures / séparateurs.
  final Color line;

  /// Pastille verte douce (avatars, badges).
  final Color softGreen;

  /// Valeurs de la maquette HBA Partner.
  ///
  /// `surface` reste blanc et `bg` prend le gris #F4F6F5 : la maquette pose des
  /// cartes blanches sur un fond légèrement teinté. Les rendre identiques ferait
  /// disparaître la limite des cartes, que rien d'autre ne dessine — elles n'ont
  /// qu'un filet très clair et aucune ombre portée.
  static const AppColors light = AppColors(
    surface: Colors.white,
    bg: Color(0xFFF4F6F5),
    ink: Color(0xFF14202B),
    subtle: Color(0xFF5A6B7B),
    line: Color(0xFFE3E8E6),
    softGreen: Color(0xFFE6F4EF),
  );

  static const AppColors dark = AppColors(
    surface: Color(0xFF171C1A),
    bg: Color(0xFF0F1312),
    ink: Color(0xFFF0F3F1),
    subtle: Color(0xFF9AA5A0),
    line: Color(0xFF2A302D),
    softGreen: Color(0xFF1D2A22),
  );

  static AppColors of(BuildContext context) =>
      Theme.of(context).extension<AppColors>() ?? light;

  @override
  AppColors copyWith({
    Color? surface,
    Color? bg,
    Color? ink,
    Color? subtle,
    Color? line,
    Color? softGreen,
  }) =>
      AppColors(
        surface: surface ?? this.surface,
        bg: bg ?? this.bg,
        ink: ink ?? this.ink,
        subtle: subtle ?? this.subtle,
        line: line ?? this.line,
        softGreen: softGreen ?? this.softGreen,
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
      softGreen: Color.lerp(softGreen, other.softGreen, t)!,
    );
  }
}

/// Thème Material 3 de HbaExpress PRO (clair + sombre).
///
/// Le vert reste la couleur d'action ; l'orange signale l'attente, le rouge
/// l'échec — convention reprise par les pastilles de statut.
class AppTheme {
  const AppTheme._();

  // ═══════════════════════════════════════════════════════════════════════════
  // PALETTE HBA PARTNER — relevée sur la maquette.
  //
  // Les six premières valeurs sont NOMMÉES dans la maquette, avec leur rôle.
  // Elles remplacent l'ancien vert #1F8A4C : le changement se propage à tous
  // les écrans, puisque tout dérive de `brandGreen` via le ColorScheme.
  // ═══════════════════════════════════════════════════════════════════════════

  /// Vert HBA — primaire.
  static const Color brandGreen = Color(0xFF087A59);

  /// Vert foncé — état pressé.
  static const Color brandGreenDark = Color(0xFF05583F);

  /// Vert clair — fonds de succès et pastilles.
  static const Color brandGreenSoft = Color(0xFFE6F4EF);

  /// Charcoal — titres.
  static const Color charcoal = Color(0xFF14202B);

  /// Gris bleuté — textes secondaires.
  static const Color slate = Color(0xFF5A6B7B);

  /// Fond d'écran général.
  static const Color surfaceBg = Color(0xFFF4F6F5);

  // ═══════════════════════════════════════════════════════════════════════════
  // COULEURS D'UNIVERS — L'AMBRE N'EST PAS UNE VALEUR RELEVÉE.
  //
  // La maquette distingue partout HBAEXPRESS (vert) de HBA FOOD (ambre) :
  // badges de commande, cartes d'activité, compteurs « à préparer ». Mais la
  // section COULEURS de la maquette ne nomme que six teintes, et l'ambre n'en
  // fait pas partie — elle n'apparaît que dans les pastilles « Attention »,
  // dont le code hexadécimal n'était pas lisible sur les captures.
  //
  // Les deux valeurs ci-dessous sont donc ESTIMÉES à l'œil. Elles rendent
  // l'écran juste, pas exact. À confronter au fichier de design avant toute
  // livraison — une teinte de marque approximative se remarque quand les deux
  // univers se côtoient dans une même liste, ce qui est précisément le cas de
  // l'écran « Commandes globales ».
  // ═══════════════════════════════════════════════════════════════════════════

  /// HBA Food — accent ambre. ESTIMÉ, à confirmer.
  static const Color foodAmber = Color(0xFFB4741A);

  /// HBA Food — fond de pastille. ESTIMÉ, à confirmer.
  static const Color foodAmberSoft = Color(0xFFFDF1E3);

  static const Color promoOrange = foodAmber; // en attente / en cours
  static const Color star = Color(0xFFF5B301);
  static const Color danger = Color(0xFFE5484D); // échec / déconnexion

  /// Fond de pastille d'ALERTE — le pendant de [brandGreenSoft].
  ///
  /// IL MANQUAIT, et son absence se voyait : les états d'alerte prenaient soit
  /// `danger` en pleine saturation — illisible sous du texte rouge — soit un
  /// gris neutre qui les faisait passer pour des états normaux. Même dérivation
  /// que les autres fonds doux : la teinte, très désaturée, très claire.
  static const Color dangerSoft = Color(0xFFFDECEC);
  static const Color info = Color(0xFF2F6FED); // information neutre

  // ═══════════════════════════════════════════════════════════════════════════
  // MÉTRIQUES DE LA MAQUETTE.
  //
  // `fieldHeight` ET `minTapTarget` NE SONT PAS LA MÊME CHOSE.
  //
  // La maquette annonce « Champs 52px » et « Zones tactiles ≥ 48px ». Un champ
  // de 52 satisfait donc la seconde règle, mais un lien texte de 20px de haut
  // ne la satisfait PAS — c'est le cas de « Mot de passe oublié ? », qui doit
  // être enveloppé pour atteindre 48. Confondre les deux valeurs produit des
  // écrans conformes en apparence et inutilisables au pouce.
  // ═══════════════════════════════════════════════════════════════════════════

  /// Hauteur des champs de saisie.
  static const double fieldHeight = 52;

  /// Hauteur du bouton d'action principal.
  static const double primaryButtonHeight = 56;

  /// Plancher de zone tactile, y compris pour les liens texte.
  static const double minTapTarget = 48;

  static const double radiusField = 12;
  static const double radiusCard = 16;

  // Tokens sémantiques « legacy » : valeurs du mode CLAIR, conservées pour que les
  // écrans qui les référencent directement compilent et restent identiques en clair.
  // Leur version adaptative vit dans AppColors (résolue par contexte) ; la bascule
  // écran par écran vers AppColors.of(context) est en cours.
  static const Color ink = charcoal;
  static const Color subtle = slate;
  static const Color bg = surfaceBg;
  static const Color line = Color(0xFFE3E8E6);
  static const Color softGreen = brandGreenSoft;

  static ThemeData light() => _build(Brightness.light, AppColors.light);
  static ThemeData dark() => _build(Brightness.dark, AppColors.dark);

  static ThemeData _build(Brightness brightness, AppColors c) {
    final scheme = ColorScheme.fromSeed(
      seedColor: brandGreen,
      primary: brandGreen,
      secondary: promoOrange,
      brightness: brightness,
    ).copyWith(surface: c.surface);

    final baseTextTheme = ThemeData(brightness: brightness).textTheme;
    final textTheme = GoogleFonts.plusJakartaSansTextTheme(baseTextTheme).apply(
      bodyColor: c.ink,
      displayColor: c.ink,
    );

    return ThemeData(
      colorScheme: scheme,
      useMaterial3: true,
      brightness: brightness,
      textTheme: textTheme,
      extensions: [c],
      scaffoldBackgroundColor: c.bg,
      appBarTheme: AppBarTheme(
        backgroundColor: c.surface,
        foregroundColor: c.ink,
        elevation: 0,
        centerTitle: false,
        titleTextStyle: GoogleFonts.plusJakartaSans(
          color: c.ink,
          fontSize: 19,
          fontWeight: FontWeight.w800,
        ),
      ),
      cardTheme: CardThemeData(
        elevation: 0,
        color: c.surface,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(16),
          side: BorderSide(color: c.line),
        ),
        margin: EdgeInsets.zero,
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: c.surface,
        // Le libellé reste TOUJOURS au-dessus du champ (jamais posé dedans comme
        // un simple placeholder qui disparaît). Le vendeur voit d'un coup d'œil à
        // quoi sert chaque champ, même vide — d'où les `hintText` qui donnent
        // alors un exemple à l'intérieur.
        floatingLabelBehavior: FloatingLabelBehavior.always,
        labelStyle: TextStyle(color: c.subtle, fontWeight: FontWeight.w600),
        hintStyle: TextStyle(color: c.subtle.withValues(alpha: 0.6)),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: BorderSide(color: c.line),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: BorderSide(color: c.line),
        ),
        contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 14),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          // Hauteur mini 50, SANS largeur mini : Size.fromHeight imposerait une
          // largeur infinie et casserait les boutons placés dans une Row.
          minimumSize: const Size(0, 50),
          padding: const EdgeInsets.symmetric(horizontal: 20),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
          textStyle: GoogleFonts.plusJakartaSans(fontSize: 16, fontWeight: FontWeight.w700),
        ),
      ),
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: c.surface,
        indicatorColor: scheme.primaryContainer,
        labelTextStyle: WidgetStateProperty.all(
          GoogleFonts.plusJakartaSans(fontSize: 11, fontWeight: FontWeight.w700),
        ),
      ),
      chipTheme: ChipThemeData(
        backgroundColor: scheme.surfaceContainerHighest,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
      ),
      // Menus contextuels : mêmes arrondis et même ombre douce que les cartes.
      popupMenuTheme: PopupMenuThemeData(
        color: c.surface,
        elevation: 8,
        shadowColor: Colors.black.withValues(alpha: 0.18),
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(16),
          side: BorderSide(color: c.line),
        ),
        textStyle: GoogleFonts.plusJakartaSans(fontSize: 14, fontWeight: FontWeight.w600, color: c.ink),
      ),
      // Boîtes de dialogue : surface unie, arrondie, sans teinte M3.
      dialogTheme: DialogThemeData(
        backgroundColor: c.surface,
        surfaceTintColor: Colors.transparent,
        elevation: 12,
        shadowColor: Colors.black.withValues(alpha: 0.20),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
        titleTextStyle: GoogleFonts.plusJakartaSans(
          fontSize: 17,
          fontWeight: FontWeight.w800,
          color: c.ink,
        ),
        contentTextStyle: GoogleFonts.plusJakartaSans(
          fontSize: 13,
          height: 1.45,
          color: c.subtle,
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          minimumSize: const Size(0, 48),
          foregroundColor: c.ink,
          side: BorderSide(color: c.line),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
          textStyle: GoogleFonts.plusJakartaSans(fontSize: 15, fontWeight: FontWeight.w700),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          foregroundColor: brandGreen,
          textStyle: GoogleFonts.plusJakartaSans(fontSize: 14, fontWeight: FontWeight.w700),
        ),
      ),
      dividerTheme: DividerThemeData(color: c.line, space: 1, thickness: 1),
      bottomSheetTheme: BottomSheetThemeData(
        backgroundColor: c.surface,
        surfaceTintColor: Colors.transparent,
        shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(22))),
      ),
    );
  }
}
