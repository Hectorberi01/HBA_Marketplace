import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/partner_widgets.dart';
import '../activities_data.dart';
import '../selected_activity.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// BASCULE D'ACTIVITÉ — feuille ouverte par le chevron de l'en-tête.
///
/// UNE FEUILLE, PAS UN ÉCRAN. LA DIFFÉRENCE EST DANS LA MAQUETTE.
///
/// « Le changement d'activité est instantané » : le partenaire touche, la
/// feuille se ferme, le tableau de bord derrière a déjà changé. Naviguer vers un
/// écran ferait perdre le contexte, imposerait un retour, et transformerait un
/// geste d'une seconde en un aller-retour.
///
/// C'est aussi pourquoi elle ne remplace pas `ActivitySelectionScreen` : celui-ci
/// est l'aiguillage de connexion, affiché une fois, sans contexte à préserver.
/// ═════════════════════════════════════════════════════════════════════════════
class ActivitySwitcherSheet extends ConsumerWidget {
  const ActivitySwitcherSheet({super.key});

  /// Ouvre la feuille. Point d'entrée unique — l'en-tête n'a pas à connaître
  /// `showModalBottomSheet` ni ses réglages.
  static Future<void> show(BuildContext context) => showModalBottomSheet<void>(
        context: context,
        backgroundColor: Colors.transparent,
        // Sans cela, une liste de six activités serait tronquée à la moitié de
        // l'écran sans possibilité de défiler.
        isScrollControlled: true,
        builder: (_) => const ActivitySwitcherSheet(),
      );

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final current = ref.watch(selectedActivityProvider);

    // La liste vient de `GET /api/v1/bff/merchant/activities` : c'est un appel
    // réseau, donc la feuille a trois états comme n'importe quel écran. Ouvrir
    // sur une liste vide « en attendant » ferait croire à un compte sans
    // activité — et le partenaire refermerait avant la réponse.
    final async = ref.watch(activitiesProvider);

    return Container(
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: const BorderRadius.vertical(top: Radius.circular(22)),
      ),
      child: SafeArea(
        top: false,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const SizedBox(height: 10),
            Center(
              child: Container(
                width: 38,
                height: 4,
                decoration: BoxDecoration(
                  color: colors.line,
                  borderRadius: BorderRadius.circular(2),
                ),
              ),
            ),
            const SizedBox(height: 18),

            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 18),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'Mes activités',
                    style: TextStyle(
                      fontSize: 20,
                      fontWeight: FontWeight.w800,
                      color: colors.ink,
                    ),
                  ),
                  const SizedBox(height: 3),
                  Text(
                    'Le changement d\'activité est instantané.',
                    style: TextStyle(fontSize: 13, color: colors.subtle),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 14),

            Flexible(
              child: ListView(
                shrinkWrap: true,
                padding: const EdgeInsets.symmetric(horizontal: 14),
                children: [
                  // CETTE LIGNE N'EST PAS SUR LA MAQUETTE, ET JE L'AJOUTE
                  //    QUAND MÊME.
                  //
                  // La feuille n'y montre que les activités. Sans retour vers la
                  // vue consolidée, un partenaire qui choisit une boutique ne
                  // peut plus JAMAIS revoir son chiffre d'affaires total : le
                  // seul chemin connu est le bouton « Toutes mes activités » de
                  // l'aiguillage de connexion, qui ne s'affiche qu'une fois.
                  //
                  // À confirmer avec le design — soit la ligne reste ici, soit
                  // un autre chemin existe que la maquette ne montre pas.
                  Padding(
                    padding: const EdgeInsets.only(bottom: 10),
                    child: _AllActivitiesRow(
                      isActive: current == null,
                      onTap: () {
                        ref.read(selectedActivityIdProvider.notifier).choisir(null);
                        Navigator.of(context).pop();
                      },
                    ),
                  ),
                  ...async.when<List<Widget>>(
                    loading: () => const [
                      Padding(
                        padding: EdgeInsets.symmetric(vertical: 28),
                        child: Center(child: CircularProgressIndicator()),
                      ),
                    ],

                    // 403 (rôle `Seller` absent) ou service injoignable. La
                    // ligne « Toutes mes activités » reste au-dessus : le
                    // partenaire n'est jamais enfermé dans la feuille.
                    error: (e, _) => [_SheetNotice(message: e.toString())],

                    data: (result) => result.data.isEmpty
                        ? const [
                            _SheetNotice(
                              message: 'Aucune activité n\'est rattachée à ce compte.',
                            ),
                          ]
                        : [
                            // Rendu partiel du BFF : on le dit, sinon une liste
                            // amputée passe pour complète et le partenaire
                            // conclut qu'un restaurant a été supprimé.
                            for (final warning in result.warnings)
                              _SheetNotice(message: warning.message),

                            for (final activity in result.data)
                              Padding(
                                padding: const EdgeInsets.only(bottom: 10),
                                child: _ActivityRow(
                                  activity: activity,
                                  isActive: activity.id == current?.id,
                                  onTap: () {
                                    // ON ÉCRIT L'ÉTAT AVANT DE FERMER.
                                    //
                                    // `Navigator.pop` détruit le contexte de la
                                    // feuille. Écrire ensuite reviendrait à
                                    // utiliser un `ref` dont le widget n'existe
                                    // plus.
                                    ref.read(selectedActivityIdProvider.notifier).choisir(activity.id);
                                    Navigator.of(context).pop();
                                  },
                                ),
                              ),
                          ],
                  ),
                ],
              ),
            ),

            Divider(height: 1, color: colors.line),

            // CE BOUTON A ÉTÉ GRISÉ SUR UN MOTIF FAUX, ET LE VOICI RÉTABLI.
            //
            // Le commentaire précédent disait : « le geste a un amont côté boutique
            // […] mais AUCUN écran de création n'existe hors du parcours
            // d'inscription — et côté restaurant, la base verrouille une
            // appartenance unique par compte ».
            //
            // Les deux faits étaient exacts. La conclusion ne l'était pas : ce qui
            // manquait était l'ÉCRAN, pas la route — `POST /api/merchants/{id}/stores`
            // et `POST /api/food/partner/restaurants` existent, gardées et testées.
            // Quant à l'unicité du restaurant, elle n'interdit pas d'ajouter une
            // boutique ; elle interdit un second établissement, ce que
            // `ActivityWizardScreen` dit dès son étape 1, carte par carte.
            //
            // Un bouton grisé pour une raison mal établie coûte plus qu'un bouton
            // absent : il fait croire la fonction impossible, et personne ne
            // revérifie.
            InkWell(
              onTap: () {
                Navigator.of(context).pop();
                context.push('/activity/new');
              },
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 16),
                child: Row(
                  children: [
                    Container(
                      width: 28,
                      height: 28,
                      alignment: Alignment.center,
                      decoration: BoxDecoration(
                        color: AppTheme.brandGreenSoft,
                        borderRadius: BorderRadius.circular(9),
                      ),
                      child: const Icon(Icons.add_rounded, size: 18, color: AppTheme.brandGreen),
                    ),
                    const SizedBox(width: 12),
                    const Expanded(
                      child: Text(
                        'Ajouter une activité',
                        style: TextStyle(fontSize: 14.5, fontWeight: FontWeight.w700),
                      ),
                    ),
                    Icon(Icons.chevron_right, size: 20, color: colors.subtle),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _ActivityRow extends StatelessWidget {
  const _ActivityRow({
    required this.activity,
    required this.isActive,
    required this.onTap,
  });

  final SellerActivity activity;
  final bool isActive;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(14),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 12),
        decoration: BoxDecoration(
          // LA LIGNE ACTIVE PREND LA TEINTE DE SON UNIVERS, PAS UNE TEINTE
          //    DE SÉLECTION UNIQUE.
          //
          // Le partenaire voit d'un coup d'œil s'il est côté boutique ou côté
          // restaurant — c'est la même information que la couleur du tableau de
          // bord derrière. Un gris de sélection dirait « celui-ci » sans dire
          // « lequel des deux mondes ».
          color: isActive ? activity.universe.soft : colors.surface,
          borderRadius: BorderRadius.circular(14),
          border: Border.all(
            color: isActive
                ? activity.universe.accent.withValues(alpha: 0.35)
                : colors.line,
          ),
        ),
        child: Row(
          children: [
            PartnerAvatar(
              initials: activity.initials,
              universe: activity.universe,
              size: 40,
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    activity.name,
                    style: TextStyle(
                      fontSize: 15,
                      fontWeight: FontWeight.w800,
                      color: colors.ink,
                    ),
                  ),
                  Text(
                    '${activity.kind} · ${activity.universe.label}',
                    style: TextStyle(fontSize: 12.5, color: colors.subtle),
                  ),
                ],
              ),
            ),
            if (isActive)
              Text(
                'ACTIF',
                style: TextStyle(
                  fontSize: 10.5,
                  fontWeight: FontWeight.w800,
                  letterSpacing: 0.8,
                  color: activity.universe.accent,
                ),
              ),
          ],
        ),
      ),
    );
  }
}


