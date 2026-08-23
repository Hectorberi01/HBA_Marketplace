import 'package:flutter/material.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../core/theme/app_theme.dart';

/// Composants visuels partagés — le vocabulaire de l'app cliente, décliné PRO.

/// Logo de la marque (repris de l'app vendeur : silhouette de l'Afrique).
///
/// Deux usages, deux fichiers :
///  • [onGreen] = true  → la marque DÉTOURÉE en blanc, posée directement sur un
///    fond vert (splash). Poser le logo d'origine — qui porte son propre fond
///    vert #3EA536 — sur le vert du thème #1F8A4C ferait apparaître un carré
///    d'une autre teinte, bien visible.
///  • [onGreen] = false → le logo complet, dans sa pastille arrondie (connexion).
class BrandLogo extends StatelessWidget {
  const BrandLogo({super.key, this.size = 72, this.onGreen = false});

  final double size;
  final bool onGreen;

  @override
  Widget build(BuildContext context) {
    // Le SYMBOLE seul (sans le nom) : le nom est toujours écrit en texte juste à
    // côté ; l'inclure dans l'image le dupliquerait, et à 64 px il serait de
    // toute façon illisible.
    final symbol = Image.asset('assets/brand/symbol_white.png', fit: BoxFit.contain);

    if (onGreen) {
      return SizedBox(width: size, height: size, child: symbol);
    }

    return Container(
      width: size,
      height: size,
      padding: EdgeInsets.all(size * 0.18),
      decoration: BoxDecoration(
        color: AppTheme.brandGreen,
        borderRadius: BorderRadius.circular(size * 0.28),
        boxShadow: [
          BoxShadow(color: Colors.black.withValues(alpha: 0.10), blurRadius: 18, offset: const Offset(0, 8)),
        ],
      ),
      child: symbol,
    );
  }
}

/// Verrouillage complet de la marque : monogramme + nom, en blanc, sur fond vert
/// (écran de démarrage).
///
/// Le nom est du TEXTE, pas de l'image. La marque est désormais un monogramme —
/// « HBA » seul, sans lettrage du nom. Le composer en texte a trois vertus :
/// il reste net à toutes les densités, il se traduit, et il est lu par les
/// lecteurs d'écran, ce qu'un nom pixelisé dans un PNG n'est jamais.
class BrandLockup extends StatelessWidget {
  const BrandLockup({super.key, this.width = 220});

  final double width;

  @override
  Widget build(BuildContext context) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        SizedBox(
          width: width * 0.62,
          child: Image.asset('assets/brand/symbol_white.png', fit: BoxFit.contain),
        ),
        SizedBox(height: width * 0.06),
        Text(
          'HbaExpress PRO',
          style: TextStyle(
            color: Colors.white,
            fontSize: width * 0.115,
            fontWeight: FontWeight.w800,
            letterSpacing: 0.5,
          ),
        ),
      ],
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
        Positioned.fill(child: ColoredBox(color: AppColors.of(context).bg)),
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

