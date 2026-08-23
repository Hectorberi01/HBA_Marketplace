import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/mock/support_mock_data.dart';
import '../../../core/theme/app_theme.dart';
import '../../mission/presentation/stages/_stage_header.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 21 — SIGNALER UN PROBLÈME.
///
/// « Bottom sheet, actions recommandées par type d'incident. »
///
/// ON CHOISIT LE PROBLÈME AVANT DE VOIR LA SOLUTION, ET C'EST L'INVERSE DE
///    CE QU'ON FAIT D'HABITUDE.
///
/// Pas de champ libre en tête, pas de « décrivez votre situation ». Sept
/// catégories, et la marche à suivre apparaît sous celle qu'on touche. Un
/// livreur bloqué devant un portail n'a ni le temps ni les mains pour rédiger —
/// et un texte libre n'aurait de toute façon reçu aucune réponse avant plusieurs
/// minutes.
///
/// LE BOUTON D'ÉCHEC EST ROUGE ET ARRIVE EN DERNIER, APRÈS LES TROIS ÉTAPES.
///
/// Déclarer un échec de livraison a des conséquences : remboursement du client,
/// course non payée, statistique du livreur. Le mettre au-dessus des étapes
/// ferait sauter l'appel et l'attente de cinq minutes que la plateforme demande
/// justement d'observer.
///
/// « AUTRE » N'A PAS DE BOUTON D'ÉCHEC.
///
/// Sinon il devient le raccourci universel pour abandonner une course sans motif
/// traçable — et la catégorie la plus utilisée de l'écran.
/// ═════════════════════════════════════════════════════════════════════════════
class IncidentScreen extends ConsumerStatefulWidget {
  const IncidentScreen({super.key});

  @override
  ConsumerState<IncidentScreen> createState() => _IncidentScreenState();
}

class _IncidentScreenState extends ConsumerState<IncidentScreen> {
  /// L'incident dont la marche à suivre est ouverte. La maquette montre
  /// « Client absent » déplié.
  IncidentType? _open = SupportMockData.incidents[1];

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        bottom: false,
        child: ListView(
          children: [
            StageHeader(
              title: 'Signaler un problème',
              onBack: () => context.pop(),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 4, 20, 24),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    SupportMockData.missionContext,
                    style: TextStyle(
                      fontSize: 13.5,
                      height: 1.45,
                      color: colors.subtle,
                    ),
                  ),
                  const SizedBox(height: 16),

                  for (final incident in SupportMockData.incidents) ...[
                    _IncidentRow(
                      incident: incident,
                      onTap: () => setState(
                        () => _open = _open == incident ? null : incident,
                      ),
                    ),
                    const SizedBox(height: 10),
                  ],

                  if (_open != null) ...[
                    const SizedBox(height: 6),
                    _Guidance(incident: _open!),
                  ],
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _IncidentRow extends StatelessWidget {
  const _IncidentRow({required this.incident, required this.onTap});

  final IncidentType incident;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(AppTheme.radiusCard),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 18),
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(AppTheme.radiusCard),
          border: Border.all(color: colors.line),
        ),
        child: Row(
          children: [
            Expanded(
              child: Text(
                incident.label,
                style: TextStyle(
                  fontSize: 15.5,
                  fontWeight: FontWeight.w700,
                  color: colors.ink,
                ),
              ),
            ),
            Icon(Icons.chevron_right, size: 19, color: colors.subtle),
          ],
        ),
      ),
    );
  }
}

/// La marche à suivre de l'incident choisi.
class _Guidance extends StatelessWidget {
  const _Guidance({required this.incident});

  final IncidentType incident;

  @override
  Widget build(BuildContext context) => Container(
        width: double.infinity,
        padding: const EdgeInsets.fromLTRB(16, 15, 16, 15),
        decoration: BoxDecoration(
          color: AppTheme.dangerSoft,
          borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${incident.label} — actions recommandées',
              style: const TextStyle(
                fontSize: 15,
                fontWeight: FontWeight.w800,
                color: AppTheme.danger,
              ),
            ),
            const SizedBox(height: 12),

            for (var i = 0; i < incident.steps.length; i++) ...[
              if (i > 0) const SizedBox(height: 8),
              Text(
                // Étapes NUMÉROTÉES, pas à puces : l'ordre compte. Appeler avant
                // d'attendre, attendre avant de déclarer l'échec.
                '${i + 1}. ${incident.steps[i]}',
                style: const TextStyle(
                  fontSize: 13.5,
                  height: 1.45,
                  color: AppTheme.danger,
                ),
              ),
            ],

            if (incident.failureAction != null) ...[
              const SizedBox(height: 16),
              FilledButton(
                onPressed: () {},
                style: FilledButton.styleFrom(
                  minimumSize:
                      const Size.fromHeight(AppTheme.primaryButtonHeight),
                  backgroundColor: AppTheme.danger,
                  foregroundColor: Colors.white,
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(AppTheme.radiusField),
                  ),
                  textStyle: const TextStyle(
                    fontSize: 14.5,
                    fontWeight: FontWeight.w800,
                  ),
                ),
                child: Text(incident.failureAction!),
              ),
              const SizedBox(height: 8),
              Center(
                child: Text(
                  SupportMockData.rulesNote,
                  style: TextStyle(
                    fontSize: 11.5,
                    color: AppTheme.danger.withValues(alpha: 0.75),
                  ),
                ),
              ),
            ],

            if (!incident.drawnByDesign) ...[
              const SizedBox(height: 10),
              Text(
                // MENTION VISIBLE À L'ÉCRAN, PAS SEULEMENT EN COMMENTAIRE.
                //
                // Seul « Client absent » est écrit par le design. Les six autres
                // marches à suivre sont déduites : un conseil erroné sur un colis
                // endommagé engage plus qu'un écran mal aligné. Cette ligne
                // disparaîtra quand l'exploitation aura relu.
                'Marche à suivre proposée — à valider par l\'exploitation.',
                style: TextStyle(
                  fontSize: 11,
                  fontStyle: FontStyle.italic,
                  color: AppTheme.danger.withValues(alpha: 0.7),
                ),
              ),
            ],
          ],
        ),
      );
}
