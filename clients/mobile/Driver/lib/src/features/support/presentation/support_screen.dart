import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/mock/support_mock_data.dart';
import '../../../core/theme/app_theme.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 23 — ASSISTANCE. « Accessible pendant la mission sans encombrer l'écran. »
///
/// L'URGENCE EST EN HAUT, PLEINE, ROUGE, ET SEULE DANS SA CARTE.
///
/// « Accident, agression, danger immédiat. » Ce n'est pas une catégorie de
/// support parmi d'autres : c'est le seul cas où chaque seconde compte, et le
/// seul bouton qu'on cherchera sans regarder l'écran. Le ranger avec « Problème
/// de paiement » le rendrait introuvable au moment précis où il sert.
///
/// « VOTRE POSITION EST TRANSMISE À HBA » EST DIT AVANT L'APPEL.
///
/// C'est une donnée personnelle envoyée sans nouvelle autorisation. L'annoncer
/// après coup — ou pas du tout — serait un problème, même quand l'intention est
/// bonne.
///
/// FOND SOMBRE, COMME L'ÉCRAN HORS LIGNE.
///
/// Les deux écrans de crise partagent la même apparence : on sait qu'on n'est
/// plus dans le fonctionnement normal avant d'avoir lu quoi que ce soit.
/// ═════════════════════════════════════════════════════════════════════════════
class SupportScreen extends ConsumerWidget {
  const SupportScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) => Scaffold(
        backgroundColor: const Color(0xFF10181F),
        body: SafeArea(
          bottom: false,
          child: Column(
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(10, 6, 20, 6),
                child: Row(
                  children: [
                    InkWell(
                      onTap: () => context.pop(),
                      borderRadius: BorderRadius.circular(24),
                      child: Container(
                        width: AppTheme.minTapTarget,
                        height: AppTheme.minTapTarget,
                        alignment: Alignment.center,
                        decoration: BoxDecoration(
                          color: Colors.white.withValues(alpha: 0.10),
                          shape: BoxShape.circle,
                        ),
                        child: const Icon(
                          Icons.chevron_left,
                          size: 22,
                          color: Colors.white,
                        ),
                      ),
                    ),
                    const SizedBox(width: 12),
                    const Text(
                      'Assistance',
                      style: TextStyle(
                        fontSize: 17,
                        fontWeight: FontWeight.w800,
                        color: Colors.white,
                      ),
                    ),
                  ],
                ),
              ),

              Expanded(
                child: ListView(
                  padding: const EdgeInsets.fromLTRB(20, 8, 20, 20),
                  children: [
                    const _EmergencyCard(),
                    const SizedBox(height: 14),
                    for (final topic in SupportMockData.supportTopics) ...[
                      _TopicRow(label: topic),
                      const SizedBox(height: 10),
                    ],
                  ],
                ),
              ),

              Padding(
                padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
                child: Container(
                  width: double.infinity,
                  padding: const EdgeInsets.symmetric(vertical: 14),
                  decoration: BoxDecoration(
                    color: Colors.white.withValues(alpha: 0.06),
                    borderRadius: BorderRadius.circular(AppTheme.radiusCard),
                  ),
                  child: Column(
                    children: [
                      Text(
                        SupportMockData.supportHours,
                        style: TextStyle(
                          fontSize: 12.5,
                          color: Colors.white.withValues(alpha: 0.65),
                        ),
                      ),
                      const SizedBox(height: 3),
                      Text(
                        SupportMockData.supportPhone,
                        style: TextStyle(
                          fontSize: 12.5,
                          fontWeight: FontWeight.w700,
                          color: Colors.white.withValues(alpha: 0.85),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ),
        ),
      );
}

class _EmergencyCard extends StatelessWidget {
  const _EmergencyCard();

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.fromLTRB(18, 18, 18, 18),
        decoration: BoxDecoration(
          color: AppTheme.danger,
          borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        ),
        child: Column(
          children: [
            const Text(
              'Urgence',
              style: TextStyle(
                fontSize: 21,
                fontWeight: FontWeight.w800,
                color: Colors.white,
              ),
            ),
            const SizedBox(height: 6),
            Text(
              SupportMockData.emergencyBody,
              textAlign: TextAlign.center,
              style: TextStyle(
                fontSize: 13,
                height: 1.45,
                color: Colors.white.withValues(alpha: 0.92),
              ),
            ),
            const SizedBox(height: 16),
            FilledButton(
              onPressed: () {},
              style: FilledButton.styleFrom(
                minimumSize:
                    const Size.fromHeight(AppTheme.primaryButtonHeight),
                backgroundColor: Colors.white,
                foregroundColor: AppTheme.danger,
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(28),
                ),
                textStyle: const TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.w800,
                ),
              ),
              child: const Text('Appeler l\'assistance HBA'),
            ),
          ],
        ),
      );
}

class _TopicRow extends StatelessWidget {
  const _TopicRow({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) => InkWell(
        onTap: () {},
        borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 18),
          decoration: BoxDecoration(
            color: Colors.white.withValues(alpha: 0.06),
            borderRadius: BorderRadius.circular(AppTheme.radiusCard),
          ),
          child: Row(
            children: [
              Expanded(
                child: Text(
                  label,
                  style: const TextStyle(
                    fontSize: 15,
                    fontWeight: FontWeight.w600,
                    color: Colors.white,
                  ),
                ),
              ),
              Icon(
                Icons.chevron_right,
                size: 19,
                color: Colors.white.withValues(alpha: 0.5),
              ),
            ],
          ),
        ),
      );
}
