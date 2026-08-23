import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/mock/mission_mock_data.dart';
import '../../../../core/mock/mission_state.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../shared/widgets/driver_widgets.dart';
import '../mission_flow_screen.dart';
import '_stage_header.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 11 — CONFIRMER LE RETRAIT.
///
/// « Méthode de confirmation pilotée par Delivery Service. »
///
/// LE QR ET LE CODE NE SONT PAS DEUX MÉTHODES : L'UN SECOURT L'AUTRE.
///
/// « Scannez le QR du restaurant OU saisissez le code à 4 chiffres affiché sur le
/// ticket. » Le code existe parce qu'un écran fissuré, un ticket froissé ou une
/// caméra sale arrivent tous les jours — et qu'un livreur bloqué au comptoir ne
/// peut appeler personne d'utile.
///
/// LE CODE EST AFFICHÉ, PAS SAISI, ET C'EST UNE SIMULATION.
///
/// En service réel, le livreur LIT le code sur le ticket et le TAPE. Ici il est
/// montré pour que la démonstration avance sans caméra ni ticket. Le champ de
/// saisie reste à construire — c'est la différence entre montrer l'écran et le
/// faire fonctionner.
/// ═════════════════════════════════════════════════════════════════════════════
class PickupConfirmStage extends ConsumerWidget {
  const PickupConfirmStage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        child: ListView(
          children: [
            StageHeader(
              title: 'Confirmer le retrait',
              onBack: () => leaveMission(context, ref),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
              child: Column(
                children: [
                  DriverCard(
                    padding: const EdgeInsets.fromLTRB(16, 14, 16, 18),
                    child: Column(
                      children: [
                        Text(
                          'MÉTHODE CONFIGURÉE PAR HBA DELIVERY',
                          style: TextStyle(
                            fontSize: 10,
                            fontWeight: FontWeight.w800,
                            letterSpacing: 0.8,
                            color: colors.subtle,
                          ),
                        ),
                        const SizedBox(height: 16),
                        const _QrPlaceholder(),
                        const SizedBox(height: 16),
                        Text(
                          'Scannez le QR du '
                          '${MissionMockData.universe.pickupNoun}',
                          style: TextStyle(
                            fontSize: 17,
                            fontWeight: FontWeight.w800,
                            color: colors.ink,
                          ),
                        ),
                        const SizedBox(height: 4),
                        Text(
                          'ou saisissez le code à 4 chiffres affiché sur le ticket.',
                          textAlign: TextAlign.center,
                          style: TextStyle(
                            fontSize: 12.5,
                            height: 1.4,
                            color: colors.subtle,
                          ),
                        ),
                        const SizedBox(height: 14),
                        const _CodeDigits(code: MissionMockData.pickupCode),
                      ],
                    ),
                  ),
                  const SizedBox(height: 12),

                  DriverCard(
                    padding: const EdgeInsets.symmetric(
                        horizontal: 16, vertical: 14),
                    child: Row(
                      children: [
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                'COMMANDE',
                                style: TextStyle(
                                  fontSize: 10,
                                  fontWeight: FontWeight.w800,
                                  letterSpacing: 0.8,
                                  color: colors.subtle,
                                ),
                              ),
                              const SizedBox(height: 3),
                              Text(
                                MissionMockData.orderReference,
                                style: TextStyle(
                                  fontSize: 16,
                                  fontWeight: FontWeight.w800,
                                  color: colors.ink,
                                ),
                              ),
                            ],
                          ),
                        ),
                        Container(
                          padding: const EdgeInsets.symmetric(
                              horizontal: 10, vertical: 5),
                          decoration: BoxDecoration(
                            color: AppTheme.brandGreenSoft,
                            borderRadius: BorderRadius.circular(7),
                          ),
                          child: const Text(
                            'PRÊTE',
                            style: TextStyle(
                              fontSize: 10.5,
                              fontWeight: FontWeight.w800,
                              letterSpacing: 0.7,
                              color: AppTheme.brandGreen,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 20),

                  DriverPrimaryButton(
                    label: 'Démarrer la livraison',
                    onPressed: () =>
                        ref.read(missionFlowProvider.notifier).advance(),
                  ),
                  const SizedBox(height: 10),
                  Text(
                    'Statut : commande récupérée',
                    style: TextStyle(fontSize: 12, color: colors.subtle),
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

/// Motif de QR dessiné, sans dépendance ni caméra.
///
/// CE N'EST PAS UN VRAI QR CODE. Il n'encode rien et ne se scanne pas. Générer
/// un vrai QR demanderait un paquet de plus pour une image qui ne sera lue par
/// personne dans une simulation.
class _QrPlaceholder extends StatelessWidget {
  const _QrPlaceholder();

  @override
  Widget build(BuildContext context) => Container(
        width: 148,
        height: 148,
        padding: const EdgeInsets.all(18),
        decoration: BoxDecoration(
          color: AppTheme.charcoal,
          borderRadius: BorderRadius.circular(10),
        ),
        child: GridView.count(
          crossAxisCount: 7,
          crossAxisSpacing: 3,
          mainAxisSpacing: 3,
          physics: const NeverScrollableScrollPhysics(),
          children: [
            for (var i = 0; i < 49; i++)
              // Motif déterministe : il ne doit pas scintiller d'un rendu à
              // l'autre, ce qui donnerait l'impression d'un code qui change.
              ColoredBox(
                color: (i * 7 + i ~/ 7) % 3 == 0
                    ? AppTheme.charcoal
                    : Colors.white,
              ),
          ],
        ),
      );
}

class _CodeDigits extends StatelessWidget {
  const _CodeDigits({required this.code});

  final String code;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Row(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        for (final digit in code.split(''))
          Container(
            width: 34,
            height: 40,
            margin: const EdgeInsets.symmetric(horizontal: 3),
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: colors.bg,
              borderRadius: BorderRadius.circular(9),
            ),
            child: Text(
              digit,
              style: TextStyle(
                fontSize: 19,
                fontWeight: FontWeight.w800,
                color: colors.ink,
              ),
            ),
          ),
      ],
    );
  }
}
