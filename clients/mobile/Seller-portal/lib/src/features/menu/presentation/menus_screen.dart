import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/partner_widgets.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../menu_data.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// CARTES ET SECTIONS — L'ÉCRAN QUI MANQUAIT (tâche #214).
///
/// LES ONZE ROUTES EXISTAIENT, ET AUCUNE N'AVAIT D'APPELANT.
///
/// `renameMenu`, `setMenuVisible`, `deleteMenu`, `renameSection`,
/// `setSectionVisible`, `moveSection`, `deleteSection`, `createMenu`,
/// `createSection` : toutes écrites dans `MenuApi` depuis VEN5-b, toutes
/// joignables. Le restaurateur pouvait créer des plats — `partner_menu_screen`
/// les affiche — mais pas organiser ses cartes. Cinquième cas de la session où la
/// couche serveur attendait un appel qui n'est jamais venu.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI UN ÉCRAN SÉPARÉ DE `partner_menu_screen`.
///
/// Celui-là répond à « qu'est-ce que je vends aujourd'hui » : la liste des plats,
/// leurs prix, leur disponibilité — ce qu'on consulte plusieurs fois par jour.
/// Celui-ci répond à « comment ma carte est-elle bâtie » : deux niveaux, des
/// créneaux, un ordre. C'est un geste rare et structurant.
///
/// Les fondre donnerait un écran où le restaurateur en service cherche un plat
/// parmi des boutons de suppression de carte. Les gestes destructeurs ne
/// s'installent pas sur le chemin quotidien.
///
/// LE MODÈLE A DEUX NIVEAUX, ET LE VOCABULAIRE COMPTE.
///
/// Une CARTE (`Menu`) porte un créneau — « Petit-déjeuner, 6 h – 11 h ». Elle
/// contient des SECTIONS (`MenuSection`) — « Boissons », « Grillades » — qui
/// contiennent les plats. Confondre les deux dans l'interface ferait créer des
/// sections là où il faut des cartes, et le restaurateur ne comprendrait pas
/// pourquoi ses plats n'apparaissent qu'à midi.
///
/// UNE CARTE MASQUÉE N'EST PAS UNE CARTE HORS CRÉNEAU.
///
/// `isActive` est une décision du restaurateur ; `isServedNow` est le calcul du
/// serveur sur l'heure courante. Une carte active mais hors créneau est normale à
/// 15 h ; une carte masquée ne sortira jamais. L'écran distingue les deux, parce
/// qu'un restaurateur qui ne voit pas ses plats cherche d'abord la mauvaise cause.
/// ═════════════════════════════════════════════════════════════════════════════
class MenusScreen extends ConsumerStatefulWidget {
  const MenusScreen({super.key, required this.restaurantId});

  final String restaurantId;

  @override
  ConsumerState<MenusScreen> createState() => _MenusScreenState();
}

class _MenusScreenState extends ConsumerState<MenusScreen> {
  /// L'identifiant de l'élément dont une action est en cours.
  ///
  /// UN SEUL À LA FOIS, ET C'EST VOULU. Déplacer une section pendant qu'une
  /// autre se supprime laisserait le serveur arbitrer deux ordres concurrents.
  String? _enCours;

