import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/mock/account_mock_data.dart';
import '../../../core/mock/driver_state.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/driver_widgets.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 24 — COMPTE. « Point d'entrée vers véhicule, documents, évaluations, sécurité. »
///
/// LA NOTE EST DÉTAILLÉE EN TROIS AXES, ET C'EST PLUS UTILE QU'UN 4,9.
///
/// « 4,9 ★ » ne dit pas quoi améliorer. Rapidité, communication, respect du
/// colis : un livreur qui perd des points saura lequel. C'est aussi ce qui rend
/// la note actionnable plutôt que subie.
///
/// « DEPUIS MARS 2025 · 1 284 LIVRAISONS » EST UNE FIERTÉ, PAS UNE STATISTIQUE.
///
/// Sur ce métier, l'ancienneté et le volume sont ce qu'on met en avant. Les
/// enterrer dans un sous-écran priverait la page de la seule chose qui la rende
/// agréable à ouvrir.
/// ═════════════════════════════════════════════════════════════════════════════
class AccountScreen extends ConsumerWidget {
  const AccountScreen({super.key});

  /// Les destinations réellement câblées. Les autres sont inertes et signalées.
  static const Map<String, String> _routes = {
    'Mon véhicule': '/vehicle',
    'Mes documents': '/documents',
    'Mes revenus': '/earnings',
    'Notifications': '/notifications',
    'Aide': '/support',
  };

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
                'Compte',
                style: TextStyle(
                  fontSize: 26,
                  fontWeight: FontWeight.w800,
                  color: colors.ink,
                ),
              ),
            ),
            const SizedBox(height: 14),

            const _ProfileCard(),
            const SizedBox(height: 12),

            IntrinsicHeight(
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Expanded(
                    child: DriverStatTile(
                      label: 'Votre note',
                      value: '${AccountMockData.rating} ★',
                      caption: '${AccountMockData.ratingCount} évaluations',
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: DriverStatTile(
                      label: 'Depuis',
                      value: AccountMockData.memberSince,
                      caption:
                          '${_thousands(AccountMockData.totalDeliveries)} livraisons',
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 12),

            const _RatingsCard(),
            const SizedBox(height: 12),

            DriverCard(
              padding: EdgeInsets.zero,
              child: Column(
                children: [
                  for (var i = 0;
                      i < AccountMockData.accountLinks.length;
                      i++) ...[
                    if (i > 0) Divider(height: 1, color: colors.line),
                    _LinkRow(
                      label: AccountMockData.accountLinks[i],
                      route: _routes[AccountMockData.accountLinks[i]],
                    ),
                  ],
                ],
              ),
            ),
            const SizedBox(height: 16),

            // ROUGE PÂLE, PAS ROUGE PLEIN.
            //
            // La déconnexion est une action banale qu'on ne veut pas frôler par
            // erreur, mais elle ne détruit rien. Un bouton rouge plein la
            // mettrait au même niveau que « Je ne peux pas effectuer la
            // livraison », qui, elle, a des conséquences.
            OutlinedButton(
              onPressed: () {
                ref.read(driverSignedInProvider.notifier).state = false;
                context.go('/login');
              },
              style: OutlinedButton.styleFrom(
                minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
                backgroundColor: AppTheme.dangerSoft,
                side: BorderSide(color: AppTheme.danger.withValues(alpha: 0.2)),
                foregroundColor: AppTheme.danger,
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(AppTheme.radiusField),
                ),
                textStyle: const TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.w700,
                ),
              ),
              child: const Text('Déconnexion'),
            ),
          ],
        ),
      ),
    );
  }

  /// « 1 284 » — espace fine, comme partout ailleurs dans l'application.
  static String _thousands(int value) {
    final digits = value.toString();
    final buffer = StringBuffer();
    for (var i = 0; i < digits.length; i++) {
      if (i > 0 && (digits.length - i) % 3 == 0) buffer.write(' ');
      buffer.write(digits[i]);
    }
    return buffer.toString();
  }
}

class _ProfileCard extends StatelessWidget {
  const _ProfileCard();

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return DriverCard(
      padding: const EdgeInsets.all(16),
      child: Row(
        children: [
          Container(
            width: 52,
            height: 52,
            alignment: Alignment.center,
            decoration: const BoxDecoration(
              color: AppTheme.brandGreenSoft,
              shape: BoxShape.circle,
            ),
            child: const Text(
              'HA',
              style: TextStyle(
                fontSize: 16,
                fontWeight: FontWeight.w800,
                color: AppTheme.brandGreen,
              ),
            ),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  AccountMockData.fullName,
                  style: TextStyle(
                    fontSize: 18,
                    fontWeight: FontWeight.w800,
                    color: colors.ink,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  AccountMockData.phone,
                  style: TextStyle(fontSize: 13, color: colors.subtle),
                ),
                Text(
                  AccountMockData.email,
                  style: TextStyle(fontSize: 13, color: colors.subtle),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _RatingsCard extends StatelessWidget {
  const _RatingsCard();

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return DriverCard(
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Évaluations',
            style: TextStyle(
              fontSize: 15,
              fontWeight: FontWeight.w800,
              color: colors.ink,
            ),
          ),
          const SizedBox(height: 12),
          for (final facet in AccountMockData.facets) ...[
            Padding(
              padding: const EdgeInsets.only(bottom: 10),
              child: Row(
                children: [
                  SizedBox(
                    width: 118,
                    child: Text(
                      facet.label,
                      style: TextStyle(fontSize: 13, color: colors.subtle),
                    ),
                  ),
                  Expanded(
                    child: ClipRRect(
                      borderRadius: BorderRadius.circular(3),
                      child: LinearProgressIndicator(
                        value: facet.score,
                        minHeight: 6,
                        backgroundColor: colors.line,
                        valueColor:
                            const AlwaysStoppedAnimation(AppTheme.brandGreen),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ],
        ],
      ),
    );
  }
}

class _LinkRow extends StatelessWidget {
  const _LinkRow({required this.label, required this.route});

  final String label;
  final String? route;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final wired = route != null;

    return InkWell(
      // INERTE PLUTÔT QUE MASQUÉ pour « Mes évaluations » et « Sécurité ».
      //
      // Les retirer donnerait une fausse idée de l'écran fini ; les câbler vers
      // une page vide serait pire.
      onTap: wired
          ? () {
              if (route == '/earnings') {
                GoRouter.of(context).go(route!);
              } else {
                GoRouter.of(context).push(route!);
              }
            }
          : null,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 17),
        child: Row(
          children: [
            Expanded(
              child: Text(
                label,
                style: TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.w600,
                  color: wired ? colors.ink : colors.subtle,
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
