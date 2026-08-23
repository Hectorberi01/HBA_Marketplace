import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../../core/mock/mission_mock_data.dart';
import '../../../../core/mock/mission_state.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../shared/widgets/driver_widgets.dart';
import '../../../../shared/widgets/mission_widgets.dart';
import '../mission_flow_screen.dart';
import '_stage_header.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 10 — COMMANDE EN PRÉPARATION.
///
/// « Pas de CTA "récupéré" tant que la commande n'est pas déclarée prête. »
///
/// C'EST LA RÈGLE LA PLUS IMPORTANTE DE TOUT LE FLUX.
///
/// Le bouton d'avancement est DÉSACTIVÉ et porte « En attente du restaurant… ».
/// Le laisser actif permettrait de déclarer un retrait qui n'a pas eu lieu : la
/// commande passerait « récupérée » côté client, le chronomètre de livraison
/// démarrerait, et le restaurant serait tenu pour responsable d'un retard qui
/// n'est pas le sien.
///
/// LE CHRONOMÈTRE MONTE, IL NE DESCEND PAS.
///
/// « 3 min 12 » d'attente écoulée est un fait ; « plus que 2 min » serait une
/// promesse faite à la place du restaurant. L'estimation reste à côté, annoncée
/// comme telle.
/// ═════════════════════════════════════════════════════════════════════════════
class WaitingStage extends ConsumerStatefulWidget {
  const WaitingStage({super.key});

  @override
  ConsumerState<WaitingStage> createState() => _WaitingStageState();
}

class _WaitingStageState extends ConsumerState<WaitingStage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) ref.read(pickupWaitProvider.notifier).start();
    });
  }

  @override
  void dispose() {
    ref.read(pickupWaitProvider.notifier).stop();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    ref.watch(pickupWaitProvider);
    final elapsed = ref.read(pickupWaitProvider.notifier).label;

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        child: ListView(
          children: [
            StageHeader(
              title: MissionMockData.orderReference,
              onBack: () => leaveMission(context, ref),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 12, 20, 24),
              child: Column(
                children: [
                  Container(
                    width: 92,
                    height: 92,
                    alignment: Alignment.center,
                    decoration: const BoxDecoration(
                      color: AppTheme.amberSoft,
                      shape: BoxShape.circle,
                    ),
                    child: const Icon(
                      Icons.timer_outlined,
                      size: 32,
                      color: AppTheme.amber,
                    ),
                  ),
                  const SizedBox(height: 18),

                  Text(
                    'Commande en préparation',
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      fontSize: 23,
                      fontWeight: FontWeight.w800,
                      color: colors.ink,
                    ),
                  ),
                  const SizedBox(height: 6),
                  Text(
                    'Le ${MissionMockData.universe.pickupNoun} prépare la '
                    'commande. Vous serez prévenu dès qu\'elle est prête.',
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      fontSize: 13.5,
                      height: 1.45,
                      color: colors.subtle,
                    ),
                  ),
                  const SizedBox(height: 20),

                  IntrinsicHeight(
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Expanded(child: _Clock(label: 'ATTENTE', value: elapsed)),
                        const SizedBox(width: 12),
                        Expanded(
                          child: _Clock(
                            label: 'ESTIMÉE',
                            value:
                                '${MissionMockData.preparationEstimateMin} min',
                            muted: true,
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 12),

                  DriverSecondaryButton(
                    label: 'Appeler le ${MissionMockData.universe.pickupNoun}',
                    onPressed: () {},
                  ),
                  const SizedBox(height: 12),

                  // DÉSACTIVÉ, ET LE LIBELLÉ DIT POURQUOI.
                  //
                  // Un bouton grisé sans explication laisse chercher ce qui
                  // manque. « En attente du restaurant… » désigne qui doit agir.
                  Opacity(
                    opacity: 0.55,
                    child: DriverPrimaryButton(
                      label: 'En attente du '
                          '${MissionMockData.universe.pickupNoun}…',
                    ),
                  ),
                  const SizedBox(height: 10),

                  _SimulateReady(
                    onTap: () => ref.read(missionFlowProvider.notifier).advance(),
                  ),
                  MissionTroubleLink(
                      label: 'Signaler un problème',
                      onTap: () => context.push('/incident'),
                    ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Clock extends StatelessWidget {
  const _Clock({required this.label, required this.value, this.muted = false});

  final String label;
  final String value;
  final bool muted;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return DriverCard(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 14),
      child: Column(
        children: [
          Text(
            label,
            style: TextStyle(
              fontSize: 10,
              fontWeight: FontWeight.w800,
              letterSpacing: 0.9,
              color: colors.subtle,
            ),
          ),
          const SizedBox(height: 6),
          Text(
            value,
            style: TextStyle(
              fontSize: 21,
              fontWeight: FontWeight.w800,
              color: muted ? colors.subtle : colors.ink,
            ),
          ),
        ],
      ),
    );
  }
}

/// Déclencheur de démonstration : il remplace l'événement temps réel du service.
///
/// IL NE DOIT PAS SURVIVRE À LA DÉMONSTRATION. Contour pointillé et fond pâle
/// pour qu'il ne ressemble à aucun autre bouton de l'application.
class _SimulateReady extends StatelessWidget {
  const _SimulateReady({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(AppTheme.radiusField),
        child: Container(
          width: double.infinity,
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 13),
          decoration: BoxDecoration(
            color: AppTheme.brandGreenSoft.withValues(alpha: 0.5),
            borderRadius: BorderRadius.circular(AppTheme.radiusField),
            border: Border.all(
              color: AppTheme.brandGreen.withValues(alpha: 0.32),
            ),
          ),
          child: const Text(
            '▶ Simuler « commande prête » (événement temps réel)',
            textAlign: TextAlign.center,
            style: TextStyle(
              fontSize: 12.5,
              fontWeight: FontWeight.w700,
              color: AppTheme.brandGreen,
            ),
          ),
        ),
      );
}
