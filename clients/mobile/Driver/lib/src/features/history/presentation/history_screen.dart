import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/mock/mission_mock_data.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/driver_widgets.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 17 — HISTORIQUE. « Filtres période, liste dense mais lisible. »
///
/// LE TOTAL EST CALCULÉ À PARTIR DES LIGNES AFFICHÉES.
///
/// Votre maquette annonçait « 68 000 F CFA · 42 courses » au-dessus de cinq
/// lignes qui n'y menaient pas, et le tableau de bord donnait 15 500 F pour
/// 8 livraisons le même jour. Trois chiffres, aucune concordance.
///
/// Le total est donc DÉRIVÉ. Un livreur additionne ses lignes — c'est son
/// argent — et un écart entre la somme et le titre lui coûte sa confiance dans
/// tout le reste de l'écran.
///
/// UNE COURSE ANNULÉE RESTE DANS LA LISTE, HORS DU COMPTE.
///
/// Elle a eu lieu du point de vue du livreur — il s'est déplacé — mais n'a rien
/// rapporté. La masquer effacerait un déplacement non payé ; la compter dans les
/// courses gonflerait la statistique dont dépend le bonus du jour.
/// ═════════════════════════════════════════════════════════════════════════════
class HistoryScreen extends ConsumerStatefulWidget {
  const HistoryScreen({super.key});

  @override
  ConsumerState<HistoryScreen> createState() => _HistoryScreenState();
}

enum _Period {
  today('Aujourd\'hui'),
  week('7 jours'),
  month('30 jours'),
  custom('Perso.');

  const _Period(this.label);

  final String label;
}

class _HistoryScreenState extends ConsumerState<HistoryScreen> {
  _Period _period = _Period.today;

  /// SEULE « AUJOURD'HUI » FILTRE RÉELLEMENT.
  ///
  /// Les données figées ne couvrent que deux jours. « 7 jours » et « 30 jours »
  /// rendent donc la même liste, et « Perso. » n'ouvre aucun sélecteur de dates.
  /// Le signaler vaut mieux que de faire croire à quatre périodes distinctes.
  List<MissionHistoryEntry> get _entries => switch (_period) {
        _Period.today => [
            for (final e in MissionMockData.history)
              if (e.isToday) e,
          ],
        _ => MissionMockData.history,
      };

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final entries = _entries;

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        bottom: false,
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 12, 20, 0),
              child: Align(
                alignment: Alignment.centerLeft,
                child: Text(
                  'Historique',
                  style: TextStyle(
                    fontSize: 26,
                    fontWeight: FontWeight.w800,
                    color: colors.ink,
                  ),
                ),
              ),
            ),
            const SizedBox(height: 14),

            SizedBox(
              height: 38,
              child: ListView(
                scrollDirection: Axis.horizontal,
                padding: const EdgeInsets.symmetric(horizontal: 20),
                children: [
                  for (final p in _Period.values)
                    Padding(
                      padding: const EdgeInsets.only(right: 8),
                      child: _Chip(
                        label: p.label,
                        selected: p == _period,
                        onTap: () => setState(() => _period = p),
                      ),
                    ),
                ],
              ),
            ),
            const SizedBox(height: 14),

            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 20),
              child: _TotalCard(period: _period.label, entries: entries),
            ),
            const SizedBox(height: 12),

            Expanded(
              child: ListView.separated(
                padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
                itemCount: entries.length,
                separatorBuilder: (_, __) => const SizedBox(height: 8),
                itemBuilder: (_, i) => _HistoryRow(entry: entries[i]),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Chip extends StatelessWidget {
  const _Chip({required this.label, required this.selected, required this.onTap});

  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(20),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 9),
        decoration: BoxDecoration(
          color: selected ? AppTheme.charcoal : colors.surface,
          borderRadius: BorderRadius.circular(20),
          border: Border.all(color: selected ? AppTheme.charcoal : colors.line),
        ),
        child: Text(
          label,
          style: TextStyle(
            fontSize: 13,
            fontWeight: FontWeight.w700,
            color: selected ? Colors.white : colors.ink,
          ),
        ),
      ),
    );
  }
}

class _TotalCard extends StatelessWidget {
  const _TotalCard({required this.period, required this.entries});

  final String period;
  final List<MissionHistoryEntry> entries;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.fromLTRB(16, 14, 16, 15),
        decoration: BoxDecoration(
          color: AppTheme.charcoal,
          borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'TOTAL · $period',
                    style: TextStyle(
                      fontSize: 10,
                      fontWeight: FontWeight.w800,
                      letterSpacing: 0.9,
                      color: Colors.white.withValues(alpha: 0.6),
                    ),
                  ),
                  const SizedBox(height: 6),
                  Text(
                    Format.cfaAmount(MissionMockData.totalFor(entries)),
                    style: const TextStyle(
                      fontSize: 24,
                      fontWeight: FontWeight.w800,
                      color: Colors.white,
                    ),
                  ),
                ],
              ),
            ),
            Column(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Text(
                  'COURSES',
                  style: TextStyle(
                    fontSize: 10,
                    fontWeight: FontWeight.w800,
                    letterSpacing: 0.9,
                    color: Colors.white.withValues(alpha: 0.6),
                  ),
                ),
                const SizedBox(height: 6),
                Text(
                  '${MissionMockData.countFor(entries)}',
                  style: const TextStyle(
                    fontSize: 24,
                    fontWeight: FontWeight.w800,
                    color: Colors.white,
                  ),
                ),
              ],
            ),
          ],
        ),
      );
}

class _HistoryRow extends StatelessWidget {
  const _HistoryRow({required this.entry});

  final MissionHistoryEntry entry;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final cancelled = entry.status == MissionListStatus.cancelled;

    return DriverCard(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Text(
                      entry.reference,
                      style: TextStyle(
                        fontSize: 13.5,
                        fontWeight: FontWeight.w800,
                        color: colors.ink,
                      ),
                    ),
                    const SizedBox(width: 8),
                    Flexible(
                      child: Text(
                        entry.universeLabel,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(fontSize: 12, color: colors.subtle),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 2),
                Text(
                  '${entry.dayLabel} · ${entry.time}',
                  style: TextStyle(fontSize: 12, color: colors.subtle),
                ),
              ],
            ),
          ),
          const SizedBox(width: 10),
          Column(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Text(
                // Le « + » n'apparaît que sur un gain réel : « +0 F » se lirait
                // comme un crédit nul, alors que c'est une course sans revenu.
                cancelled
                    ? '0 F'
                    : '+${Format.amount(entry.amount)} F',
                style: TextStyle(
                  fontSize: 14.5,
                  fontWeight: FontWeight.w800,
                  color: cancelled ? colors.subtle : AppTheme.brandGreen,
                ),
              ),
              const SizedBox(height: 2),
              Text(
                entry.status.label,
                style: TextStyle(
                  fontSize: 11.5,
                  color: cancelled ? AppTheme.danger : colors.subtle,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
