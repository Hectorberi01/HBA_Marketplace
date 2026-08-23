import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/mock/mission_mock_data.dart';
import '../../../../core/mock/mission_state.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../shared/utils/formatters.dart';
import '../../../../shared/widgets/driver_widgets.dart';
import '../../../../shared/widgets/mission_widgets.dart';
import '../mission_flow_screen.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 06 — NOUVELLE MISSION. Feuille plein écran, rebours, destination partielle.
///
/// LA DESTINATION EST INCOMPLÈTE AVANT ACCEPTATION, ET C'EST LE POINT.
///
/// « Akpakpa » puis « Adresse complète après acceptation ». Sans cela, un livreur
/// pourrait écrémer : lire l'adresse exacte, juger le quartier, refuser. Le
/// quartier et la distance suffisent à décider ; la rue ne sert qu'à rouler.
///
/// C'est aussi une protection du client, dont l'adresse ne circule pas auprès de
/// livreurs qui ne prendront pas la course.
///
/// « REFUSER » EST À GAUCHE, PLUS PETIT, ET SANS CONFIRMATION.
///
/// Refuser doit rester immédiat : on refuse souvent parce qu'on roule déjà. Une
/// boîte de dialogue ferait perdre les secondes du rebours à quelqu'un qui a
/// déjà décidé.
/// ═════════════════════════════════════════════════════════════════════════════
class OfferedStage extends ConsumerWidget {
  const OfferedStage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final seconds = ref.watch(missionCountdownProvider);