  Future<void> _agir(String id, Future<void> Function() action, String succes) async {
    setState(() => _enCours = id);
    try {
      await action();
      ref.invalidate(restaurantMenuProvider(widget.restaurantId));
      if (mounted) AppNotify.success(context, succes);
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _enCours = null);
    }
  }

  MenuApi get _api => ref.read(menuApiProvider);

  // ─────────────────────────────────────────────────────────────────── saisies

  /// Demande un nom. Rend `null` si le restaurateur annule.
  ///
  /// UNE SEULE BOÎTE POUR CRÉER ET RENOMMER : les deux gestes demandent la même
  /// chose. En écrire deux ferait diverger la validation, et c'est toujours celle
  /// du renommage qu'on oublie.
  ///
  /// ═══════════════════════════════════════════════════════════════════════════
  /// LE CONTRÔLEUR APPARTIENT À LA BOÎTE, PAS À CETTE MÉTHODE. C'ÉTAIT UN BOGUE.
  ///
  /// La première version créait un `TextEditingController` ici et l'appelait
  /// `dispose()` juste après l'`await` :
  ///
  ///     final valeur = await showDialog<String>(…);
  ///     champ.dispose();
  ///
  /// Ce qui lève « A TextEditingController was used after being disposed ».
  ///
  /// La cause est contre-intuitive : `showDialog` rend la main dès que
  /// `Navigator.pop` est appelé, mais l'ANIMATION DE FERMETURE continue. Flutter
  /// reconstruit donc le `TextField` pendant la sortie — sur un contrôleur qu'on
  /// vient de libérer. L'exception arrive une image plus tard, dans
  /// `RawGestureDetector`, et pointe vers un endroit qui n'a rien fait de mal.
  ///
  /// Attendre un délai avant de libérer serait un pari sur la durée de
  /// l'animation. Confier le contrôleur à un `StatefulWidget` fait coïncider sa
  /// vie avec celle du widget qui l'emploie : Flutter appelle `dispose()` quand
  /// l'arbre est réellement démonté, et personne n'a à devenir la durée.
  /// ═══════════════════════════════════════════════════════════════════════════
  Future<String?> _demanderNom({required String titre, String? initial}) =>
      showDialog<String>(
        context: context,
        builder: (_) => _NomDialog(titre: titre, initial: initial),
      );

  /// LA SUPPRESSION SE CONFIRME EN NOMMANT CE QU'ELLE EMPORTE.
  ///
  /// Supprimer une carte emporte ses sections, donc ses plats. Un « Êtes-vous
  /// sûr ? » ne dit pas cela ; le restaurateur croit retirer un titre.
  Future<bool> _confirmerSuppression({required String quoi, required String consequence}) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (d) => AlertDialog(
        title: Text('Supprimer $quoi ?'),
        content: Text(consequence),
        actions: [
          TextButton(onPressed: () => Navigator.pop(d, false), child: const Text('Annuler')),
          FilledButton(
            style: FilledButton.styleFrom(backgroundColor: AppTheme.danger),
            onPressed: () => Navigator.pop(d, true),
            child: const Text('Supprimer'),
          ),
        ],
      ),
    );
    return ok ?? false;
  }

  // ─────────────────────────────────────────────────────────────────── affichage

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final async = ref.watch(restaurantMenuProvider(widget.restaurantId));

    return Scaffold(
      backgroundColor: colors.bg,
      appBar: AppBar(title: const Text('Mes cartes')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () async {
          final nom = await _demanderNom(titre: 'Nouvelle carte');
          if (nom == null) return;
          await _agir('nouvelle', () => _api.createMenu(widget.restaurantId, name: nom),
              'Carte « $nom » créée.');
        },
        icon: const Icon(Icons.add),
        label: const Text('Nouvelle carte'),
      ),
      body: async.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(
          message: e.toString(),
          onRetry: () => ref.invalidate(restaurantMenuProvider(widget.restaurantId)),
        ),
        data: (carte) => RefreshIndicator(
          onRefresh: () async => ref.invalidate(restaurantMenuProvider(widget.restaurantId)),
          child: carte.menus.isEmpty
              ? ListView(
                  children: const [
                    SizedBox(height: 80),
                    PartnerEmptyState(
                      icon: Icons.menu_book_outlined,
                      message: 'Aucune carte. Une carte regroupe vos sections — '
                          '« Boissons », « Grillades » — et peut n\'être servie qu\'à '
                          'certaines heures.',
                    ),
                  ],
                )
              : ListView(
                  padding: const EdgeInsets.fromLTRB(16, 16, 16, 96),
                  children: [
                    for (final m in carte.menus) ...[
                      _CarteTile(
                        menu: m,
                        enCours: _enCours,
                        colors: colors,
                        onRenommer: () async {
                          final nom = await _demanderNom(
                              titre: 'Renommer la carte', initial: m.name);
                          if (nom == null) return;
                          await _agir(
                              m.id,
                              () => _api.renameMenu(widget.restaurantId,
                                  menuId: m.id, name: nom),
                              'Carte renommée.');
                        },
                        onVisibilite: () => _agir(
                          m.id,
                          () => _api.setMenuVisible(widget.restaurantId,
                              menuId: m.id, active: !m.isActive),
                          m.isActive ? 'Carte masquée.' : 'Carte affichée.',
                        ),
                        onSupprimer: () async {
                          final n = m.sections.length;
                          if (!await _confirmerSuppression(
                            quoi: 'la carte « ${m.name} »',
                            consequence: n == 0
                                ? 'Cette carte est vide. La suppression est définitive.'
                                : 'Elle contient $n section(s) et tous leurs plats. '
                                    'La suppression est définitive.',
                          )) {
                            return;
                          }
                          await _agir(
                              m.id,
                              () => _api.deleteMenu(widget.restaurantId, menuId: m.id),
                              'Carte supprimée.');
                        },
                        onAjouterSection: () async {
                          final nom = await _demanderNom(titre: 'Nouvelle section');
                          if (nom == null) return;
                          await _agir(
                              m.id,
                              () => _api.createSection(widget.restaurantId,
                                  menuId: m.id, name: nom),
                              'Section « $nom » créée.');
                        },
                        onRenommerSection: (s) async {
                          final nom = await _demanderNom(
                              titre: 'Renommer la section', initial: s.name);
                          if (nom == null) return;
                          await _agir(
                              s.id,
                              () => _api.renameSection(widget.restaurantId,
                                  sectionId: s.id, name: nom),
                              'Section renommée.');
                        },
                        onVisibiliteSection: (s) => _agir(
                          s.id,
                          () => _api.setSectionVisible(widget.restaurantId,
                              sectionId: s.id, active: !s.isActive),
                          s.isActive ? 'Section masquée.' : 'Section affichée.',
                        ),
                        onDeplacerSection: (s, position) => _agir(
                          s.id,
                          // `displayOrder`, PAS `position` : le serveur reçoit
                          // un ORDRE D'AFFICHAGE, qu'il applique tel quel. Le nom
                          // compte, parce que rien n'empêche deux sections de
                          // partager le même — c'est alors l'ordre de lecture en
                          // base qui tranche, donc l'arbitraire.
                          () => _api.moveSection(widget.restaurantId,
                              sectionId: s.id, displayOrder: position),
                          'Ordre mis à jour.',
                        ),
                        onSupprimerSection: (s) async {
                          final n = s.items.length;
                          if (!await _confirmerSuppression(
                            quoi: 'la section « ${s.name} »',
                            consequence: n == 0
                                ? 'Cette section est vide. La suppression est définitive.'
                                : 'Elle contient $n plat(s). La suppression est définitive.',
                          )) {
                            return;
                          }
                          await _agir(
                              s.id,
                              () => _api.deleteSection(widget.restaurantId, sectionId: s.id),
                              'Section supprimée.');
                        },
                      ),
                      const SizedBox(height: 12),
                    ],
                  ],
                ),
        ),
      ),
    );
  }
}

