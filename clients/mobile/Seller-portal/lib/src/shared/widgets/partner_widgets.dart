import 'package:flutter/material.dart';

import '../../core/theme/app_theme.dart';
import '../../features/activities/activities_data.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// VOCABULAIRE VISUEL COMMUN AUX DEUX UNIVERS.
///
/// Ces composants viennent du tableau de bord Express et sont repris tels quels
/// par celui de Food. Les extraire ici plutôt que de les recopier n'est pas une
/// coquetterie : les deux écrans doivent RESTER identiques sur ces points. Une
/// carte d'indicateur qui divergerait d'un univers à l'autre se remarquerait
/// immédiatement chez un partenaire qui gère une boutique ET un restaurant — et
/// c'est exactement le cas de la maquette.
///
/// CE QUI DIFFÈRE ENTRE LES UNIVERS N'A RIEN À FAIRE ICI.
///
/// La carte « CUISINE », la file « À faire maintenant », le bloc « À TRAITER » :
/// chacun n'existe que d'un côté. Les généraliser produirait des composants à
/// paramètres optionnels dont la moitié serait toujours nulle.
/// ═════════════════════════════════════════════════════════════════════════════

/// En-tête contextuel : quelle activité, dans quel univers, comment en changer.
class PartnerContextHeader extends StatelessWidget {
  const PartnerContextHeader({
    super.key,
    required this.activity,
    required this.onSwitchActivity,
    this.trailing,
  });

  final SellerActivity activity;

  /// Bouton de droite. La maquette met une cloche côté Express et un bouton
  /// « Simuler » côté Food — d'où le remplacement complet plutôt qu'un drapeau.
  final Widget? trailing;

  /// Ouverture de la bascule d'activité.
  ///
  /// INJECTÉE PLUTÔT QU'APPELÉE DIRECTEMENT.
  ///
  /// `partner_widgets` vit dans `shared/` et ne doit pas connaître un écran de
  /// `features/`. Sans ce rappel, le composant partagé importerait la feuille,
  /// et le socle visuel dépendrait d'une fonctionnalité — l'inverse du sens
  /// voulu.
  final void Function(BuildContext) onSwitchActivity;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Row(
      children: [
        PartnerAvatar(
          initials: activity.initials,
          universe: activity.universe,
          size: 40,
        ),
        const SizedBox(width: 10),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              // LE CHEVRON OUVRE UNE FEUILLE, IL NE NAVIGUE PAS.
              //
              // Il menait vers `/select-activity`, l'aiguillage de connexion.
              // La maquette annonce « le changement d'activité est instantané » :
              // naviguer ferait perdre le contexte, imposerait un retour, et
              // transformerait un geste d'une seconde en aller-retour.
              InkWell(
                onTap: () => onSwitchActivity(context),
                borderRadius: BorderRadius.circular(8),
                child: Padding(
                  padding: const EdgeInsets.symmetric(vertical: 2),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        activity.name,
                        style: TextStyle(
                          fontSize: 17,
                          fontWeight: FontWeight.w800,
                          color: colors.ink,
                        ),
                      ),
                      const SizedBox(width: 4),
                      Icon(Icons.keyboard_arrow_down_rounded, size: 20, color: colors.subtle),
                    ],
                  ),
                ),
              ),
              Text(
                activity.universe.badge,
                style: TextStyle(
                  fontSize: 10.5,
                  fontWeight: FontWeight.w800,
                  letterSpacing: 0.6,
                  color: colors.subtle,
                ),
              ),
            ],
          ),
        ),
        if (trailing != null) trailing!,
      ],
    );
  }
}

class PartnerAvatar extends StatelessWidget {
  const PartnerAvatar({
    super.key,
    required this.initials,
    required this.universe,
    this.size = 40,
  });

  final String initials;
  final HbaUniverse universe;
  final double size;

  @override
  Widget build(BuildContext context) => Container(
        width: size,
        height: size,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: universe.soft,
          borderRadius: BorderRadius.circular(size * 0.28),
        ),
        child: Text(
          initials,
          style: TextStyle(
            fontSize: size * 0.32,
            fontWeight: FontWeight.w800,
            color: universe.accent,
          ),
        ),
      );
}

/// Intitulé de section : capitales espacées, action facultative à droite.
class PartnerSectionTitle extends StatelessWidget {
  const PartnerSectionTitle(this.label, {super.key, this.action, this.onTap});

