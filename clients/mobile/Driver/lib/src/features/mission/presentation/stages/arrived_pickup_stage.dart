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
/// 09 — ARRIVÉ AU RETRAIT. « Identification commande et établissement. »
///
/// LE BOUTON DE VOTRE MAQUETTE DIT « JE SUIS ARRIVÉ ». JE L'AI CHANGÉ.
///
/// L'écran s'ouvre sur « Vous êtes arrivé au point de retrait » — l'arrivée est
/// donc déjà actée par l'étape précédente, dont le bouton portait DÉJÀ « Je suis
/// arrivé ». Le répéter ici ferait toucher deux fois le même mot pour deux
/// actions différentes, et le second tap n'aurait aucun effet lisible.
///
/// Ce que ce bouton fait réellement, c'est déclarer qu'on s'est présenté au
/// comptoir — ce que le bandeau demande explicitement. D'où « Je me suis
/// présenté ». Si vous préférez conserver le libellé dessiné, c'est une ligne à
/// changer, mais alors les deux écrans se ressembleront à s'y méprendre.
///
/// LE NUMÉRO PRÉSENTÉ EST CELUI DE LA COMMANDE, PAS DE LA COURSE.
///
/// « #FOOD-2058 » et non « #DEL-2058 » : au comptoir, le restaurateur ne connaît
/// que sa propre référence. Cf. `MissionMockData.orderReference`.
/// ═════════════════════════════════════════════════════════════════════════════
class ArrivedPickupStage extends ConsumerWidget {
  const ArrivedPickupStage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        child: ListView(
          children: [
            StageHeader(
              title: 'Point de retrait',
              onBack: () => leaveMission(context, ref),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  // Bandeau ambre : une action est attendue du livreur.
                  Container(
                    width: double.infinity,
                    padding: const EdgeInsets.all(16),
                    decoration: BoxDecoration(
                      color: AppTheme.amberSoft,
                      borderRadius: BorderRadius.circular(AppTheme.radiusCard),
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Vous êtes arrivé au point de retrait.',
                          style: TextStyle(
                            fontSize: 18,
                            fontWeight: FontWeight.w800,
                            height: 1.3,
                            color: colors.ink,
                          ),
                        ),
                        const SizedBox(height: 6),
                        const Text(
                          'Présentez-vous au comptoir avec le numéro de commande.',
                          style: TextStyle(
                            fontSize: 13.5,
                            height: 1.4,
                            color: AppTheme.amber,
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 12),

                  DriverCard(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Expanded(
                              child: Text(
                                MissionMockData.pickupName,
                                style: TextStyle(
                                  fontSize: 19,
                                  fontWeight: FontWeight.w800,
                                  color: colors.ink,
                                ),
                              ),
                            ),
                            const MissionStatusPill(
                              status: MissionListStatus.inProgress,
                            ),
                          ],
                        ),
                        const SizedBox(height: 14),
                        IntrinsicHeight(
                          child: Row(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              const Expanded(
                                child: _Box(
                                  label: 'COMMANDE',
                                  value: MissionMockData.orderReference,
                                ),
                              ),
                              const SizedBox(width: 10),
                              Expanded(
                                child: _Box(
                                  label: 'PRÉPARATION',
                                  value: '≈ '
                                      '${MissionMockData.preparationEstimateMin} min',
                                ),
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(height: 10),
                        Container(
                          width: double.infinity,
                          padding: const EdgeInsets.symmetric(
                              horizontal: 13, vertical: 13),
                          decoration: BoxDecoration(
                            color: colors.bg,
                            borderRadius:
                                BorderRadius.circular(AppTheme.radiusField),
                          ),
                          child: Text(
                            // Consigne de manutention : elle vaut pour le
                            // livreur, pas pour le comptoir. « maintenir
                            // vertical » évite de retrouver la sauce dans le sac.
                            MissionMockData.parcelNote,
                            style: TextStyle(fontSize: 13.5, color: colors.ink),
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 20),

                  DriverPrimaryButton(
                    label: 'Je me suis présenté',
                    onPressed: () =>
                        ref.read(missionFlowProvider.notifier).advance(),
                  ),
                  Center(
                    child: MissionTroubleLink(
                      label: 'Signaler un problème',
                      onTap: () => context.push('/incident'),
                    ),
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

class _Box extends StatelessWidget {
  const _Box({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 13, vertical: 12),
      decoration: BoxDecoration(
        color: colors.bg,
        borderRadius: BorderRadius.circular(AppTheme.radiusField),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            label,
            style: TextStyle(
              fontSize: 10,
              fontWeight: FontWeight.w800,
              letterSpacing: 0.8,
              color: colors.subtle,
            ),
          ),
          const SizedBox(height: 4),
          Text(
            value,
            style: TextStyle(
              fontSize: 16,
              fontWeight: FontWeight.w800,
              color: colors.ink,
            ),
          ),
        ],
      ),
    );
  }
}