/// Marges d'une feuille modale — CLAVIER ET BARRE SYSTÈME COMPRIS.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// LE BOUTON D'ACTION ÉTAIT COUPÉ SUR PRESQUE TOUTES LES FEUILLES.
///
/// Le motif répandu dans l'application était :
///
///     EdgeInsets.fromLTRB(20, 20, 20, MediaQuery.of(context).viewInsets.bottom + 20)
///
/// `viewInsets` ne décrit QUE le clavier. Il ne dit rien de la barre de navigation
/// Android, qui occupe une quarantaine de points en bas de l'écran. Clavier fermé, la
/// feuille descendait donc SOUS la barre système — et le bouton principal
/// (« Ajouter le lieu », « Envoyer pour vérification ») se retrouvait à moitié
/// recouvert, parfois intouchable. Sur les gabarits à navigation gestuelle, la barre
/// est plus fine et le défaut passait inaperçu ; sur les trois boutons classiques, il
/// bloquait l'action.
///
/// `padding.bottom` complète `viewInsets.bottom` : c'est l'inset système RESTANT,
/// celui que `SafeArea` applique. Les deux se cumulent sans jamais faire double emploi
/// — quand le clavier recouvre la barre, `padding.bottom` vaut zéro.
///
/// `systemInset: false` QUAND UN `SafeArea` EST DÉJÀ AU-DESSUS.
///
/// `SafeArea` retire bien le padding du `MediaQuery` de SON sous-arbre — mais le
/// `context` d'un `build()` désigne le widget lui-même, donc un point situé AU-DESSUS
/// du `SafeArea` qu'il retourne. `MediaQuery.of(context).padding.bottom` y vaut encore
/// l'inset complet, et les deux mécanismes s'additionneraient : une centaine de points
/// de vide sous le bouton. Les trois feuilles qui géraient déjà correctement la barre
/// système passent donc `systemInset: false`.
/// ─────────────────────────────────────────────────────────────────────────────────
EdgeInsets sheetPadding(
  BuildContext context, {
  double horizontal = 20,
  double top = 20,
  double bottom = 20,
  bool systemInset = true,
}) {
  final media = MediaQuery.of(context);
  final system = systemInset ? media.padding.bottom : 0.0;
  return EdgeInsets.fromLTRB(
    horizontal,
    top,
    horizontal,
    media.viewInsets.bottom + system + bottom,
  );
}

/// ─────────────────────────────────────────────────────────────────────────────
/// MARGE BASSE D'UN ÉCRAN DÉFILANT, EN MODE BORDS À BORDS.
///
/// Depuis qu'Android 15 impose ce mode, le contenu passe SOUS la barre de
/// navigation système. Un `padding: EdgeInsets.only(bottom: 32)` écrit à la main
/// ne suffit plus : la dernière carte, le dernier bouton, se retrouvent derrière
/// les trois boutons du téléphone — ou derrière la barre de geste.
///
/// `viewPadding.bottom` donne la hauteur réellement occupée par le système,
/// quelle que soit la navigation choisie par l'utilisateur. On y ajoute la marge
/// visuelle voulue.
///
/// Pendant du `sheetPadding` ci-dessus, pour les écrans plutôt que les feuilles.
/// ─────────────────────────────────────────────────────────────────────────────
double bottomSafePadding(BuildContext context, {double extra = 24}) =>
    MediaQuery.of(context).viewPadding.bottom + extra;

/// Poignée grise en haut d'une feuille modale : le repère visuel qui dit
/// « ceci se referme en glissant vers le bas ».
class SheetHandle extends StatelessWidget {
  const SheetHandle({super.key});

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Container(
        width: 40,
        height: 4,
        margin: const EdgeInsets.only(bottom: 16),
        decoration: BoxDecoration(
          color: AppColors.of(context).line,
          borderRadius: BorderRadius.circular(4),
        ),
      ),
    );
  }
}

/// Entrée de menu contextuel : icône + libellé, avec une déclinaison « danger ».
///
/// Les menus natifs n'affichent qu'un texte nu : sur un écran de gestion, une
/// icône permet de repérer l'action sans relire, et la couleur distingue
/// l'action destructrice de la routine.
PopupMenuItem<T> appMenuItem<T>({
  required T value,
  required IconData icon,
  required String label,
  bool danger = false,
}) {
  // Pas de BuildContext ici : la couleur du libellé « normal » est laissée à null
  // pour hériter du textStyle adaptatif du PopupMenuTheme (clair/sombre). Seul le
  // rouge « danger » est imposé.
  return PopupMenuItem<T>(
    value: value,
    height: 46,
    child: Row(
      children: [
        Icon(icon, size: 19, color: danger ? AppTheme.danger : AppTheme.subtle),
        const SizedBox(width: 12),
        Text(label,
            style: TextStyle(
                fontSize: 14, fontWeight: FontWeight.w600, color: danger ? AppTheme.danger : null)),
      ],
    ),
  );
}