  final String label;
  final String? action;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        Text(
          label.toUpperCase(),
          style: TextStyle(
            fontSize: 11,
            fontWeight: FontWeight.w800,
            letterSpacing: 0.9,
            color: colors.subtle,
          ),
        ),
        if (action != null)
          GestureDetector(
            onTap: onTap,
            child: Text(
              action!,
              style: const TextStyle(
                fontSize: 12.5,
                fontWeight: FontWeight.w700,
                color: AppTheme.brandGreen,
              ),
            ),
          ),
      ],
    );
  }
}

/// Carte blanche, filet clair, sans ombre — le contenant de tout l'écran.
class PartnerCard extends StatelessWidget {
  const PartnerCard({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(14),
    this.borderColor,
    this.background,
  });

  final Widget child;
  final EdgeInsets padding;
  final Color? borderColor;
  final Color? background;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    // ═════════════════════════════════════════════════════════════════════════
    // `Material` ET NON `Container`, ET CE N'EST PAS UN DÉTAIL DE STYLE.
    //
    // Un `ListTile` peint son fond et ses ondes d'encre sur le `Material` LE PLUS
    // PROCHE. Posé dans un `Container` coloré, ce dernier s'intercale et masque
    // les deux — Flutter lève alors, en debug :
    //
    //   ListTile background color or ink splashes may be invisible.
    //   The ListTile is wrapped in a DecoratedBox that has a background color.
    //
    // Quatre écrans mettent un `ListTile` dans une de ces cartes. Trois ne
    // s'en apercevaient pas faute d'`onTap` — donc faute d'encre à masquer. Le
    // quatrième en a ajouté un, et l'assertion est tombée.
    //
    // POURQUOI CORRIGER LE CONTENEUR PLUTÔT QUE L'APPEL.
    //
    // Envelopper le `ListTile` fautif dans un `Material` aurait réglé le cas et
    // laissé le piège en place pour le prochain `onTap`. Un `Material` peint le
    // fond ET fournit la surface d'encre en un seul widget : il n'y a plus de
    // couche à intercaler, donc plus rien à oublier.
    //
    // `clipBehavior: antiAlias` EST REQUIS. Sans lui l'onde d'encre déborde des
    // coins arrondis — visible sur un appui long, discret et laid.
    // ═════════════════════════════════════════════════════════════════════════
    return Material(
      color: background ?? colors.surface,
      clipBehavior: Clip.antiAlias,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        side: BorderSide(color: borderColor ?? colors.line),
      ),
      child: Padding(padding: padding, child: child),
    );
  }
}

/// Un indicateur de tableau de bord.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CHAMP `delta` A ÉTÉ RETIRÉ, ET IL NE FAUT PAS LE REMETTRE.
///
/// Il portait « +12 % vs hier ». AUCUN contrat ne rend de valeur de la veille :
/// `MerchantTodayDto` ne connaît que le JOUR COURANT, et il n'existe nulle part
/// de série temporelle côté HBA (module `analytics`, cf. `not_migrated.dart`).
/// Le pourcentage était écrit dans le fichier de maquette. Le champ disparaît
/// donc de la classe plutôt que de rester nullable : un champ facultatif finit
/// toujours par être rempli par quelqu'un qui n'a pas lu ce commentaire.
///
/// C'est la classe de VUE, pas un modèle de données : elle ne porte que des
/// chaînes déjà mises en forme par l'écran qui la construit.
/// ═════════════════════════════════════════════════════════════════════════════
class PartnerKpi {
  const PartnerKpi(this.label, this.value, {this.unit, this.hint, this.accent});

  final String label;
  final String value;

  /// « F CFA », affiché plus petit sous la valeur.
  final String? unit;

  /// Précision secondaire : « dont 2 en attente ».
  final String? hint;

  /// Couleur de la valeur. Nulle = couleur des titres.
  ///
  /// NE SERT QU'À SIGNALER UNE ACTION DUE, PAS À DÉCORER.
  ///
  /// « Commandes en cuisine : 3 » est en ambre parce que trois plats attendent.
  /// Colorer aussi le chiffre d'affaires ferait un écran où plus rien ne
  /// ressort — et c'est précisément ce que la couleur sert à faire ici.
  final Color? accent;
}

/// Grille d'indicateurs, deux par ligne.
class PartnerKpiGrid extends StatelessWidget {
  const PartnerKpiGrid({super.key, required this.kpis});

  final List<PartnerKpi> kpis;

