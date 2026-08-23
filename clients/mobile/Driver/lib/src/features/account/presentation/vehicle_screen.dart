import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/mock/account_mock_data.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/driver_widgets.dart';
import '../../mission/presentation/stages/_stage_header.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 25 — MON VÉHICULE. « Type, marque, immatriculation, statut de vérification. »
///
/// CHANGER DE TYPE DE VÉHICULE N'EST PAS UN RÉGLAGE D'AFFICHAGE.
///
/// Le type détermine les missions attribuables : un vélo ne prend pas une course
/// de 12 km, un tricycle prend un volume qu'une moto refuse. Le basculer d'un
/// tap devrait donc, en service réel, exiger une nouvelle carte grise et une
/// nouvelle vérification — et remettre le statut « Vérifié » à zéro.
///
/// Rien de tout cela n'est dessiné. La sélection est ici purement visuelle, et
/// c'est signalé à l'écran : la brancher sans la règle laisserait accepter des
/// courses avec un véhicule non déclaré.
/// ═════════════════════════════════════════════════════════════════════════════
class VehicleScreen extends ConsumerStatefulWidget {
  const VehicleScreen({super.key});

  @override
  ConsumerState<VehicleScreen> createState() => _VehicleScreenState();
}

class _VehicleScreenState extends ConsumerState<VehicleScreen> {
  VehicleKind _kind = AccountMockData.vehicleKind;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        bottom: false,
        child: ListView(
          children: [
            StageHeader(title: 'Mon véhicule', onBack: () => context.pop()),
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  DriverCard(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Container(
                              padding: const EdgeInsets.symmetric(
                                  horizontal: 9, vertical: 5),
                              decoration: BoxDecoration(
                                color: colors.bg,
                                borderRadius: BorderRadius.circular(6),
                              ),
                              child: Text(
                                AccountMockData.vehicleKind.label.toUpperCase(),
                                style: TextStyle(
                                  fontSize: 10.5,
                                  fontWeight: FontWeight.w800,
                                  letterSpacing: 0.6,
                                  color: colors.subtle,
                                ),
                              ),
                            ),
                            const Spacer(),
                            if (AccountMockData.vehicleVerified)
                              Row(
                                children: [
                                  Container(
                                    width: 7,
                                    height: 7,
                                    decoration: const BoxDecoration(
                                      color: AppTheme.brandGreen,
                                      shape: BoxShape.circle,
                                    ),
                                  ),
                                  const SizedBox(width: 6),
                                  const Text(
                                    'Vérifié',
                                    style: TextStyle(
                                      fontSize: 12.5,
                                      fontWeight: FontWeight.w700,
                                      color: AppTheme.brandGreen,
                                    ),
                                  ),
                                ],
                              ),
                          ],
                        ),
                        const SizedBox(height: 12),
                        Text(
                          AccountMockData.vehicleModel,
                          style: TextStyle(
                            fontSize: 23,
                            fontWeight: FontWeight.w800,
                            color: colors.ink,
                          ),
                        ),
                        const SizedBox(height: 2),
                        Text(
                          AccountMockData.plate,
                          style: TextStyle(
                            fontSize: 14,
                            fontWeight: FontWeight.w700,
                            // Espacement des lettres : une plaque se lit
                            // caractère par caractère, jamais comme un mot.
                            letterSpacing: 1.4,
                            color: colors.subtle,
                          ),
                        ),
                        const SizedBox(height: 14),
                        Divider(height: 1, color: colors.line),
                        const SizedBox(height: 12),
                        Row(
                          children: [
                            const Expanded(
                              child: _Field(
                                label: 'COULEUR',
                                value: AccountMockData.vehicleColor,
                              ),
                            ),
                            const Expanded(
                              child: _Field(
                                label: 'ANNÉE',
                                value: AccountMockData.vehicleYear,
                              ),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 12),

                  Row(
                    children: [
                      Expanded(
                        child: DriverSecondaryButton(
                          label: 'Modifier',
                          onPressed: () {},
                        ),
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        child: FilledButton(
                          onPressed: () => context.push('/documents'),
                          style: FilledButton.styleFrom(
                            minimumSize: const Size.fromHeight(
                                AppTheme.primaryButtonHeight),
                            backgroundColor: AppTheme.charcoal,
                            foregroundColor: Colors.white,
                            shape: RoundedRectangleBorder(
                              borderRadius:
                                  BorderRadius.circular(AppTheme.radiusField),
                            ),
                            textStyle: const TextStyle(
                              fontSize: 15,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                          child: const Text('Documents'),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 18),

                  Text(
                    'CHANGER DE VÉHICULE',
                    style: TextStyle(
                      fontSize: 10.5,
                      fontWeight: FontWeight.w800,
                      letterSpacing: 0.9,
                      color: colors.subtle,
                    ),
                  ),
                  const SizedBox(height: 10),

                  GridView.count(
                    shrinkWrap: true,
                    physics: const NeverScrollableScrollPhysics(),
                    crossAxisCount: 2,
                    crossAxisSpacing: 12,
                    mainAxisSpacing: 12,
                    childAspectRatio: 3.3,
                    children: [
                      for (final k in VehicleKind.values)
                        _KindTile(
                          label: k.label,
                          selected: k == _kind,
                          onTap: () => setState(() => _kind = k),
                        ),
                    ],
                  ),
                  const SizedBox(height: 12),

                  Text(
                    // AVERTISSEMENT VISIBLE, PAS SEULEMENT EN COMMENTAIRE.
                    //
                    // Le type de véhicule conditionne les missions attribuées.
                    // Laisser croire qu'un tap suffit ferait accepter des courses
                    // avec un véhicule non déclaré.
                    'Le changement de type nécessitera une nouvelle carte grise '
                    'et une revérification. Cette sélection n\'est pas encore '
                    'enregistrée.',
                    style: TextStyle(
                      fontSize: 12,
                      height: 1.4,
                      fontStyle: FontStyle.italic,
                      color: colors.subtle,
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

class _Field extends StatelessWidget {
  const _Field({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Column(
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
        const SizedBox(height: 3),
        Text(
          value,
          style: TextStyle(
            fontSize: 15,
            fontWeight: FontWeight.w700,
            color: colors.ink,
          ),
        ),
      ],
    );
  }
}

class _KindTile extends StatelessWidget {
  const _KindTile({
    required this.label,
    required this.selected,
    required this.onTap,
  });

  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(AppTheme.radiusField),
      child: Container(
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(AppTheme.radiusField),
          border: Border.all(
            color: selected ? AppTheme.brandGreen : colors.line,
            width: selected ? 1.6 : 1,
          ),
        ),
        child: Text(
          label,
          style: TextStyle(
            fontSize: 15,
            fontWeight: FontWeight.w700,
            color: selected ? AppTheme.brandGreen : colors.ink,
          ),
        ),
      ),
    );
  }
}