/// Une carte et ses sections.
class _CarteTile extends StatelessWidget {
  const _CarteTile({
    required this.menu,
    required this.enCours,
    required this.colors,
    required this.onRenommer,
    required this.onVisibilite,
    required this.onSupprimer,
    required this.onAjouterSection,
    required this.onRenommerSection,
    required this.onVisibiliteSection,
    required this.onDeplacerSection,
    required this.onSupprimerSection,
  });

  final Menu menu;
  final String? enCours;
  final AppColors colors;
  final VoidCallback onRenommer;
  final VoidCallback onVisibilite;
  final VoidCallback onSupprimer;
  final VoidCallback onAjouterSection;
  final void Function(MenuSection) onRenommerSection;
  final void Function(MenuSection) onVisibiliteSection;
  final void Function(MenuSection, int) onDeplacerSection;
  final void Function(MenuSection) onSupprimerSection;

  @override
  Widget build(BuildContext context) {
    final occupe = enCours == menu.id;

    return CardSection(
      margin: EdgeInsets.zero,
      padding: const EdgeInsets.all(14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(menu.name,
                        style: TextStyle(
                            fontWeight: FontWeight.w800, fontSize: 15.5, color: colors.ink)),
                    const SizedBox(height: 3),
                    // DEUX ÉTATS DISTINCTS, JAMAIS FONDUS.
                    //
                    // « Masquée » est la décision du restaurateur ; « hors
                    // créneau » est le calcul du serveur sur l'heure. Écrire
                    // « indisponible » pour les deux enverrait chercher la
                    // mauvaise cause — et une carte de petit-déjeuner est
                    // normalement hors créneau à 15 h.
                    Text(
                      !menu.isActive
                          ? 'Masquée — elle ne sortira jamais'
                          : menu.servedFrom == null
                              ? 'Servie en permanence'
                              : menu.isServedNow
                                  ? 'Servie maintenant · ${menu.servedFrom} – ${menu.servedUntil}'
                                  : 'Hors créneau · ${menu.servedFrom} – ${menu.servedUntil}',
                      style: TextStyle(
                        fontSize: 12,
                        color: !menu.isActive
                            ? AppTheme.promoOrange
                            : menu.isServedNow
                                ? AppTheme.brandGreen
                                : colors.subtle,
                      ),
                    ),
                  ],
                ),
              ),
              if (occupe)
                const SizedBox(
                    width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2))
              else
                PopupMenuButton<String>(
                  position: PopupMenuPosition.under,
                  onSelected: (v) => switch (v) {
                    'renommer' => onRenommer(),
                    'visibilite' => onVisibilite(),
                    'section' => onAjouterSection(),
                    _ => onSupprimer(),
                  },
                  itemBuilder: (_) => [
                    appMenuItem(
                        value: 'section', icon: Icons.playlist_add, label: 'Ajouter une section'),
                    appMenuItem(value: 'renommer', icon: Icons.edit_outlined, label: 'Renommer'),
                    appMenuItem(
                        value: 'visibilite',
                        icon: menu.isActive ? Icons.visibility_off_outlined : Icons.visibility_outlined,
                        label: menu.isActive ? 'Masquer' : 'Afficher'),
                    appMenuItem(
                        value: 'supprimer',
                        icon: Icons.delete_outline,
                        label: 'Supprimer',
                        danger: true),
                  ],
                ),
            ],
          ),

          if (menu.sections.isEmpty) ...[
            const SizedBox(height: 10),
            Text(
              'Aucune section. Les plats se rangent dans une section, pas '
              'directement dans la carte.',
              style: TextStyle(fontSize: 12, color: colors.subtle, height: 1.4),
            ),
          ] else
            for (var i = 0; i < menu.sections.length; i++) ...[
              Divider(height: 20, color: colors.line),
              _SectionRow(
                section: menu.sections[i],
                // LA POSITION EST L'INDEX CIBLE, et les bornes sont ici : le
                // serveur refuserait une position hors liste, et l'aller-retour
                // pour l'apprendre serait inutile.
                peutMonter: i > 0,
                peutDescendre: i < menu.sections.length - 1,
                occupe: enCours == menu.sections[i].id,
                colors: colors,
                onRenommer: () => onRenommerSection(menu.sections[i]),
                onVisibilite: () => onVisibiliteSection(menu.sections[i]),
                onMonter: () => onDeplacerSection(menu.sections[i], i - 1),
                onDescendre: () => onDeplacerSection(menu.sections[i], i + 1),
                onSupprimer: () => onSupprimerSection(menu.sections[i]),
              ),
            ],
        ],
      ),
    );
  }
}