/// Liste déroulante de l'app.
///
/// Le `DropdownButtonFormField` brut ouvre un menu Material par défaut : fond
/// gris, coins carrés, surlignage gris terne — étranger au reste de l'interface.
/// Ce composant impose le menu blanc arrondi, l'ombre douce et la coche verte
/// sur l'option retenue. Tous les sélecteurs de l'app passent par lui, sinon la
/// correction se perdrait à chaque nouvel écran.
class AppDropdown<T> extends StatelessWidget {
  const AppDropdown({
    super.key,
    required this.value,
    required this.label,
    required this.options,
    required this.onChanged,
    this.hint,
  });

  final T? value;
  final String label;
  final List<({T value, String label})> options;
  final ValueChanged<T?> onChanged;
  final String? hint;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    return DropdownButtonFormField<T>(
      initialValue: value,
      isExpanded: true,
      decoration: InputDecoration(labelText: label, hintText: hint),
      dropdownColor: colors.surface,
      borderRadius: BorderRadius.circular(14),
      elevation: 3,
      icon: Icon(Icons.expand_more_rounded, color: colors.subtle),
      style: TextStyle(fontSize: 15, fontWeight: FontWeight.w600, color: colors.ink),
      onChanged: onChanged,
      // `selectedItemBuilder` : ce qui s'affiche dans le CHAMP une fois choisi.
      // Sans lui, l'option (avec sa coche) serait rendue telle quelle dans le
      // champ, et la coche s'y afficherait aussi — parasite.
      selectedItemBuilder: (context) => [
        for (final o in options)
          Align(
            alignment: Alignment.centerLeft,
            child: Text(o.label, maxLines: 1, overflow: TextOverflow.ellipsis),
          ),
      ],
      items: [
        for (final o in options)
          DropdownMenuItem<T>(
            value: o.value,
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    o.label,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      fontSize: 15,
                      fontWeight: o.value == value ? FontWeight.w700 : FontWeight.w500,
                      color: o.value == value ? AppTheme.brandGreen : colors.ink,
                    ),
                  ),
                ),
                if (o.value == value)
                  const Icon(Icons.check_rounded, size: 18, color: AppTheme.brandGreen),
              ],
            ),
          ),
      ],
    );
  }
}

/// Titre de section, avec action optionnelle à droite.
class SectionHeader extends StatelessWidget {
  const SectionHeader({super.key, required this.title, this.onAction, this.actionLabel});
  final String title;
  final VoidCallback? onAction;

  /// Libellé de l'action ; à défaut, « Voir tout » localisé.
  final String? actionLabel;

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context);
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 18, 16, 10),
      child: Row(
        children: [
          Expanded(
            child: Text(title,
                style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: AppColors.of(context).ink)),
          ),
          if (onAction != null)
            GestureDetector(
              onTap: onAction,
              child: Text(actionLabel ?? l.commonSeeAll,
                  style: const TextStyle(
                      fontSize: 12, fontWeight: FontWeight.w800, color: AppTheme.brandGreen, letterSpacing: 0.3)),
            ),
        ],
      ),
    );
  }
}

/// Petit label de section en capitales grises.
class SectionLabel extends StatelessWidget {
  const SectionLabel(this.text, {super.key});
  final String text;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 18, 20, 8),
      child: Text(text.toUpperCase(),
          style: TextStyle(
              fontSize: 12, fontWeight: FontWeight.w800, color: AppColors.of(context).subtle, letterSpacing: 0.6)),
    );
  }
}