  @override
  Widget build(BuildContext context) => Column(
        children: [
          for (var i = 0; i < kpis.length; i += 2) ...[
            // `IntrinsicHeight` EST OBLIGATOIRE ICI. NE PAS LE REMPLACER PAR
            //    `CrossAxisAlignment.stretch` SEUL.
            //
            // `stretch` demande aux enfants toute la hauteur de la `Row`. Dans
            // une `ListView`, cette hauteur n'est pas bornée : les cartes
            // reçoivent une contrainte infinie et le rendu casse sur
            // « 'child.hasSize': is not true ». `IntrinsicHeight` mesure d'abord
            // la hauteur naturelle, puis la fixe pour les deux.
            IntrinsicHeight(
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Expanded(child: PartnerKpiCard(kpi: kpis[i])),
                  const SizedBox(width: 12),
                  Expanded(
                    child: i + 1 < kpis.length
                        ? PartnerKpiCard(kpi: kpis[i + 1])
                        : const SizedBox.shrink(),
                  ),
                ],
              ),
            ),
            if (i + 2 < kpis.length) const SizedBox(height: 12),
          ],
        ],
      );
}

class PartnerKpiCard extends StatelessWidget {
  const PartnerKpiCard({super.key, required this.kpi});

  final PartnerKpi kpi;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return PartnerCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(kpi.label, style: TextStyle(fontSize: 12.5, color: colors.subtle)),
          const SizedBox(height: 8),
          Text(
            kpi.value,
            style: TextStyle(
              fontSize: 24,
              fontWeight: FontWeight.w800,
              // Une valeur accentuée signale une ACTION DUE — « commandes en
              // cuisine », par exemple. Le reste garde la couleur des titres.
              color: kpi.accent ?? colors.ink,
            ),
          ),
          const SizedBox(height: 3),
          Row(
            children: [
              if (kpi.unit != null)
                Text(kpi.unit!, style: TextStyle(fontSize: 11.5, color: colors.subtle)),
              if (kpi.unit != null && kpi.hint != null) const SizedBox(width: 6),
              if (kpi.hint != null)
                Expanded(
                  child: Text(
                    kpi.hint!,
                    style: TextStyle(fontSize: 11.5, color: colors.subtle),
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
            ],
          ),
        ],
      ),
    );
  }
}

/// ═════════════════════════════════════════════════════════════════════════════
/// `PartnerWeekChart` A ÉTÉ SUPPRIMÉ. IL N'AVAIT AUCUNE DONNÉE À DESSINER.
///
/// L'histogramme « Performance · 7 jours » prenait sept HAUTEURS RELATIVES
/// (`0.42, 0.50, 0.62…`) et un delta (« +18 % ») écrits à la main dans le
/// fichier de maquette. Aucune série temporelle n'existe côté HBA : elle était
/// calculée par le BFF vendeur du monolithe, et ni service ni route BFF ne la
/// rend aujourd'hui (module `analytics`, cf. `core/network/not_migrated.dart`).
///
/// Le composant est retiré plutôt que laissé vide : un histogramme qui accepte
/// des barres en paramètre finit toujours par recevoir des barres inventées. Le
/// jour où une agrégation existera, il faudra de toute façon le réécrire autour
/// de VRAIES valeurs — donc avec une échelle, ce que celui-ci n'avait pas.
/// ═════════════════════════════════════════════════════════════════════════════

/// En-tête des écrans de catalogue : un titre, puis l'activité en capitales.
///
/// CE N'EST PAS `PartnerContextHeader`, ET LA DIFFÉRENCE EST VOULUE.
///
/// L'en-tête contextuel des tableaux de bord met l'ACTIVITÉ au premier plan avec
/// son avatar et son chevron de bascule : on y arrive sans savoir ce qu'on
/// regarde. Ici, on a choisi l'onglet « Produits » ou « Menu » — le titre est
/// donc l'écran, et l'activité passe en sous-titre, comme sur la maquette.
class PartnerScreenHeader extends StatelessWidget {
  const PartnerScreenHeader({
    super.key,
    required this.title,
    required this.activity,
    this.trailing,
  });