class _SectionRow extends StatelessWidget {
  const _SectionRow({
    required this.section,
    required this.peutMonter,
    required this.peutDescendre,
    required this.occupe,
    required this.colors,
    required this.onRenommer,
    required this.onVisibilite,
    required this.onMonter,
    required this.onDescendre,
    required this.onSupprimer,
  });

  final MenuSection section;
  final bool peutMonter;
  final bool peutDescendre;
  final bool occupe;
  final AppColors colors;
  final VoidCallback onRenommer;
  final VoidCallback onVisibilite;
  final VoidCallback onMonter;
  final VoidCallback onDescendre;
  final VoidCallback onSupprimer;

  @override
  Widget build(BuildContext context) => Row(
        children: [
          // DEUX FLÈCHES PLUTÔT QU'UN GLISSER-DÉPOSER.
          //
          // `moveSection` prend une POSITION, pas un déplacement relatif : un
          // réordonnancement par glissement enverrait une écriture par
          // franchissement, sur un réseau où chacune peut échouer — et l'ordre
          // affiché divergerait de l'ordre enregistré. Deux flèches, une écriture,
          // un résultat vérifiable.
          Column(
            children: [
              InkWell(
                onTap: peutMonter && !occupe ? onMonter : null,
                child: Icon(Icons.keyboard_arrow_up,
                    size: 20, color: peutMonter ? colors.subtle : colors.line),
              ),
              InkWell(
                onTap: peutDescendre && !occupe ? onDescendre : null,
                child: Icon(Icons.keyboard_arrow_down,
                    size: 20, color: peutDescendre ? colors.subtle : colors.line),
              ),
            ],
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(section.name,
                    style: TextStyle(
                        fontWeight: FontWeight.w600,
                        fontSize: 14,
                        color: section.isActive ? colors.ink : colors.subtle)),
                Text(
                  section.isActive
                      ? '${section.items.length} plat(s)'
                      : '${section.items.length} plat(s) · masquée',
                  style: TextStyle(
                      fontSize: 11.5,
                      color: section.isActive ? colors.subtle : AppTheme.promoOrange),
                ),
              ],
            ),
          ),
          if (occupe)
            const SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2))
          else
            PopupMenuButton<String>(
              position: PopupMenuPosition.under,
              onSelected: (v) => switch (v) {
                'renommer' => onRenommer(),
                'visibilite' => onVisibilite(),
                _ => onSupprimer(),
              },
              itemBuilder: (_) => [
                appMenuItem(value: 'renommer', icon: Icons.edit_outlined, label: 'Renommer'),
                appMenuItem(
                    value: 'visibilite',
                    icon: section.isActive
                        ? Icons.visibility_off_outlined
                        : Icons.visibility_outlined,
                    label: section.isActive ? 'Masquer' : 'Afficher'),
                appMenuItem(
                    value: 'supprimer',
                    icon: Icons.delete_outline,
                    label: 'Supprimer',
                    danger: true),
              ],
            ),
        ],
      );
}