/// Carte blanche arrondie (bordure fine, pas d'ombre).
class CardSection extends StatelessWidget {
  const CardSection({super.key, required this.child, this.padding = EdgeInsets.zero, this.margin});
  final Widget child;
  final EdgeInsets padding;
  final EdgeInsets? margin;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    // `Material` ET NON `Container` : voir l'encadré de `PartnerCard`. Un
    // `ListTile` peint son encre sur le `Material` le plus proche, et un
    // `Container` coloré la masquerait. Trois écrans posent un `ListTile` ici.
    //
    // LA MARGE RESTE UN `Container` EXTÉRIEUR : `Material` n'a pas de `margin`,
    // et la lui donner par du `Padding` déplacerait le fond avec elle.
    return Container(
      margin: margin ?? const EdgeInsets.symmetric(horizontal: 16),
      child: Material(
        color: colors.surface,
        clipBehavior: Clip.antiAlias,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(16),
          side: BorderSide(color: colors.line),
        ),
        child: Padding(padding: padding, child: child),
      ),
    );
  }
}

/// Tuile de KPI (valeur + libellé + icône pastille).
class KpiTile extends StatelessWidget {
  const KpiTile({
    super.key,
    required this.label,
    required this.value,
    required this.icon,
    this.color = AppTheme.brandGreen,
    this.onTap,
  });

  final String label;
  final String value;
  final IconData icon;
  final Color color;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(16),
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(16),
          border: Border.all(color: colors.line),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 34,
              height: 34,
              decoration: BoxDecoration(color: color.withValues(alpha: 0.12), borderRadius: BorderRadius.circular(10)),
              child: Icon(icon, size: 18, color: color),
            ),
            const SizedBox(height: 10),
            FittedBox(
              fit: BoxFit.scaleDown,
              alignment: Alignment.centerLeft,
              child: Text(value,
                  style: TextStyle(fontSize: 20, fontWeight: FontWeight.w800, color: colors.ink)),
            ),
            const SizedBox(height: 2),
            Text(label, style: TextStyle(fontSize: 12, color: colors.subtle, fontWeight: FontWeight.w600)),
          ],
        ),
      ),
    );
  }
}

/// Bandeau vert (bienvenue, mise en avant).
class HeroBanner extends StatelessWidget {
  const HeroBanner({super.key, required this.title, required this.subtitle, this.tag});
  final String title;
  final String subtitle;
  final String? tag;

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.fromLTRB(16, 8, 16, 4),
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
        children: [
          if (tag != null)
            Text(tag!.toUpperCase(),
                style: TextStyle(
                    color: Colors.white.withValues(alpha: 0.85),
                    fontSize: 11,
                    fontWeight: FontWeight.w700,
                    letterSpacing: 0.5)),
          const SizedBox(height: 6),
          Text(title,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.w800, height: 1.15)),
          const SizedBox(height: 4),
          Text(subtitle, style: TextStyle(color: Colors.white.withValues(alpha: 0.9), fontSize: 13)),
        ],
      ),
    );
  }
}

/// Pastille de statut colorée.
class StatusBadge extends StatelessWidget {
  const StatusBadge({super.key, required this.label, required this.color, this.icon});
  final String label;
  final Color color;
  final IconData? icon;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
      decoration: BoxDecoration(color: color.withValues(alpha: 0.12), borderRadius: BorderRadius.circular(20)),
      child: Row(mainAxisSize: MainAxisSize.min, children: [
        if (icon != null) ...[Icon(icon, size: 13, color: color), const SizedBox(width: 4)],
        Text(label, style: TextStyle(color: color, fontSize: 12, fontWeight: FontWeight.w700)),
      ]),
    );
  }
}

/// Traduction des statuts du backend en pastilles.
///
/// Règle de sécurité : un statut INCONNU ne doit jamais s'afficher « Échec ».
/// C'est exactement le bug qu'on vient de corriger côté vendeur, où un versement
/// « Processing » (argent en route) apparaissait comme un échec. Par défaut, on
/// affiche donc un état neutre — jamais alarmant.
class StatusPill {
  const StatusPill._();

