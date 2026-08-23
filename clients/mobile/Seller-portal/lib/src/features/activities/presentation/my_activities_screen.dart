import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/async_views.dart';
import '../activities_data.dart';
import '../selected_activity.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// MES ACTIVITÉS — onglet « Activités » de la barre du bas.
///
/// À NE PAS CONFONDRE AVEC « SÉLECTION DE L'ACTIVITÉ ».
///
/// Les deux écrans listent les mêmes activités et se ressemblent, mais ne
/// servent pas la même chose :
///
///   • `ActivitySelectionScreen` est un AIGUILLAGE affiché une fois, après
///     connexion, sans barre du bas ;
///   • cet écran-ci est un onglet PERMANENT. C'est aussi d'ici qu'on va régler
///     les paramètres d'une boutique qui ne tourne pas encore.
///
/// LA DISTINCTION « AVEC MOUVEMENT AUJOURD'HUI » A DISPARU DES DEUX.
///
/// L'aiguillage ne montrait que les activités ayant des commandes du jour.
/// `MerchantActivityDto` ne porte AUCUN compteur — ni commandes, ni chiffre
/// d'affaires, ni file d'attente — donc ce tri n'est plus calculable. Les deux
/// écrans montrent tout, et c'est le tableau de bord d'une activité qui donne
/// ses chiffres.
/// ═════════════════════════════════════════════════════════════════════════════
class MyActivitiesScreen extends ConsumerWidget {
  const MyActivitiesScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final async = ref.watch(activitiesProvider);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        bottom: false,
        child: RefreshIndicator(
          onRefresh: () async => ref.invalidate(activitiesProvider),
          child: async.when(
            loading: () => const LoadingView(),

            // 403 N'EST PAS UNE PANNE : la route exige le rôle `Seller`. Un
            // compte uniquement restaurateur, ou dont le jeton n'a pas encore
            // été repris après création de boutique, tombe ici.
            error: (e, _) => ErrorView(
              message: e.toString(),
              onRetry: () => ref.invalidate(activitiesProvider),
            ),
            data: (result) {
              final activities = result.data;

              if (activities.isEmpty) {
                return const EmptyView(
                  message: 'Aucune activité n\'est rattachée à ce compte.\n'
                      'Créez votre boutique pour commencer à vendre.',
                  icon: Icons.storefront_outlined,
                );
              }

              return ListView(
                padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
                children: [
                  Text(
                    'Mes activités',
                    style:
                        TextStyle(fontSize: 26, fontWeight: FontWeight.w800, color: colors.ink),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    // L'accord se fait ici : « 1 activité » / « 3 activités ».
                    // La mention « · 1 compte » de la maquette est retirée —
                    // elle était vraie par construction et n'apprenait rien.
                    activities.length > 1
                        ? '${activities.length} activités'
                        : '1 activité',
                    style: TextStyle(fontSize: 13.5, color: colors.subtle),
                  ),
                  const SizedBox(height: 18),

                  // Rendu partiel : une partie des activités manque à l'appel.
                  // Le taire présenterait une liste amputée comme complète.
                  for (final warning in result.warnings) ...[
                    _PartialBanner(message: warning.message),
                    const SizedBox(height: 8),
                  ],

                  for (final activity in activities) ...[
                    _ActivityRow(activity: activity),
                    const SizedBox(height: 12),
                  ],

                  // « AJOUTER UNE ACTIVITÉ » A ÉTÉ RETIRÉ, PAS DÉPLACÉ.
                  //
                  // Le bouton portait `onPressed: () {}` — inerte, donc
                  // indistinguable d'un bouton en panne. Créer une boutique
                  // passe aujourd'hui par le parcours d'inscription
                  // (`POST /api/merchants`, cf. `AuthController.registerShop`),
                  // et aucun écran ne l'expose depuis une session ouverte. À
                  // rouvrir le jour où cet écran existera.
                ],
              );
            },
          ),
        ),
      ),
    );
  }
}