/// Boîte de saisie d'un nom, propriétaire de son contrôleur.
///
/// `StatefulWidget` UNIQUEMENT POUR LE CYCLE DE VIE, pas pour l'état visuel.
///
/// Rien ici ne change d'apparence ; l'unique raison de cette classe est que
/// `dispose()` soit appelé par Flutter au démontage réel, et non par l'appelant
/// dès le retour de `showDialog` — alors que l'animation de fermeture reconstruit
/// encore le champ. Voir l'encadré de `_demanderNom`.
class _NomDialog extends StatefulWidget {
  const _NomDialog({required this.titre, this.initial});

  final String titre;
  final String? initial;

  @override
  State<_NomDialog> createState() => _NomDialogState();
}

class _NomDialogState extends State<_NomDialog> {
  late final TextEditingController _champ =
      TextEditingController(text: widget.initial);

  @override
  void dispose() {
    _champ.dispose();
    super.dispose();
  }

  void _valider() {
    final v = _champ.text.trim();
    // VALIDÉ ICI, PAS AU RETOUR : rendre une chaîne vide obligerait chaque
    // appelant à la retester, et l'un d'eux l'oublierait.
    if (v.isNotEmpty) Navigator.pop(context, v);
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
        title: Text(widget.titre),
        content: TextField(
          controller: _champ,
          autofocus: true,
          textCapitalization: TextCapitalization.sentences,
          // VALIDE AU CLAVIER AUSSI. Sur un téléphone, la touche « entrée »
          // est le geste naturel après avoir tapé un nom ; obliger à viser un
          // bouton après avoir refermé le clavier est une frustration gratuite.
          textInputAction: TextInputAction.done,
          onSubmitted: (_) => _valider(),
          decoration: const InputDecoration(labelText: 'Nom'),
        ),
        actions: [
          TextButton(
              onPressed: () => Navigator.pop(context), child: const Text('Annuler')),
          FilledButton(onPressed: _valider, child: const Text('Valider')),
        ],
      );
}