  /// Retrait / versement.
  static StatusBadge withdrawal(AppLocalizations l, String status) {
    switch (status.toLowerCase()) {
      case 'completed':
      case 'paid':
        return StatusBadge(label: l.pillPaid, color: AppTheme.brandGreen, icon: Icons.check_circle);
      case 'processing':
        return StatusBadge(label: l.pillPayoutProcessing, color: AppTheme.promoOrange, icon: Icons.sync);
      case 'requested':
      case 'pending':
        return StatusBadge(label: l.pillPending, color: AppTheme.promoOrange, icon: Icons.schedule);
      case 'rejected':
        return StatusBadge(label: l.pillRejectedM, color: AppTheme.subtle, icon: Icons.block);
      case 'failed':
        return StatusBadge(label: l.pillFailed, color: AppTheme.danger, icon: Icons.error_outline);
      default:
        return StatusBadge(label: l.pillProcessing, color: AppTheme.subtle, icon: Icons.more_horiz);
    }
  }

  /// Commande.
  static StatusBadge order(AppLocalizations l, String status) {
    switch (status.toLowerCase()) {
      case 'delivered':
        return StatusBadge(label: l.pillDelivered, color: AppTheme.brandGreen, icon: Icons.check_circle);
      case 'shipped':
        return StatusBadge(label: l.pillShipped, color: AppTheme.info, icon: Icons.local_shipping_outlined);
      case 'confirmed':
      case 'paid':
      case 'processing':
        return StatusBadge(label: l.pillToProcess, color: AppTheme.promoOrange, icon: Icons.inventory_2_outlined);
      case 'cancelled':
      case 'canceled':
        return StatusBadge(label: l.pillCancelled, color: AppTheme.danger, icon: Icons.close);
      case 'refunded':
        return StatusBadge(label: l.pillRefunded, color: AppTheme.subtle, icon: Icons.undo);
      default:
        return StatusBadge(label: l.pillPending, color: AppTheme.subtle, icon: Icons.schedule);
    }
  }

  /// Expédition.
  static StatusBadge shipment(AppLocalizations l, String status) {
    switch (status.toLowerCase()) {
      case 'delivered':
        return StatusBadge(label: l.pillDelivered, color: AppTheme.brandGreen, icon: Icons.check_circle);
      case 'shipped':
        return StatusBadge(label: l.pillShipped, color: AppTheme.info, icon: Icons.local_shipping_outlined);
      case 'prepared':
        return StatusBadge(label: l.pillPrepared, color: AppTheme.promoOrange, icon: Icons.inventory_2_outlined);
      case 'cancelled':
      case 'canceled':
        return StatusBadge(label: l.pillCancelled, color: AppTheme.danger, icon: Icons.close);
      default:
        return StatusBadge(label: l.pillToPrepare, color: AppTheme.subtle, icon: Icons.schedule);
    }
  }

  /// Produit / mise en vente (draft, active, paused, archived…).
  static StatusBadge catalog(AppLocalizations l, String status) {
    switch (status.toLowerCase()) {
      case 'active':
        return StatusBadge(label: l.pillOnline, color: AppTheme.brandGreen, icon: Icons.visibility_outlined);
      case 'paused':
        return StatusBadge(label: l.pillPaused, color: AppTheme.promoOrange, icon: Icons.pause_circle_outline);
      case 'draft':
        return StatusBadge(label: l.pillDraft, color: AppTheme.subtle, icon: Icons.edit_outlined);
      case 'archived':
      case 'closed':
        return StatusBadge(label: l.pillArchived, color: AppTheme.subtle, icon: Icons.archive_outlined);
      default:
        return StatusBadge(label: status.isEmpty ? '—' : status, color: AppTheme.subtle);
    }
  }