/// Bandeau d'un rendu incomplet du BFF.
class _PartialBanner extends StatelessWidget {
  const _PartialBanner({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
        decoration: BoxDecoration(
          color: AppTheme.foodAmberSoft,
          borderRadius: BorderRadius.circular(12),
        ),
        child: Row(
          children: [
            const Icon(Icons.cloud_off_outlined, size: 18, color: AppTheme.foodAmber),
            const SizedBox(width: 10),
            Expanded(
              child: Text(message,
                  style: const TextStyle(fontSize: 12.5, color: AppTheme.foodAmber)),
            ),
          ],
        ),
      );
}

class _ActivityRow extends ConsumerWidget {
  const _ActivityRow({required this.activity});

  final SellerActivity activity;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);

    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        border: Border.all(color: colors.line),
      ),
      child: Column(
        children: [
          Row(
            children: [
              Container(
                width: 40,
                height: 40,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: activity.universe.soft,
                  borderRadius: BorderRadius.circular(11),
                ),
                child: Text(
                  activity.initials,
                  style: TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w800,
                    color: activity.universe.accent,
                  ),
                ),
              ),
              const SizedBox(width: 11),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      activity.name,
                      style: TextStyle(
                        fontSize: 15.5,
                        fontWeight: FontWeight.w800,
                        color: colors.ink,
                      ),
                    ),
                    Text(
                      // « Boutique · HBAExpress » : le métier PUIS l'univers.
                      // L'univers seul ne dirait pas si c'est une boutique ou un
                      // restaurant — HBA Food n'accueille que des restaurants,
                      // mais HBAExpress pourrait demain accueillir autre chose.
                      '${activity.kind} · ${activity.universe.label}',
                      style: TextStyle(fontSize: 12.5, color: colors.subtle),
                    ),
                  ],
                ),
              ),

              // `null` = l'amont n'a pas su dire si l'activité prend des
              // commandes, ce qui n'est PAS « fermé » : on n'affiche alors rien.
              if (activity.isOpenNow != null)
                _OpenDot(isOpen: activity.isOpenNow!),
            ],
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              Expanded(
                child: FilledButton(
                  // Même contrainte que sur l'aiguillage de connexion : fixer
                  // l'état AVANT de naviguer, sinon `/home` affiche l'activité
                  // précédente.
                  onPressed: () {
                    ref.read(selectedActivityIdProvider.notifier).choisir(activity.id);
                    context.go('/home');
                  },
                  style: FilledButton.styleFrom(
                    minimumSize: const Size.fromHeight(AppTheme.minTapTarget),
                  ),
                  child: const Text('Gérer'),
                ),
              ),
              const SizedBox(width: 10),

              // LES RÉGLAGES NE S'OUVRENT QUE POUR UNE BOUTIQUE.
              //
              // `/shop` lit `GET /api/merchants/me` : la fiche du VENDEUR, pas
              // celle d'un restaurant. Un restaurateur qui l'ouvrirait verrait
              // les informations d'une autre entité, ou un 404 s'il n'est pas
              // vendeur. Les réglages d'un établissement Food vivent dans
              // food-service et n'ont pas encore d'écran.
              if (activity.universe == HbaUniverse.express)
                SizedBox(
                  width: AppTheme.minTapTarget,
                  height: AppTheme.minTapTarget,
                  child: OutlinedButton(
                    onPressed: () => context.push('/shop'),
                    style: OutlinedButton.styleFrom(
                      padding: EdgeInsets.zero,
                      side: BorderSide(color: colors.line),
                      foregroundColor: colors.subtle,
                    ),
                    child: const Icon(Icons.tune_rounded, size: 19),
                  ),
                ),
            ],
          ),
        ],
      ),
    );
  }
}

/// Point d'état « prend des commandes / n'en prend pas ».
class _OpenDot extends StatelessWidget {
  const _OpenDot({required this.isOpen});

  final bool isOpen;

  @override
  Widget build(BuildContext context) => Container(
        width: 8,
        height: 8,
        decoration: BoxDecoration(
          color: isOpen ? AppTheme.brandGreen : AppTheme.slate,
          shape: BoxShape.circle,
        ),
      );
}
