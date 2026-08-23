import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../core/theme/app_theme.dart';

/// Bibliothèque de composants visuels partagés, calqués sur les maquettes.

/// Marge basse à réserver dans une FEUILLE MODALE (`showModalBottomSheet`).
///
/// ─────────────────────────────────────────────────────────────────────────────
/// DEUX obstacles peuvent recouvrir le bas d'une feuille, et il faut les traiter
/// tous les deux :
///
///   • `viewInsets.bottom`  — le CLAVIER, quand un champ a le focus ;
///   • `viewPadding.bottom` — la BARRE SYSTÈME de l'appareil (trois boutons ou
///     barre de geste). Elle ne bouge pas et n'apparaît nulle part dans
///     `viewInsets` : une feuille qui n'utilise que ce dernier laisse son bouton
///     principal PASSER SOUS la barre Android, hors d'atteinte.
///
/// On prend le MAXIMUM des deux, jamais leur somme : lorsque le clavier est
/// ouvert, il recouvre déjà l'emplacement de la barre système. Les additionner
/// ferait remonter la feuille d'une cinquantaine de pixels de trop.
///
/// [extra] est la respiration visuelle voulue par la maquette, ajoutée par-dessus.
/// ─────────────────────────────────────────────────────────────────────────────
double sheetBottomInset(BuildContext context, {double extra = 24}) {
  final mq = MediaQuery.of(context);
  return math.max(mq.viewInsets.bottom, mq.viewPadding.bottom) + extra;
}

/// ─────────────────────────────────────────────────────────────────────────────
/// MARGE BASSE D'UN ÉCRAN DÉFILANT, EN MODE BORDS À BORDS.
///
/// Depuis qu'Android 15 impose ce mode, le contenu passe SOUS la barre de
/// navigation système. Un `padding: EdgeInsets.only(bottom: 24)` écrit à la main
/// ne suffit plus : la dernière carte, le dernier bouton, se retrouvent derrière
/// les trois boutons du téléphone — ou derrière la barre de geste.
///
/// `viewPadding.bottom` donne la hauteur réellement occupée par le système,
/// quelle que soit la navigation choisie par l'utilisateur (boutons ou gestes).
/// On y ajoute la marge visuelle voulue.
/// ─────────────────────────────────────────────────────────────────────────────
double bottomSafePadding(BuildContext context, {double extra = 24}) =>
    MediaQuery.of(context).viewPadding.bottom + extra;


/// Logo de marque : carré arrondi avec le monogramme « H » (HbaExpress).
class BrandLogo extends StatelessWidget {
  const BrandLogo({super.key, this.size = 72, this.onGreen = false});
  final double size;
  final bool onGreen;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        color: onGreen ? Colors.white : AppTheme.brandGreen,
        borderRadius: BorderRadius.circular(size * 0.28),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.08),
            blurRadius: 18,
            offset: const Offset(0, 8),
          ),
        ],
      ),
      alignment: Alignment.center,
      child: Text(
        'H',
        style: TextStyle(
          color: onGreen ? AppTheme.brandGreen : Colors.white,
          fontSize: size * 0.5,
          fontWeight: FontWeight.w800,
        ),
      ),
    );
  }
}

/// Fond clair avec halos verts diffus (écrans d'authentification).
class GlowBackground extends StatelessWidget {
  const GlowBackground({super.key, required this.child});
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Stack(
      children: [
        Positioned.fill(child: ColoredBox(color: AppTheme.bg)),
        Positioned(top: -80, right: -60, child: _blob(220)),
        Positioned(bottom: -100, left: -80, child: _blob(260)),
        child,
      ],
    );
  }

  Widget _blob(double d) => Container(
        width: d,
        height: d,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          gradient: RadialGradient(colors: [
            AppTheme.brandGreen.withValues(alpha: 0.10),
            AppTheme.brandGreen.withValues(alpha: 0.0),
          ]),
        ),
      );
}

/// Titre de section avec lien « VOIR TOUT » optionnel.
class SectionHeader extends StatelessWidget {
  const SectionHeader({super.key, required this.title, this.onSeeAll, this.seeAllLabel = 'VOIR TOUT'});
  final String title;
  final VoidCallback? onSeeAll;
  final String seeAllLabel;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 18, 16, 10),
      child: Row(
        children: [
          Expanded(
            child: Text(title, style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: AppTheme.ink)),
          ),
          if (onSeeAll != null)
            GestureDetector(
              onTap: onSeeAll,
              child: Text(seeAllLabel,
                  style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w800, color: AppTheme.brandGreen, letterSpacing: 0.3)),
            ),
        ],
      ),
    );
  }
}

/// Petit label de section en capitales grises (« GÉNÉRAL », « SUPPORT »…).
class SectionLabel extends StatelessWidget {
  const SectionLabel(this.text, {super.key});
  final String text;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 18, 20, 8),
      child: Text(text.toUpperCase(),
          style: TextStyle(fontSize: 12, fontWeight: FontWeight.w800, color: AppTheme.subtle, letterSpacing: 0.6)),
    );
  }
}

/// Bandeau promotionnel vert (offre spéciale).
class PromoBanner extends StatelessWidget {
  const PromoBanner({super.key, required this.title, this.tag = 'OFFRE SPÉCIALE', this.onTap});
  final String title;
  final String tag;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
      child: GestureDetector(
        onTap: onTap,
        child: Container(
          height: 116,
          padding: const EdgeInsets.all(18),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(18),
            gradient: const LinearGradient(
              colors: [AppTheme.brandGreenDark, AppTheme.brandGreen],
              begin: Alignment.centerLeft,
              end: Alignment.centerRight,
            ),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Text(tag,
                  style: TextStyle(color: Colors.white.withValues(alpha: 0.85), fontSize: 11, fontWeight: FontWeight.w700, letterSpacing: 0.5)),
              const SizedBox(height: 6),
              Text(title,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.w800, height: 1.15)),
            ],
          ),
        ),
      ),
    );
  }
}

