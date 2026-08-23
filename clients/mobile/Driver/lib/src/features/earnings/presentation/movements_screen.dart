import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/mock/earnings_mock_data.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/driver_widgets.dart';
import '../../mission/presentation/stages/_stage_header.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 19 — DÉTAIL DES REVENUS.
///
/// « Chaque mouvement explicité, aucun détail interne plateforme. »
///
/// CETTE PHRASE EST UNE RÈGLE, ET ELLE COUPE DANS LES DEUX SENS.
///
/// EXPLICITÉ : un « Ajustement −500 F » sans motif est la ligne qui déclenche un
/// appel au support. « Annulation client #DEL-2001 » désigne la course et permet
/// de vérifier soi-même.
///
/// AUCUN DÉTAIL INTERNE : pas de commission plateforme, pas de part restaurant,
/// pas d'identifiant de transaction PSP. Le livreur voit ce qui entre et sort de
/// SON solde — le reste ne le concerne pas et l'inquiéterait sans l'informer.
///
/// « SOLDE APRÈS MOUVEMENTS » N'EST PAS LA SOMME DES LIGNES.
///
/// C'est le solde RÉSULTANT : 61 000 avant, −18 500 de mouvements, 42 500 après.
/// Quelqu'un qui additionnerait les quatre lignes trouverait −18 500 et croirait
/// à une erreur. Le libellé le dit, et le total du haut n'est donc pas affiché.
/// ═════════════════════════════════════════════════════════════════════════════
class MovementsScreen extends ConsumerWidget {
  const MovementsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        bottom: false,
        child: ListView(
          children: [
            StageHeader(
              title: 'Détail des revenus',
              onBack: () => context.pop(),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    EarningsMockData.todayLabel,
                    style: TextStyle(
                      fontSize: 10.5,
                      fontWeight: FontWeight.w800,
                      letterSpacing: 0.9,
                      color: colors.subtle,
                    ),
                  ),
                  const SizedBox(height: 10),

                  DriverCard(
                    padding: EdgeInsets.zero,
                    child: Column(
                      children: [
                        for (var i = 0;
                            i < EarningsMockData.movements.length;
                            i++) ...[
                          if (i > 0) Divider(height: 1, color: colors.line),
                          _MovementRow(
                              movement: EarningsMockData.movements[i]),
                        ],
                      ],
                    ),
                  ),
                  const SizedBox(height: 12),

                  Container(
                    width: double.infinity,
                    padding: const EdgeInsets.symmetric(
                        horizontal: 16, vertical: 16),
                    decoration: BoxDecoration(
                      color: AppTheme.brandGreenSoft,
                      borderRadius: BorderRadius.circular(AppTheme.radiusCard),
                    ),
                    child: Row(
                      children: [
                        Expanded(
                          child: Text(
                            'Solde après mouvements',
                            style: TextStyle(
                              fontSize: 14,
                              fontWeight: FontWeight.w600,
                              color: colors.ink,
                            ),
                          ),
                        ),
                        Text(
                          '${Format.amount(EarningsMockData.available)} F',
                          style: TextStyle(
                            fontSize: 19,
                            fontWeight: FontWeight.w800,
                            color: colors.ink,
                          ),
                        ),
                      ],
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

class _MovementRow extends StatelessWidget {
  const _MovementRow({required this.movement});

  final DriverMovement movement;

  /// UN RETRAIT EST NEUTRE, UN AJUSTEMENT EST ROUGE.
  ///
  /// Les deux sont négatifs, mais ils ne veulent pas dire la même chose : le
  /// retrait est un virement DEMANDÉ, l'ajustement une reprise SUBIE. Les
  /// peindre pareil ferait lire chaque virement comme une sanction.
  static Color _tint(DriverMovement m, AppColors colors) => switch (m.kind) {
        MovementKind.delivery => AppTheme.brandGreen,
        MovementKind.bonus => AppTheme.brandGreen,
        MovementKind.adjustment => AppTheme.danger,
        MovementKind.payout => colors.ink,
      };

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final positive = movement.amount > 0;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  movement.title,
                  style: TextStyle(
                    fontSize: 15,
                    fontWeight: FontWeight.w700,
                    color: colors.ink,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  movement.detail,
                  style: TextStyle(fontSize: 12.5, color: colors.subtle),
                ),
              ],
            ),
          ),
          const SizedBox(width: 10),
          Text(
            // Le « + » est explicite sur un crédit ; le « − » vient du signe du
            // montant via `Format.amount`.
            '${positive ? '+' : ''}${Format.amount(movement.amount)} F',
            style: TextStyle(
              fontSize: 15,
              fontWeight: FontWeight.w800,
              color: _tint(movement, colors),
            ),
          ),
        ],
      ),
    );
  }
}
