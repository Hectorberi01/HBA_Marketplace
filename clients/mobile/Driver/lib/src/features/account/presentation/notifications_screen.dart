import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/mock/account_mock_data.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/driver_widgets.dart';
import '../../mission/presentation/stages/_stage_header.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 27 — NOTIFICATIONS. « Mission, paiement, document, bonus, commande prête. »
///
/// LA PASTILLE DE COULEUR REMPLACE UNE COLONNE D'ICÔNES.
///
/// Cinq catégories, cinq couleurs déjà employées ailleurs : vert pour une
/// opportunité, ambre pour ce qui attend, bleu pour l'argent, rouge pour ce qui
/// bloque. Un pictogramme par catégorie aurait demandé cinq dessins et un
/// apprentissage ; la couleur est déjà connue de qui a parcouru l'application.
///
/// AUCUNE N'EST MARQUÉE COMME LUE.
///
/// La maquette ne distingue pas lu et non-lu, et je ne l'invente pas — mais la
/// cloche de l'accueil porte une pastille rouge, ce qui suppose un compteur de
/// non-lus. L'un des deux écrans devra trancher.
/// ═════════════════════════════════════════════════════════════════════════════
class NotificationsScreen extends ConsumerWidget {
  const NotificationsScreen({super.key});

  static Color _tint(NotificationKind kind) => switch (kind) {
        NotificationKind.mission => AppTheme.brandGreen,
        NotificationKind.orderReady => AppTheme.amber,
        NotificationKind.payment => AppTheme.info,
        NotificationKind.document => AppTheme.danger,
        NotificationKind.bonus => AppTheme.brandGreen,
      };

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        bottom: false,
        child: ListView(
          children: [
            StageHeader(title: 'Notifications', onBack: () => context.pop()),
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
              child: Column(
                children: [
                  for (final n in AccountMockData.notifications) ...[
                    DriverCard(
                      padding: const EdgeInsets.symmetric(
                          horizontal: 14, vertical: 14),
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Padding(
                            // Alignée sur la première ligne de texte, pas au
                            // centre de la carte : deux lignes de titre
                            // décaleraient la pastille vers le bas.
                            padding: const EdgeInsets.only(top: 4),
                            child: Container(
                              width: 9,
                              height: 9,
                              decoration: BoxDecoration(
                                color: _tint(n.kind),
                                shape: BoxShape.circle,
                              ),
                            ),
                          ),
                          const SizedBox(width: 11),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  n.title,
                                  style: TextStyle(
                                    fontSize: 14.5,
                                    fontWeight: FontWeight.w800,
                                    height: 1.3,
                                    color: colors.ink,
                                  ),
                                ),
                                const SizedBox(height: 3),
                                Text(
                                  n.detail,
                                  style: TextStyle(
                                    fontSize: 12.5,
                                    color: colors.subtle,
                                  ),
                                ),
                              ],
                            ),
                          ),
                          const SizedBox(width: 8),
                          Text(
                            n.when,
                            style: TextStyle(
                              fontSize: 11.5,
                              color: colors.subtle,
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 10),
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
