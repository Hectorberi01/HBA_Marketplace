import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/mock/driver_mock_data.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/driver_widgets.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 03 — VÉRIFICATION DES DOCUMENTS. Écran d'attente après dépôt du dossier.
///
/// IL ANNONCE UN DÉLAI. C'EST TOUT CE QUI COMPTE ICI.
///
/// « généralement sous 48 h » : sans cette phrase, l'écran ne dit que « on
/// regarde », et quelqu'un qui a besoin de travailler rouvre l'application dix
/// fois dans la journée, puis appelle le support. Le délai est la seule
/// information qui remplace l'attente par une prévision.
///
/// PAS DE BOUTON PRINCIPAL, ET C'EST DÉLIBÉRÉ.
///
/// Rien n'est attendu du livreur à ce stade — la balle est chez HBA. « Contacter
/// le support » est en contour fin : disponible, jamais suggéré. Un gros bouton
/// vert aurait laissé croire qu'il y a un geste pour accélérer.
///
/// AUCUN CHEMIN VERS LE DASHBOARD DEPUIS CET ÉCRAN.
///
/// Un dossier non validé ne roule pas. Y ajouter une sortie « Continuer quand
/// même » ferait entrer dans l'application quelqu'un qui ne peut recevoir aucune
/// mission — et qui conclurait que l'application ne fonctionne pas.
/// ═════════════════════════════════════════════════════════════════════════════
class DriverVerificationScreen extends ConsumerWidget {
  const DriverVerificationScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);

    return Scaffold(
      backgroundColor: colors.surface,
      body: SafeArea(
        child: Column(
          children: [
            Expanded(
              child: ListView(
                padding: const EdgeInsets.fromLTRB(22, 18, 22, 8),
                children: [
                  const Center(child: _WaitingHalo()),
                  const SizedBox(height: 22),

                  Text(
                    'Vérification en cours',
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      fontSize: 24,
                      fontWeight: FontWeight.w800,
                      color: colors.ink,
                    ),
                  ),
                  const SizedBox(height: 8),
                  Text(
                    DriverMockData.verificationDelay,
                    textAlign: TextAlign.center,
                    style: TextStyle(fontSize: 14, height: 1.45, color: colors.subtle),
                  ),
                  const SizedBox(height: 22),

                  for (final doc in DriverMockData.documents) ...[
                    _DocumentRow(document: doc),
                    const SizedBox(height: 10),
                  ],
                ],
              ),
            ),

            Padding(
              padding: const EdgeInsets.fromLTRB(22, 8, 22, 18),
              child: DriverSecondaryButton(
                label: 'Contacter le support',
                onPressed: () => context.push('/support'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Sablier posé sur deux cercles ambrés concentriques.
///
/// L'ambre plutôt que le vert : rien n'est acquis, on attend. Le vert aurait
/// annoncé une validation qui n'a pas eu lieu.
class _WaitingHalo extends StatelessWidget {
  const _WaitingHalo();

  @override
  Widget build(BuildContext context) => Container(
        width: 170,
        height: 170,
        alignment: Alignment.center,
        decoration: const BoxDecoration(
          color: AppTheme.amberSoft,
          shape: BoxShape.circle,
        ),
        child: Container(
          width: 74,
          height: 74,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: AppTheme.amberSoft.withValues(alpha: 0.9),
            shape: BoxShape.circle,
            border: Border.all(color: AppTheme.amber.withValues(alpha: 0.25)),
          ),
          child: const Icon(
            Icons.hourglass_empty_rounded,
            size: 30,
            color: AppTheme.amber,
          ),
        ),
      );
}

class _DocumentRow extends StatelessWidget {
  const _DocumentRow({required this.document});

  final DriverDocument document;

  /// Couleur du texte d'état et de la vignette.
  ///
  /// « EN ATTENTE » EST GRIS, PAS AMBRE.
  ///
  /// L'ambre dit « quelque chose est attendu de vous ». Une pièce que HBA n'a
  /// pas encore examinée n'attend rien du livreur — l'ambrer l'inviterait à
  /// renvoyer un document déjà transmis.
  static Color _tint(DriverDocStatus status, AppColors colors) => switch (status) {
        DriverDocStatus.verified => AppTheme.brandGreen,
        DriverDocStatus.expiring => AppTheme.amber,
        DriverDocStatus.pending => colors.subtle,
        DriverDocStatus.rejected => AppTheme.danger,
      };

  static Color _wash(DriverDocStatus status, AppColors colors) => switch (status) {
        DriverDocStatus.verified => AppTheme.brandGreenSoft,
        DriverDocStatus.expiring => AppTheme.amberSoft,
        DriverDocStatus.pending => colors.bg,
        DriverDocStatus.rejected => AppTheme.dangerSoft,
      };

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final tint = _tint(document.status, colors);

    return DriverCard(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 13),
      child: Row(
        children: [
          // Vignette pleine, sans icône : la maquette ne montre qu'un aplat de
          // couleur. Y placer un pictogramme par type de pièce demanderait
          // quatre icônes que rien ne définit.
          Container(
            width: 38,
            height: 38,
            decoration: BoxDecoration(
              color: _wash(document.status, colors),
              borderRadius: BorderRadius.circular(10),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  document.name,
                  style: TextStyle(
                    fontSize: 15,
                    fontWeight: FontWeight.w700,
                    color: colors.ink,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  // L'accord suit le genre du document : « Permis vérifié »,
                  // « Carte d'identité vérifiée ». Cf. `DriverDocument`.
                  document.statusLabel,
                  style: TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w600,
                    color: tint,
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