/// Pastille de statut colorée (commande, état…).
class StatusBadge extends StatelessWidget {
  const StatusBadge({super.key, required this.label, required this.color, this.icon});
  final String label;
  final Color color;
  final IconData? icon;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(20),
      ),
      child: Row(mainAxisSize: MainAxisSize.min, children: [
        if (icon != null) ...[Icon(icon, size: 13, color: color), const SizedBox(width: 4)],
        Text(label, style: TextStyle(color: color, fontSize: 12, fontWeight: FontWeight.w700)),
      ]),
    );
  }
}

/// Bouton rond clair en superposition (retour, partage, favori sur image).
class SoftCircleButton extends StatelessWidget {
  const SoftCircleButton({super.key, required this.icon, required this.onTap, this.color, this.background, this.semanticLabel});
  final IconData icon;
  final VoidCallback onTap;
  final Color? color;
  final Color? background;

  /// Libellé annoncé par les lecteurs d'écran (bouton à icône seule).
  final String? semanticLabel;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      button: true,
      label: semanticLabel,
      child: Material(
        color: background ?? AppTheme.surface,
        shape: const CircleBorder(),
        elevation: 1.5,
        shadowColor: Colors.black26,
        child: InkWell(
          customBorder: const CircleBorder(),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.all(9),
            child: Icon(icon, size: 20, color: color ?? AppTheme.ink),
          ),
        ),
      ),
    );
  }
}

/// Ligne de réglage / raccourci (icône pastille + libellé + chevron + badge).
class SettingsTile extends StatelessWidget {
  const SettingsTile({
    super.key,
    required this.icon,
    required this.label,
    required this.onTap,
    this.iconColor,
    this.iconBackground,
    this.badge,
    this.trailing,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;
  final Color? iconColor;
  final Color? iconBackground;
  final String? badge;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
        child: Row(children: [
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              color: iconBackground ?? AppTheme.softGreen,
              borderRadius: BorderRadius.circular(12),
            ),
            child: Icon(icon, size: 20, color: iconColor ?? AppTheme.brandGreen),
          ),
          const SizedBox(width: 14),
          Expanded(child: Text(label, style: TextStyle(fontSize: 15, fontWeight: FontWeight.w600, color: AppTheme.ink))),
          if (badge != null) ...[
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
              decoration: const BoxDecoration(color: AppTheme.danger, shape: BoxShape.circle),
              constraints: const BoxConstraints(minWidth: 20, minHeight: 20),
              alignment: Alignment.center,
              child: Text(badge!, style: const TextStyle(color: Colors.white, fontSize: 11, fontWeight: FontWeight.w700)),
            ),
            const SizedBox(width: 8),
          ],
          trailing ?? Icon(Icons.chevron_right, color: AppTheme.subtle),
        ]),
      ),
    );
  }
}

/// Conteneur carte blanc arrondi (sans ombre, bordure fine) — regroupe des lignes.
class CardSection extends StatelessWidget {
  const CardSection({super.key, required this.child, this.padding = EdgeInsets.zero, this.margin});
  final Widget child;
  final EdgeInsets padding;
  final EdgeInsets? margin;

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: margin ?? const EdgeInsets.symmetric(horizontal: 16),
      padding: padding,
      decoration: BoxDecoration(
        color: AppTheme.surface,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: AppTheme.line),
      ),
      child: child,
    );
  }
}

/// Puce de filtre/catégorie sélectionnable (verte si active).
class FilterChipPill extends StatelessWidget {
  const FilterChipPill({super.key, required this.label, required this.selected, required this.onTap, this.trailingIcon});
  final String label;
  final bool selected;
  final VoidCallback onTap;
  final IconData? trailingIcon;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 9),
        decoration: BoxDecoration(
          color: selected ? AppTheme.brandGreen : AppTheme.surface,
          borderRadius: BorderRadius.circular(22),
          border: Border.all(color: selected ? AppTheme.brandGreen : AppTheme.line),
        ),
        child: Row(mainAxisSize: MainAxisSize.min, children: [
          Text(label, style: TextStyle(color: selected ? Colors.white : AppTheme.ink, fontWeight: FontWeight.w600, fontSize: 13)),
          if (trailingIcon != null) ...[
            const SizedBox(width: 4),
            Icon(trailingIcon, size: 16, color: selected ? Colors.white : AppTheme.subtle),
          ],
        ]),
      ),
    );
  }
}

/// Sélecteur de quantité (− valeur +).
class QuantityStepper extends StatelessWidget {
  const QuantityStepper({super.key, required this.value, required this.onChanged, this.min = 1});
  final int value;
  final ValueChanged<int> onChanged;
  final int min;

  @override
  Widget build(BuildContext context) {
    return Row(mainAxisSize: MainAxisSize.min, children: [
      _btn(Icons.remove, value > min ? () => onChanged(value - 1) : null),
      Padding(
        padding: const EdgeInsets.symmetric(horizontal: 14),
        child: Text('$value', style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 16)),
      ),
      _btn(Icons.add, () => onChanged(value + 1)),
    ]);
  }

  Widget _btn(IconData icon, VoidCallback? onTap) => Material(
        color: AppTheme.bg,
        shape: const CircleBorder(),
        child: InkWell(
          customBorder: const CircleBorder(),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.all(6),
            child: Icon(icon, size: 18, color: onTap == null ? AppTheme.subtle : AppTheme.ink),
          ),
        ),
      );
}