    return Scaffold(
      backgroundColor: const Color(0xFF10181F),
      body: Stack(
        children: [
          const Positioned.fill(
            child: MissionMapSketch(
              markerLabel: 'P',
              markerColor: AppTheme.amber,
              dark: true,
            ),
          ),

          Align(
            alignment: Alignment.bottomCenter,
            child: Container(
              decoration: BoxDecoration(
                color: colors.surface,
                borderRadius:
                    const BorderRadius.vertical(top: Radius.circular(22)),
              ),
              child: SafeArea(
                top: false,
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(20, 10, 20, 16),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
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
                      const SizedBox(height: 16),

                      Row(
                        children: [
                          const UniverseBadge(
                            universe: MissionMockData.universe,
                          ),
                          const Spacer(),
                          Text(
                            // « seconde » S'ACCORDE. À 1, « 1 seconde ».
                            '$seconds ${seconds > 1 ? 'secondes' : 'seconde'}',
                            style: const TextStyle(
                              fontSize: 15,
                              fontWeight: FontWeight.w800,
                              color: AppTheme.amber,
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 10),

                      ClipRRect(
                        borderRadius: BorderRadius.circular(3),
                        child: LinearProgressIndicator(
                          // La barre se VIDE : elle représente le temps restant,
                          // pas le temps écoulé. Une barre qui se remplit se lit
                          // comme une progression, donc comme quelque chose de
                          // positif.
                          value: seconds / MissionMockData.acceptSeconds,
                          minHeight: 4,
                          backgroundColor: colors.line,
                          valueColor:
                              const AlwaysStoppedAnimation(AppTheme.amber),
                        ),
                      ),
                      const SizedBox(height: 18),

                      Text(
                        'Nouvelle mission',
                        style: TextStyle(
                          fontSize: 24,
                          fontWeight: FontWeight.w800,
                          color: colors.ink,
                        ),
                      ),
                      const SizedBox(height: 3),
                      Text(
                        MissionMockData.missionSummary,
                        style: TextStyle(fontSize: 13.5, color: colors.subtle),
                      ),
                      const SizedBox(height: 16),

                      const _RouteBox(),
                      const SizedBox(height: 12),

                      Container(
                        padding: const EdgeInsets.symmetric(
                            horizontal: 16, vertical: 15),
                        decoration: BoxDecoration(
                          color: AppTheme.brandGreenSoft,
                          borderRadius:
                              BorderRadius.circular(AppTheme.radiusField),
                        ),
                        child: Row(
                          children: [
                            Text(
                              'Gain estimé',
                              style: TextStyle(
                                fontSize: 14,
                                color: colors.subtle,
                              ),
                            ),
                            const Spacer(),
                            Text(
                              Format.cfaAmount(MissionMockData.earning),
                              style: TextStyle(
                                fontSize: 19,
                                fontWeight: FontWeight.w800,
                                color: colors.ink,
                              ),
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(height: 16),

                      Row(
                        children: [
                          Expanded(
                            child: DriverSecondaryButton(
                              label: 'Refuser',
                              onPressed: () {
                                ref.read(missionFlowProvider.notifier).decline();
                                leaveMission(context, ref);
                              },
                            ),
                          ),
                          const SizedBox(width: 12),
                          Expanded(
                            // Deux fois plus large : c'est l'issue attendue.
                            flex: 2,
                            child: DriverPrimaryButton(
                              label: 'Accepter',
                              onPressed: () =>
                                  ref.read(missionFlowProvider.notifier).advance(),
                            ),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// Retrait puis destination, reliés par un trait vertical.
class _RouteBox extends StatelessWidget {
  const _RouteBox();

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Container(
      padding: const EdgeInsets.fromLTRB(14, 14, 14, 14),
      decoration: BoxDecoration(
        color: colors.bg,
        borderRadius: BorderRadius.circular(AppTheme.radiusField),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // Le trait relie les deux points : il dit qu'il s'agit d'un TRAJET, et
          // non de deux informations posées l'une sous l'autre.
          Column(
            children: [
              const _RoutePoint(color: AppTheme.amber, square: false),
              Container(width: 2, height: 58, color: colors.line),
              const _RoutePoint(color: AppTheme.brandGreen, square: true),
            ],
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                _Leg(
                  label: 'RETRAIT · '
                      '${Format.km(MissionMockData.pickupDistanceKm)}',
                  labelColor: AppTheme.amber,
                  title: MissionMockData.pickupName,
                  detail: MissionMockData.pickupArea,
                ),
                const SizedBox(height: 14),
                _Leg(
                  label: 'DESTINATION · '
                      '${Format.km(MissionMockData.dropoffDistanceKm)}',
                  labelColor: colors.subtle,
                  title: MissionMockData.dropoffArea,
                  // MENTION EXPLICITE, PAS UNE ADRESSE FLOUTÉE.
                  //
                  // Dire pourquoi l'adresse manque évite de croire à un défaut de
                  // l'application — et annonce ce qu'on obtient en acceptant.
                  detail: 'Adresse complète après acceptation',
                  detailMuted: true,
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _RoutePoint extends StatelessWidget {
  const _RoutePoint({required this.color, required this.square});

  final Color color;
  final bool square;

  @override
  Widget build(BuildContext context) => Container(
        width: 10,
        height: 10,
        decoration: BoxDecoration(
          color: color,
          shape: square ? BoxShape.rectangle : BoxShape.circle,
          borderRadius: square ? BorderRadius.circular(2) : null,
        ),
      );
}

class _Leg extends StatelessWidget {
  const _Leg({
    required this.label,
    required this.labelColor,
    required this.title,
    required this.detail,
    this.detailMuted = false,
  });

  final String label;
  final Color labelColor;
  final String title;
  final String detail;
  final bool detailMuted;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          label,
          style: TextStyle(
            fontSize: 10.5,
            fontWeight: FontWeight.w800,
            letterSpacing: 0.7,
            color: labelColor,
          ),
        ),
        const SizedBox(height: 3),
        Text(
          title,
          style: TextStyle(
            fontSize: 16,
            fontWeight: FontWeight.w800,
            color: colors.ink,
          ),
        ),
        Text(
          detail,
          style: TextStyle(
            fontSize: 13,
            fontStyle: detailMuted ? FontStyle.italic : FontStyle.normal,
            color: colors.subtle,
          ),
        ),
      ],
    );
  }
}