  /// KYB de la boutique.
  static StatusBadge kyb(AppLocalizations l, String status) {
    switch (status.toLowerCase()) {
      case 'verified':
        return StatusBadge(label: l.pillVerified, color: AppTheme.brandGreen, icon: Icons.verified_outlined);
      case 'rejected':
        return StatusBadge(label: l.pillRejectedF, color: AppTheme.danger, icon: Icons.error_outline);
      case 'pending':
      case 'submitted':
      case 'inreview':
        return StatusBadge(label: l.pillInReview, color: AppTheme.promoOrange, icon: Icons.schedule);
      default:
        return StatusBadge(label: l.pillToComplete, color: AppTheme.subtle, icon: Icons.description_outlined);
    }
  }
}

/// Ligne de réglage / raccourci (icône + libellé + chevron + badge).
class SettingsTile extends StatelessWidget {
  const SettingsTile({
    super.key,
    required this.icon,
    required this.label,
    required this.onTap,
    this.subtitle,
    this.iconColor,
    this.iconBackground,
    this.badge,
    this.trailing,
  });

  final IconData icon;
  final String label;
  final String? subtitle;
  final VoidCallback onTap;
  final Color? iconColor;
  final Color? iconBackground;
  final String? badge;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    return InkWell(
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
        child: Row(children: [
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              color: iconBackground ?? colors.softGreen,
              borderRadius: BorderRadius.circular(12),
            ),
            child: Icon(icon, size: 20, color: iconColor ?? AppTheme.brandGreen),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(label,
                    style: TextStyle(fontSize: 15, fontWeight: FontWeight.w600, color: colors.ink)),
                if (subtitle != null) ...[
                  const SizedBox(height: 2),
                  Text(subtitle!, style: TextStyle(fontSize: 12, color: colors.subtle)),
                ],
              ],
            ),
          ),
          if (badge != null) ...[
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
              decoration: const BoxDecoration(color: AppTheme.danger, shape: BoxShape.circle),
              constraints: const BoxConstraints(minWidth: 20, minHeight: 20),
              alignment: Alignment.center,
              child: Text(badge!,
                  style: const TextStyle(color: Colors.white, fontSize: 11, fontWeight: FontWeight.w700)),
            ),
            const SizedBox(width: 8),
          ],
          trailing ?? Icon(Icons.chevron_right, color: colors.subtle),
        ]),
      ),
    );
  }
}

/// Puce de filtre sélectionnable.
class FilterChipPill extends StatelessWidget {
  const FilterChipPill({super.key, required this.label, required this.selected, required this.onTap});
  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    return GestureDetector(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 9),
        decoration: BoxDecoration(
          color: selected ? AppTheme.brandGreen : colors.surface,
          borderRadius: BorderRadius.circular(22),
          border: Border.all(color: selected ? AppTheme.brandGreen : colors.line),
        ),
        child: Text(label,
            style: TextStyle(
                color: selected ? Colors.white : colors.ink, fontWeight: FontWeight.w600, fontSize: 13)),
      ),
    );
  }
}

/// Ligne « libellé → valeur » (récapitulatifs, décomposition de prix).
class KeyValueRow extends StatelessWidget {
  const KeyValueRow({super.key, required this.label, required this.value, this.strong = false, this.color});
  final String label;
  final String value;
  final bool strong;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 5),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // Libellé à sa largeur naturelle. S'il était en Expanded, une valeur
          // longue (ex. fil d'ariane catégorie) le comprimait à ~0 px et il
          // s'affichait une lettre par ligne.
          Text(label,
              style: TextStyle(
                  fontSize: 13,
                  color: strong ? colors.ink : colors.subtle,
                  fontWeight: strong ? FontWeight.w700 : FontWeight.w500)),
          const SizedBox(width: 12),
          // La valeur prend l'espace restant, alignée à droite, et revient à la
          // ligne au lieu de déborder hors de l'écran.
          Expanded(
            child: Text(value,
                textAlign: TextAlign.right,
                style: TextStyle(
                    fontSize: strong ? 15 : 13,
                    fontWeight: strong ? FontWeight.w800 : FontWeight.w600,
                    color: color ?? colors.ink)),
          ),
        ],
      ),
    );
  }
}
