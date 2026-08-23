import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/mock/mission_mock_data.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../shared/utils/formatters.dart';
import '../mission_flow_screen.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 15 — LIVRAISON TERMINÉE. « Récapitulatif court : gain, temps, distance. »
///
/// ÉCRAN VERT PLEINE PAGE — LE SEUL DE L'APPLICATION.
///
/// C'est le seul moment où l'application n'a rien à demander. Le vert entier, la
/// somme en grand, trois chiffres et une sortie : rien à lire, rien à décider.
///
/// « CRÉDITÉ SUR VOTRE SOLDE », PAS « VERSÉ ».
///
/// Le gain rejoint un solde ; le versement est un autre geste, sur un autre écran
/// (20 · Retrait). Confondre les deux ferait attendre un virement qui ne
/// viendra qu'au retrait demandé.
/// ═════════════════════════════════════════════════════════════════════════════
class DeliveredStage extends ConsumerWidget {
  const DeliveredStage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) => Scaffold(
        backgroundColor: AppTheme.brandGreen,
        body: SafeArea(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(24, 24, 24, 20),
            child: Column(
              children: [
                const Spacer(flex: 2),

                Container(
                  width: 72,
                  height: 72,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(
                    color: Colors.white.withValues(alpha: 0.16),
                    shape: BoxShape.circle,
                  ),
                  child: const Icon(Icons.check, size: 34, color: Colors.white),
                ),
                const SizedBox(height: 20),

                const Text(
                  'Livraison terminée',
                  style: TextStyle(
                    fontSize: 27,
                    fontWeight: FontWeight.w800,
                    color: Colors.white,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  MissionMockData.orderReference,
                  style: TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w600,
                    color: Colors.white.withValues(alpha: 0.7),
                  ),
                ),
                const SizedBox(height: 22),

                Text(
                  // Le « + » est écrit : il annonce un ajout au solde, pas un
                  // montant neutre.
                  '+${Format.amount(MissionMockData.earning)} F',
                  style: const TextStyle(
                    fontSize: 46,
                    fontWeight: FontWeight.w800,
                    color: Colors.white,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  'crédité sur votre solde',
                  style: TextStyle(
                    fontSize: 13,
                    color: Colors.white.withValues(alpha: 0.8),
                  ),
                ),

                const Spacer(flex: 3),

                IntrinsicHeight(
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Expanded(
                        child: _Stat(
                          label: 'TEMPS',
                          value:
                              '${MissionMockData.completedDurationMin} min',
                        ),
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        child: _Stat(
                          label: 'DISTANCE',
                          value: Format.km(
                            MissionMockData.dropoffDistanceKm,
                            upper: false,
                          ),
                        ),
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        child: _Stat(
                          label: 'COURSES',
                          // Calculé : 8 au tableau de bord, plus celle-ci.
                          value: '${MissionMockData.deliveriesAfter}',
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 16),

                FilledButton(
                  onPressed: () => leaveMission(context, ref),
                  style: FilledButton.styleFrom(
                    minimumSize:
                        const Size.fromHeight(AppTheme.primaryButtonHeight),
                    backgroundColor: Colors.white,
                    foregroundColor: AppTheme.charcoal,
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(AppTheme.radiusField),
                    ),
                    textStyle: const TextStyle(
                      fontSize: 16,
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                  child: const Text('Continuer'),
                ),
              ],
            ),
          ),
        ),
      );
}

class _Stat extends StatelessWidget {
  const _Stat({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 13),
        decoration: BoxDecoration(
          color: Colors.white.withValues(alpha: 0.14),
          borderRadius: BorderRadius.circular(12),
        ),
        child: Column(
          children: [
            Text(
              label,
              style: TextStyle(
                fontSize: 9.5,
                fontWeight: FontWeight.w800,
                letterSpacing: 0.8,
                color: Colors.white.withValues(alpha: 0.7),
              ),
            ),
            const SizedBox(height: 5),
            Text(
              value,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(
                fontSize: 17,
                fontWeight: FontWeight.w800,
                color: Colors.white,
              ),
            ),
          ],
        ),
      );
}
