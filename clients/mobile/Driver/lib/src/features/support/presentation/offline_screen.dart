import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/mock/mission_mock_data.dart';
import '../../../core/mock/support_mock_data.dart';
import '../../../core/theme/app_theme.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 22 — CONNEXION PERDUE.
///
/// « La mission active reste consultable, les actions sont mises en file. »
///
/// CET ÉCRAN N'EST PAS UNE ERREUR, C'EST UN MODE DE FONCTIONNEMENT.
///
/// Il ne dit pas « une erreur est survenue » mais « votre mission reste
/// disponible ». La différence est tout : au Bénin, la couverture tombe
/// régulièrement, et un livreur qui perd le réseau à mi-course a besoin de
/// l'adresse et du numéro, pas d'un écran de panne.
///
/// LES ACTIONS SONT NOMMÉES DANS LA FILE, PAS SEULEMENT COMPTÉES.
///
/// « 1 action en attente » ne dit pas laquelle. « "Je suis arrivé" sera envoyé
/// automatiquement » dit ce que le serveur ignore encore — et évite qu'on
/// retouche le bouton trois fois en croyant que rien n'a été pris.
///
/// FOND SOMBRE, ET CE N'EST PAS DÉCORATIF.
///
/// L'écran change entièrement d'apparence pour qu'on comprenne sans lire qu'on
/// n'est plus dans le fonctionnement normal. Un simple bandeau rouge en haut
/// d'un écran habituel se serait fait ignorer au bout de deux fois.
/// ═════════════════════════════════════════════════════════════════════════════
class OfflineScreen extends ConsumerWidget {
  const OfflineScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) => Scaffold(
        backgroundColor: const Color(0xFF10181F),
        body: SafeArea(
          child: ListView(
            padding: const EdgeInsets.fromLTRB(20, 16, 20, 24),
            children: [
              Container(
                padding:
                    const EdgeInsets.symmetric(horizontal: 14, vertical: 13),
                decoration: BoxDecoration(
                  color: AppTheme.danger.withValues(alpha: 0.18),
                  borderRadius: BorderRadius.circular(AppTheme.radiusField),
                ),
                child: Row(
                  children: [
                    Container(
                      width: 8,
                      height: 8,
                      decoration: const BoxDecoration(
                        color: AppTheme.danger,
                        shape: BoxShape.circle,
                      ),
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Text(
                        SupportMockData.offlineBanner,
                        style: TextStyle(
                          fontSize: 13,
                          height: 1.35,
                          fontWeight: FontWeight.w600,
                          color: AppTheme.danger.withValues(alpha: 0.95),
                        ),
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 22),

              const Text(
                SupportMockData.offlineTitle,
                style: TextStyle(
                  fontSize: 25,
                  fontWeight: FontWeight.w800,
                  height: 1.25,
                  color: Colors.white,
                ),
              ),
              const SizedBox(height: 8),
              Text(
                SupportMockData.offlineBody,
                style: TextStyle(
                  fontSize: 13.5,
                  height: 1.5,
                  color: Colors.white.withValues(alpha: 0.6),
                ),
              ),
              const SizedBox(height: 18),

              const _CachedMissionCard(),
              const SizedBox(height: 12),
              const _QueueCard(),
              const SizedBox(height: 26),

              FilledButton(
                onPressed: () => context.pop(),
                style: FilledButton.styleFrom(
                  minimumSize:
                      const Size.fromHeight(AppTheme.primaryButtonHeight),
                  backgroundColor: Colors.white.withValues(alpha: 0.12),
                  foregroundColor: Colors.white,
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(AppTheme.radiusField),
                  ),
                  textStyle: const TextStyle(
                    fontSize: 15.5,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                child: const Text('Réessayer maintenant'),
              ),
            ],
          ),
        ),
      );
}

/// La mission conservée sur l'appareil.
///
/// « APPELER » ET « ITINÉRAIRE » RESTENT ACTIFS — ILS NE PASSENT PAS PAR HBA.
///
/// L'appel emprunte le réseau téléphonique, l'itinéraire une carte hors ligne.
/// Les griser avec le reste priverait le livreur des deux seules choses qui
/// fonctionnent encore.
class _CachedMissionCard extends StatelessWidget {
  const _CachedMissionCard();

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          color: Colors.white.withValues(alpha: 0.06),
          borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Text(
                  MissionMockData.reference,
                  style: TextStyle(
                    fontSize: 12.5,
                    fontWeight: FontWeight.w800,
                    color: Colors.white.withValues(alpha: 0.6),
                  ),
                ),
                const Spacer(),
                Container(
                  padding: const EdgeInsets.symmetric(
                      horizontal: 9, vertical: 4),
                  decoration: BoxDecoration(
                    color: AppTheme.amber.withValues(alpha: 0.22),
                    borderRadius: BorderRadius.circular(7),
                  ),
                  child: const Text(
                    'EN LIVRAISON',
                    style: TextStyle(
                      fontSize: 10,
                      fontWeight: FontWeight.w800,
                      letterSpacing: 0.6,
                      color: AppTheme.amber,
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 10),
            Text(
              MissionMockData.dropoffAddress.split(' — ').first,
              style: const TextStyle(
                fontSize: 18,
                fontWeight: FontWeight.w800,
                color: Colors.white,
              ),
            ),
            const SizedBox(height: 4),
            Text(
              '« ${MissionMockData.dropoffInstruction} »',
              style: TextStyle(
                fontSize: 12.5,
                fontStyle: FontStyle.italic,
                color: Colors.white.withValues(alpha: 0.55),
              ),
            ),
            const SizedBox(height: 14),
            Row(
              children: [
                Expanded(child: _DarkButton(label: 'Appeler', onTap: () {})),
                const SizedBox(width: 10),
                Expanded(child: _DarkButton(label: 'Itinéraire', onTap: () {})),
              ],
            ),
          ],
        ),
      );
}

class _DarkButton extends StatelessWidget {
  const _DarkButton({required this.label, required this.onTap});

  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(AppTheme.radiusField),
        child: Container(
          height: AppTheme.primaryButtonHeight,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: Colors.white.withValues(alpha: 0.10),
            borderRadius: BorderRadius.circular(AppTheme.radiusField),
          ),
          child: Text(
            label,
            style: const TextStyle(
              fontSize: 14.5,
              fontWeight: FontWeight.w700,
              color: Colors.white,
            ),
          ),
        ),
      );
}

class _QueueCard extends StatelessWidget {
  const _QueueCard();

  @override
  Widget build(BuildContext context) {
    final queued = SupportMockData.queuedActions;

    return Container(
      padding: const EdgeInsets.all(15),
      decoration: BoxDecoration(
        color: AppTheme.amber.withValues(alpha: 0.14),
        borderRadius: BorderRadius.circular(AppTheme.radiusCard),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 22,
            height: 22,
            child: CircularProgressIndicator(
              strokeWidth: 2.2,
              color: AppTheme.amber,
              backgroundColor: AppTheme.amber.withValues(alpha: 0.25),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  // L'accord suit le nombre d'actions en file.
                  '${queued.length} '
                  '${queued.length > 1 ? 'actions en attente' : 'action en attente'} '
                  'de synchronisation',
                  style: const TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w800,
                    height: 1.3,
                    color: AppTheme.amber,
                  ),
                ),
                const SizedBox(height: 4),
                for (final action in queued)
                  Text(
                    '« $action » sera envoyé automatiquement.',
                    style: TextStyle(
                      fontSize: 12.5,
                      height: 1.35,
                      color: AppTheme.amber.withValues(alpha: 0.85),
                    ),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
