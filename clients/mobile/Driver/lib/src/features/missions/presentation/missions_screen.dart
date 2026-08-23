import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/mock/mission_mock_data.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/driver_widgets.dart';
import '../../../shared/widgets/mission_widgets.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 16 — MISSIONS. « 4 onglets, cartes à 6 informations maximum. »
///
/// SIX INFORMATIONS, ET J'AI COMPTÉ.
///
/// Référence, univers, statut, trajet, distance-durée, gain. C'est la contrainte
/// que votre annotation pose, et elle est bonne : une septième obligerait à
/// réduire les cinq autres ou à allonger la carte, et une liste où l'on ne voit
/// que trois cartes à l'écran se parcourt mal en marchant.
///
/// La date n'y est donc PAS — elle appartient à l'historique, écran 17, dont
/// c'est justement le sujet.
///
/// VOTRE MAQUETTE MONTRE LES QUATRE STATUTS SOUS L'ONGLET « EN COURS ».
///
/// C'est un artefact de rendu : une mission livrée n'est pas en cours. Le filtre
/// est ici réel — sans quoi les quatre onglets afficheraient tous la même chose
/// et ne serviraient à rien.
/// ═════════════════════════════════════════════════════════════════════════════
class MissionsScreen extends ConsumerStatefulWidget {
  const MissionsScreen({super.key});

  @override
  ConsumerState<MissionsScreen> createState() => _MissionsScreenState();
}

class _MissionsScreenState extends ConsumerState<MissionsScreen> {
  MissionListStatus _tab = MissionListStatus.inProgress;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final visible = [
      for (final m in MissionMockData.missions)
        if (m.status == _tab) m,
    ];

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
                  'Missions',
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
                  for (final s in MissionListStatus.values)
                    Padding(
                      padding: const EdgeInsets.only(right: 8),
                      child: _Tab(
                        // Pluriel des onglets : « Disponibles », « Terminées ».
                        // « En cours » ne se pluralise pas ; « Livrée » devient
                        // « Terminées » sur la maquette, qui n'emploie donc pas
                        // le même mot que la pastille. Les deux sont conservés.
                        label: switch (s) {
                          MissionListStatus.inProgress => 'En cours',
                          MissionListStatus.available => 'Disponibles',
                          MissionListStatus.delivered => 'Terminées',
                          MissionListStatus.cancelled => 'Annulées',
                        },
                        selected: s == _tab,
                        onTap: () => setState(() => _tab = s),
                      ),
                    ),
                ],
              ),
            ),
            const SizedBox(height: 14),

            Expanded(
              child: visible.isEmpty
                  ? const _EmptyTab()
                  : ListView.separated(
                      padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
                      itemCount: visible.length,
                      separatorBuilder: (_, __) => const SizedBox(height: 12),
                      itemBuilder: (_, i) => _MissionCard(mission: visible[i]),
                    ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Tab extends StatelessWidget {
  const _Tab({required this.label, required this.selected, required this.onTap});

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

class _MissionCard extends StatelessWidget {
  const _MissionCard({required this.mission});

  final MockMission mission;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return DriverCard(
      padding: const EdgeInsets.all(14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text(
                mission.reference,
                style: TextStyle(
                  fontSize: 12.5,
                  fontWeight: FontWeight.w800,
                  color: colors.subtle,
                ),
              ),
              const SizedBox(width: 8),
              UniverseBadge(universe: mission.universe, compact: true),
              const Spacer(),
              MissionStatusPill(status: mission.status),
            ],
          ),
          const SizedBox(height: 10),

          Text(
            // « Chez Mama → Akpakpa » : la flèche dit le trajet en un caractère,
            // là où « de … vers … » prendrait deux lignes sur un téléphone.
            mission.route,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              fontSize: 16,
              fontWeight: FontWeight.w800,
              color: colors.ink,
            ),
          ),
          const SizedBox(height: 10),
          Divider(height: 1, color: colors.line),
          const SizedBox(height: 10),

          Row(
            children: [
              Expanded(
                child: Text(
                  '${Format.km(mission.distanceKm, upper: false)} · '
                  '${mission.durationMin} min',
                  style: TextStyle(fontSize: 12.5, color: colors.subtle),
                ),
              ),
              Text(
                // Une course annulée affiche « 0 F » plutôt que rien : le
                // déplacement a eu lieu, et l'absence de gain est l'information.
                '${Format.amount(mission.earning)} F',
                style: TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.w800,
                  color: mission.earning == 0
                      ? colors.subtle
                      : AppTheme.brandGreen,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _EmptyTab extends StatelessWidget {
  const _EmptyTab();

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 48),
      child: Column(
        children: [
          Icon(Icons.inbox_outlined, size: 34, color: colors.line),
          const SizedBox(height: 12),
          Text(
            'Aucune mission dans cet onglet.',
            textAlign: TextAlign.center,
            style: TextStyle(fontSize: 13.5, color: colors.subtle),
          ),
        ],
      ),
    );
  }
}
