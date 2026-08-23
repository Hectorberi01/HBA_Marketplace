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
/// 13 — ARRIVÉ CHEZ LE CLIENT. « Contact avant preuve de livraison. »
///
/// « APPELER LE CLIENT » EST LE SEUL BOUTON NOIR DE L'APPLICATION.
///
/// Ni vert — il n'avance pas la course — ni gris — il est la première chose à
/// faire en arrivant, et l'instruction du client le demandait explicitement
/// (« Appeler en arrivant »). Le noir dit : action franche, mais pas l'issue.
///
/// LE NUMÉRO EST MASQUÉ ET L'APPEL PASSE PAR HBA.
///
/// Le livreur ne voit jamais le numéro du client, et réciproquement. C'est ce qui
/// empêche qu'un contact né d'une course se prolonge après elle — le risque le
/// plus concret de ce métier, et celui dont on parle le moins.
/// ═════════════════════════════════════════════════════════════════════════════
class ArrivedDropoffStage extends ConsumerWidget {
  const ArrivedDropoffStage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        child: ListView(
          children: [
            StageHeader(
              title: MissionMockData.reference,
              onBack: () => leaveMission(context, ref),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  // Bandeau VERT ici, alors que celui du retrait était ambre :
                  // à ce stade, rien ne bloque — on est au bon endroit et il ne
                  // reste qu'à remettre.
                  Container(
                    width: double.infinity,
                    padding: const EdgeInsets.all(16),
                    decoration: BoxDecoration(
                      color: AppTheme.brandGreenSoft,
                      borderRadius: BorderRadius.circular(AppTheme.radiusCard),
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Vous êtes arrivé.',
                          style: TextStyle(
                            fontSize: 19,
                            fontWeight: FontWeight.w800,
                            color: colors.ink,
                          ),
                        ),
                        const SizedBox(height: 5),
                        Text(
                          '${MissionMockData.dropoffAddress.split(' — ').first} '
                          '— maison portail noir.',
                          style: const TextStyle(
                            fontSize: 13.5,
                            height: 1.4,
                            color: AppTheme.brandGreen,
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
                            Container(
                              width: 42,
                              height: 42,
                              alignment: Alignment.center,
                              decoration: BoxDecoration(
                                color: colors.bg,
                                borderRadius: BorderRadius.circular(12),
                              ),
                              child: Text(
                                'SA',
                                style: TextStyle(
                                  fontSize: 13,
                                  fontWeight: FontWeight.w800,
                                  color: colors.subtle,
                                ),
                              ),
                            ),
                            const SizedBox(width: 12),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    MissionMockData.customerName,
                                    style: TextStyle(
                                      fontSize: 16,
                                      fontWeight: FontWeight.w800,
                                      color: colors.ink,
                                    ),
                                  ),
                                  Text(
                                    MissionMockData.customerPhoneNote,
                                    style: TextStyle(
                                      fontSize: 12.5,
                                      color: colors.subtle,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: 14),
                        Row(
                          children: [
                            Expanded(
                              child: FilledButton(
                                onPressed: () {},
                                style: FilledButton.styleFrom(
                                  minimumSize: const Size.fromHeight(
                                      AppTheme.primaryButtonHeight),
                                  backgroundColor: AppTheme.charcoal,
                                  foregroundColor: Colors.white,
                                  shape: RoundedRectangleBorder(
                                    borderRadius: BorderRadius.circular(
                                        AppTheme.radiusField),
                                  ),
                                  textStyle: const TextStyle(
                                    fontSize: 14.5,
                                    fontWeight: FontWeight.w700,
                                  ),
                                ),
                                child: const Text('Appeler le client'),
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
                  const SizedBox(height: 20),

                  DriverPrimaryButton(
                    label: 'Confirmer la livraison',
                    onPressed: () =>
                        ref.read(missionFlowProvider.notifier).advance(),
                  ),
                  Center(
                    child: MissionTroubleLink(
                      label: 'Client absent / problème',
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