/// Message d'état dans la feuille : erreur, liste vide, rendu partiel.
///
/// Volontairement sobre et SANS bouton « Réessayer » : la feuille se referme et
/// se rouvre d'un geste, ce qui relance l'appel. Un bouton de plus dans un
/// panneau de 300 px prendrait la place des activités.
class _SheetNotice extends StatelessWidget {
  const _SheetNotice({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Padding(
      padding: const EdgeInsets.fromLTRB(4, 8, 4, 16),
      child: Text(
        message,
        textAlign: TextAlign.center,
        style: TextStyle(fontSize: 13, height: 1.4, color: colors.subtle),
      ),
    );
  }
}

/// Retour à la vue consolidée.
class _AllActivitiesRow extends StatelessWidget {
  const _AllActivitiesRow({required this.isActive, required this.onTap});

  final bool isActive;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(14),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 12),
        decoration: BoxDecoration(
          color: isActive ? AppTheme.brandGreenSoft : colors.surface,
          borderRadius: BorderRadius.circular(14),
          border: Border.all(
            color: isActive
                ? AppTheme.brandGreen.withValues(alpha: 0.35)
                : colors.line,
          ),
        ),
        child: Row(
          children: [
            Container(
              width: 40,
              height: 40,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: colors.bg,
                borderRadius: BorderRadius.circular(11),
              ),
              child: Icon(Icons.dashboard_outlined, size: 19, color: colors.subtle),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'Toutes mes activités',
                    style: TextStyle(
                      fontSize: 15,
                      fontWeight: FontWeight.w800,
                      color: colors.ink,
                    ),
                  ),
                  Text(
                    'Vue consolidée',
                    style: TextStyle(fontSize: 12.5, color: colors.subtle),
                  ),
                ],
              ),
            ),
            if (isActive)
              const Text(
                'ACTIF',
                style: TextStyle(
                  fontSize: 10.5,
                  fontWeight: FontWeight.w800,
                  letterSpacing: 0.8,
                  color: AppTheme.brandGreen,
                ),
              ),
          ],
        ),
      ),
    );
  }
}