  final String title;
  final SellerActivity activity;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Row(
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                title,
                style: TextStyle(
                  fontSize: 25,
                  fontWeight: FontWeight.w800,
                  color: colors.ink,
                ),
              ),
              const SizedBox(height: 3),
              Text(
                // Le nom en capitales, puis l'univers : le partenaire multi-
                // activités doit pouvoir vérifier d'un regard qu'il ne modifie
                // pas le catalogue de la mauvaise boutique.
                '${activity.name.toUpperCase()} · ${activity.universe.badge}',
                style: TextStyle(
                  fontSize: 11,
                  fontWeight: FontWeight.w800,
                  letterSpacing: 0.7,
                  color: activity.universe.accent,
                ),
              ),
            ],
          ),
        ),
        if (trailing != null) trailing!,
      ],
    );
  }
}

/// Champ de recherche de la maquette : bordure fine, loupe, pas d'ombre.
class PartnerSearchField extends StatelessWidget {
  const PartnerSearchField({
    super.key,
    required this.hint,
    required this.onChanged,
  });

  final String hint;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return SizedBox(
      height: AppTheme.fieldHeight,
      child: TextField(
        onChanged: onChanged,
        style: TextStyle(fontSize: 14.5, color: colors.ink),
        decoration: InputDecoration(
          hintText: hint,
          hintStyle: TextStyle(fontSize: 14.5, color: colors.subtle),
          prefixIcon: Icon(Icons.search, size: 20, color: colors.subtle),
          filled: true,
          fillColor: colors.surface,
          contentPadding: EdgeInsets.zero,
          enabledBorder: OutlineInputBorder(
            borderRadius: BorderRadius.circular(12),
            borderSide: BorderSide(color: colors.line),
          ),
          focusedBorder: OutlineInputBorder(
            borderRadius: BorderRadius.circular(12),
            borderSide: const BorderSide(color: AppTheme.brandGreen, width: 1.4),
          ),
        ),
      ),
    );
  }
}

/// Puce de filtre : pleine et sombre quand active, contour fin sinon.
class PartnerFilterChip extends StatelessWidget {
  const PartnerFilterChip({
    super.key,
    required this.label,
    required this.selected,
    required this.onTap,
  });

  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(20),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 15, vertical: 9),
        decoration: BoxDecoration(
          color: selected ? AppTheme.charcoal : colors.surface,
          borderRadius: BorderRadius.circular(20),
          border: Border.all(color: selected ? AppTheme.charcoal : colors.line),
        ),
        child: Text(
          label,
          style: TextStyle(
            fontSize: 13,
            fontWeight: FontWeight.w700,
            color: selected ? Colors.white : colors.subtle,
          ),
        ),
      ),
    );
  }
}

/// Pastille d'état : un point coloré et un mot.
class PartnerStatusDot extends StatelessWidget {
  const PartnerStatusDot({
    super.key,
    required this.label,
    required this.color,
    required this.background,
  });

  final String label;
  final Color color;
  final Color background;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
        decoration: BoxDecoration(
          color: background,
          borderRadius: BorderRadius.circular(7),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 6,
              height: 6,
              decoration: BoxDecoration(color: color, shape: BoxShape.circle),
            ),
            const SizedBox(width: 5),
            Text(
              label,
              style: TextStyle(
                fontSize: 11.5,
                fontWeight: FontWeight.w700,
                color: color,
              ),
            ),
          ],
        ),
      );
}

/// État vide d'une liste filtrée.
class PartnerEmptyState extends StatelessWidget {
  const PartnerEmptyState({super.key, required this.icon, required this.message});

  final IconData icon;
  final String message;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 56),
      child: Column(
        children: [
          Icon(icon, size: 38, color: colors.line),
          const SizedBox(height: 12),
          Text(
            message,
            textAlign: TextAlign.center,
            style: TextStyle(fontSize: 13.5, color: colors.subtle),
          ),
        ],
      ),
    );
  }
}


/// ═════════════════════════════════════════════════════════════════════════════
/// LA PASTILLE DE STATUT DE COMMANDE A QUITTÉ CE FICHIER.
///
/// Elle prenait `MockOrderStatus` — une énumération de la maquette (« Nouvelle »,
/// « À préparer », « Prête »…) dont AUCUNE valeur n'existe dans `OrderStatus`
/// côté order-service. La table de couleurs suit maintenant les statuts réels et
/// vit avec eux : `features/orders/presentation/order_status_pill.dart`.
///
/// La raison de la garder unique n'a pas changé : une commande verte ici et
/// ambre ailleurs, c'est un partenaire qui apprend deux fois le même code
/// couleur, puis cesse de s'y fier.
/// ═════════════════════════════════════════════════════════════════════════════
