import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../../core/mock/mission_mock_data.dart';
import '../../../../core/mock/mission_state.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../shared/utils/formatters.dart';
import '../../../../shared/widgets/driver_widgets.dart';
import '../../../../shared/widgets/mission_widgets.dart';

/// 07 — MISSION ACCEPTÉE. « Confirmation courte, adresse pickup complète, un CTA. »
///
/// L'ADRESSE DE RETRAIT SE COMPLÈTE ICI, PAS CELLE DU CLIENT.
///
/// « Rue 12.045, Fidjrossè Plage — face à la station Oryx » remplace le simple
/// quartier de l'écran précédent. La destination, elle, reste « Akpakpa » :
/// l'adresse du client n'apparaît qu'après le RETRAIT. Deux dévoilements
/// successifs, chacun au moment où l'information devient nécessaire.
class AcceptedStage extends ConsumerWidget {
  const AcceptedStage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(20, 16, 20, 24),
          children: [
            const Center(child: _SuccessHalo()),
            const SizedBox(height: 16),
            Text(
              'Mission acceptée',
              textAlign: TextAlign.center,
              style: TextStyle(
                fontSize: 24,
                fontWeight: FontWeight.w800,
                color: colors.ink,
              ),
            ),
            const SizedBox(height: 3),
            Text(
              '${MissionMockData.reference} · '
              '${MissionMockData.universe.badge.toLowerCase() == 'hba food' ? 'HBA Food' : MissionMockData.universe.badge}',
              textAlign: TextAlign.center,
              style: TextStyle(fontSize: 13, color: colors.subtle),
            ),
            const SizedBox(height: 20),

            DriverCard(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const _SectionLabel('POINT DE RETRAIT', color: AppTheme.amber),
                  const SizedBox(height: 8),
                  Text(
                    MissionMockData.pickupName,
                    style: TextStyle(
                      fontSize: 19,
                      fontWeight: FontWeight.w800,
                      color: colors.ink,
                    ),
                  ),
                  const SizedBox(height: 3),
                  Text(
                    MissionMockData.pickupAddress,
                    style: TextStyle(
                      fontSize: 13.5,
                      height: 1.4,
                      color: colors.subtle,
                    ),
                  ),
                  const SizedBox(height: 14),
                  Row(
                    children: [
                      Expanded(
                        child: DriverSecondaryButton(
                          label: 'Appeler',
                          onPressed: () {},
                        ),
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        child: DriverSecondaryButton(
                          label: 'Message',
                          onPressed: () {},
                        ),
                      ),
                    ],
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
                  _SectionLabel('DESTINATION', color: colors.subtle),
                  const SizedBox(height: 8),
                  Text(
                    MissionMockData.dropoffArea,
                    style: TextStyle(
                      fontSize: 19,
                      fontWeight: FontWeight.w800,
                      color: colors.ink,
                    ),
                  ),
                  const SizedBox(height: 3),
                  Text(
                    'Adresse complète disponible après le retrait',
                    style: TextStyle(fontSize: 13.5, color: colors.subtle),
                  ),
                  const SizedBox(height: 14),
                  IntrinsicHeight(
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Expanded(
                          child: _Metric(
                            label: 'Distance',
                            value: Format.km(
                              MissionMockData.dropoffDistanceKm,
                              upper: false,
                            ),
                          ),
                        ),
                        Expanded(
                          child: _Metric(
                            label: 'Durée',
                            value: '${MissionMockData.totalDurationMin} min',
                          ),
                        ),
                        Expanded(
                          child: _Metric(
                            label: 'Gain',
                            value: '${Format.amount(MissionMockData.earning)} F',
                            accent: AppTheme.brandGreen,
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 20),

            DriverPrimaryButton(
              label: 'Démarrer l\'itinéraire',
              onPressed: () => ref.read(missionFlowProvider.notifier).advance(),
            ),
            const SizedBox(height: 4),
            Center(
              child: MissionTroubleLink(
                      label: 'Signaler un problème',
                      onTap: () => context.push('/incident'),
                    ),
            ),
          ],
        ),
      ),
    );
  }
}

class _SuccessHalo extends StatelessWidget {
  const _SuccessHalo();

  @override
  Widget build(BuildContext context) => Container(
        width: 66,
        height: 66,
        alignment: Alignment.center,
        decoration: const BoxDecoration(
          color: AppTheme.brandGreenSoft,
          shape: BoxShape.circle,
        ),
        child: const Icon(Icons.check, size: 30, color: AppTheme.brandGreen),
      );
}

class _SectionLabel extends StatelessWidget {
  const _SectionLabel(this.text, {required this.color});

  final String text;
  final Color color;

  @override
  Widget build(BuildContext context) => Text(
        text,
        style: TextStyle(
          fontSize: 10.5,
          fontWeight: FontWeight.w800,
          letterSpacing: 0.9,
          color: color,
        ),
      );
}

class _Metric extends StatelessWidget {
  const _Metric({required this.label, required this.value, this.accent});

  final String label;
  final String value;
  final Color? accent;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: TextStyle(fontSize: 11.5, color: colors.subtle)),
        const SizedBox(height: 2),
        Text(
          value,
          style: TextStyle(
            fontSize: 15.5,
            fontWeight: FontWeight.w800,
            color: accent ?? colors.ink,
          ),
        ),
      ],
    );
  }
}
