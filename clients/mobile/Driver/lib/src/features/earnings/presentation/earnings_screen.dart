import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/mock/earnings_mock_data.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/driver_widgets.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 18 — REVENUS. « Disponible / en attente en haut, graphique 7 jours simple. »
///
/// « DISPONIBLE » ET « EN ATTENTE » SONT SÉPARÉS PAR UN TRAIT, DANS LA MÊME
///    CARTE, ET C'EST EXACTEMENT LE BON COMPROMIS.
///
/// Deux cartes distinctes laisseraient croire à deux comptes. Un seul nombre
/// ferait promettre un retrait que le service refuserait — les 15 000 F en
/// attente sont des courses livrées mais pas encore libérées.
///
/// Le trait dit : même argent, deux états.
///
/// LE GRAPHIQUE EST DESSINÉ À LA MAIN, SANS BIBLIOTHÈQUE.
///
/// Sept barres et sept étiquettes. `fl_chart` apporterait des axes, des
/// info-bulles et des animations dont aucun n'est demandé, pour une dépendance
/// de plus dans une application qui doit rester légère sur des téléphones
/// d'entrée de gamme.
/// ═════════════════════════════════════════════════════════════════════════════
class EarningsScreen extends ConsumerWidget {
  const EarningsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        bottom: false,
        child: ListView(
          padding: const EdgeInsets.fromLTRB(20, 12, 20, 24),
          children: [
            Align(
              alignment: Alignment.centerLeft,
              child: Text(
                'Revenus',
                style: TextStyle(
                  fontSize: 26,
                  fontWeight: FontWeight.w800,
                  color: colors.ink,
                ),
              ),
            ),
            const SizedBox(height: 14),

            _BalanceCard(onWithdraw: () => context.push('/payout')),
            const SizedBox(height: 12),

            IntrinsicHeight(
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Expanded(
                    child: DriverStatTile(
                      label: 'Aujourd\'hui',
                      value: '${Format.amount(EarningsMockData.today)} F',
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: DriverStatTile(
                      label: 'Cette semaine',
                      value: '${Format.amount(EarningsMockData.week)} F',
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 12),

            const _WeekChart(),
            const SizedBox(height: 12),

            _MovementsLink(onTap: () => context.push('/earnings/detail')),
          ],
        ),
      ),
    );
  }
}

class _BalanceCard extends StatelessWidget {
  const _BalanceCard({required this.onWithdraw});

  final VoidCallback onWithdraw;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.fromLTRB(18, 16, 16, 16),
        decoration: BoxDecoration(
          color: AppTheme.brandGreen,
          borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'DISPONIBLE',
              style: TextStyle(
                fontSize: 10.5,
                fontWeight: FontWeight.w800,
                letterSpacing: 1,
                color: Colors.white.withValues(alpha: 0.7),
              ),
            ),
            const SizedBox(height: 6),
            Row(
              crossAxisAlignment: CrossAxisAlignment.baseline,
              textBaseline: TextBaseline.alphabetic,
              children: [
                Text(
                  Format.amount(EarningsMockData.available),
                  style: const TextStyle(
                    fontSize: 33,
                    fontWeight: FontWeight.w800,
                    color: Colors.white,
                  ),
                ),
                const SizedBox(width: 6),
                Text(
                  // « FCFA » collé, comme sur la maquette de cet écran — ailleurs
                  // c'est « F CFA ». Divergence de votre charte, reproduite ici :
                  // à unifier d'un côté ou de l'autre.
                  'FCFA',
                  style: TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w700,
                    color: Colors.white.withValues(alpha: 0.7),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 14),
            Divider(height: 1, color: Colors.white.withValues(alpha: 0.2)),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'EN ATTENTE',
                        style: TextStyle(
                          fontSize: 10,
                          fontWeight: FontWeight.w800,
                          letterSpacing: 0.9,
                          color: Colors.white.withValues(alpha: 0.7),
                        ),
                      ),
                      const SizedBox(height: 3),
                      Text(
                        '${Format.amount(EarningsMockData.pending)} F',
                        style: const TextStyle(
                          fontSize: 17,
                          fontWeight: FontWeight.w800,
                          color: Colors.white,
                        ),
                      ),
                    ],
                  ),
                ),
                FilledButton(
                  onPressed: onWithdraw,
                  style: FilledButton.styleFrom(
                    backgroundColor: Colors.white,
                    foregroundColor: AppTheme.charcoal,
                    minimumSize: const Size(0, AppTheme.minTapTarget),
                    padding: const EdgeInsets.symmetric(horizontal: 18),
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(24),
                    ),
                    textStyle: const TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                  child: const Text('Retirer mes gains'),
                ),
              ],
            ),
          ],
        ),
      );
}

class _WeekChart extends StatelessWidget {
  const _WeekChart();

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final days = EarningsMockData.weekByDay;
    final max = days.values.reduce((a, b) => a > b ? a : b);
    final lastKey = days.keys.last;

    return DriverCard(
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text(
                '7 derniers jours',
                style: TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.w800,
                  color: colors.ink,
                ),
              ),
              const Spacer(),
              Text(
                // Total CALCULÉ : il suivra la moindre correction d'un jour.
                'Total ${Format.amount(EarningsMockData.week)} F',
                style: TextStyle(fontSize: 12.5, color: colors.subtle),
              ),
            ],
          ),
          const SizedBox(height: 16),

          SizedBox(
            height: 110,
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                for (final entry in days.entries) ...[
                  if (entry.key != days.keys.first) const SizedBox(width: 8),
                  Expanded(
                    child: FractionallySizedBox(
                      heightFactor: entry.value / max,
                      child: Container(
                        decoration: BoxDecoration(
                          // Le jour en cours est plein, les autres pâles : on
                          // repère « où j'en suis » sans lire les étiquettes.
                          color: entry.key == lastKey
                              ? AppTheme.brandGreen
                              : AppTheme.brandGreenSoft,
                          borderRadius: const BorderRadius.vertical(
                            top: Radius.circular(6),
                          ),
                        ),
                      ),
                    ),
                  ),
                ],
              ],
            ),
          ),
          const SizedBox(height: 8),

          Row(
            children: [
              for (final key in days.keys) ...[
                if (key != days.keys.first) const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    key,
                    textAlign: TextAlign.center,
                    style: TextStyle(fontSize: 11, color: colors.subtle),
                  ),
                ),
              ],
            ],
          ),
        ],
      ),
    );
  }
}

class _MovementsLink extends StatelessWidget {
  const _MovementsLink({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(AppTheme.radiusCard),
      child: DriverCard(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 18),
        child: Row(
          children: [
            Expanded(
              child: Text(
                'Détail des mouvements',
                style: TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.w800,
                  color: colors.ink,
                ),
              ),
            ),
            Icon(Icons.chevron_right, size: 20, color: colors.subtle),
          ],
        ),
      ),
    );
  }
}
