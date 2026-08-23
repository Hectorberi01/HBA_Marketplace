import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/mock/account_mock_data.dart';
import '../../../core/mock/driver_mock_data.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/driver_widgets.dart';
import '../../mission/presentation/stages/_stage_header.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 26 — MES DOCUMENTS. « Statuts explicites : vérifié, en attente, expiré, refusé. »
///
/// LE BANDEAU DIT LA CONSÉQUENCE, PAS SEULEMENT L'ÉCHÉANCE.
///
/// « Mettez à jour votre assurance avant le 24 mai POUR CONTINUER À RECEVOIR DES
/// MISSIONS. » Sans la seconde moitié, l'avertissement se range mentalement
/// avec les rappels administratifs qu'on traite plus tard — et le livreur
/// découvre le blocage le matin où il ne reçoit plus rien.
///
/// LA DATE LIMITE EST CALCULÉE : 12 mai + 12 jours = 24 mai.
///
/// Vos écrans 19 et 26 concordent. Écrire « 24 mai » en dur aurait fait mentir
/// l'un des deux au premier changement de l'autre. Cf. `AccountMockData`.
///
/// MÊME LISTE QUE L'ÉCRAN 03, MAIS UN AUTRE MOMENT.
///
/// L'écran de vérification est en LECTURE : le dossier est en cours d'examen,
/// rien n'est attendu. Ici, chaque pièce porte « Mettre à jour ». Le premier
/// rassure, le second met au travail.
/// ═════════════════════════════════════════════════════════════════════════════
class DocumentsScreen extends ConsumerWidget {
  const DocumentsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);

    final expiring = [
      for (final d in DriverMockData.documents)
        if (d.status == DriverDocStatus.expiring) d,
    ];

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        bottom: false,
        child: ListView(
          children: [
            StageHeader(title: 'Mes documents', onBack: () => context.pop()),
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
              child: Column(
                children: [
                  if (expiring.isNotEmpty) ...[
                    Container(
                      width: double.infinity,
                      padding: const EdgeInsets.all(15),
                      decoration: BoxDecoration(
                        color: AppTheme.amberSoft,
                        borderRadius:
                            BorderRadius.circular(AppTheme.radiusCard),
                      ),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            // Le compte vient de la liste : deux documents qui
                            // expirent donneraient « 2 documents expirent
                            // bientôt » sans qu'on y touche.
                            '${expiring.length} document'
                            '${expiring.length > 1 ? 's expirent' : ' expire'} '
                            'bientôt',
                            style: TextStyle(
                              fontSize: 15,
                              fontWeight: FontWeight.w800,
                              color: colors.ink,
                            ),
                          ),
                          const SizedBox(height: 5),
                          Text(
                            AccountMockData.documentsWarning,
                            style: const TextStyle(
                              fontSize: 13,
                              height: 1.45,
                              color: AppTheme.amber,
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 12),
                  ],

                  for (final doc in DriverMockData.documents) ...[
                    _DocumentCard(document: doc),
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

class _DocumentCard extends StatelessWidget {
  const _DocumentCard({required this.document});

  final DriverDocument document;

  static (Color, Color) _tone(DriverDocStatus s, AppColors colors) =>
      switch (s) {
        DriverDocStatus.verified =>
          (AppTheme.brandGreen, AppTheme.brandGreenSoft),
        DriverDocStatus.expiring => (AppTheme.amber, AppTheme.amberSoft),
        DriverDocStatus.pending => (colors.subtle, colors.bg),
        DriverDocStatus.rejected => (AppTheme.danger, AppTheme.dangerSoft),
      };

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final (tint, wash) = _tone(document.status, colors);

    return DriverCard(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 13),
      child: Row(
        children: [
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              color: wash,
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
                    height: 1.25,
                    color: colors.ink,
                  ),
                ),
                const SizedBox(height: 5),
                Container(
                  padding: const EdgeInsets.symmetric(
                      horizontal: 8, vertical: 4),
                  decoration: BoxDecoration(
                    color: wash,
                    borderRadius: BorderRadius.circular(7),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Container(
                        width: 6,
                        height: 6,
                        decoration:
                            BoxDecoration(color: tint, shape: BoxShape.circle),
                      ),
                      const SizedBox(width: 5),
                      Flexible(
                        child: Text(
                          // L'accord suit le genre du document : cf.
                          // `DriverDocument.statusLabel`.
                          document.statusLabel,
                          style: TextStyle(
                            fontSize: 11.5,
                            fontWeight: FontWeight.w700,
                            color: tint,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: 10),
          OutlinedButton(
            onPressed: () {},
            style: OutlinedButton.styleFrom(
              minimumSize: const Size(0, 40),
              padding: const EdgeInsets.symmetric(horizontal: 12),
              side: BorderSide(color: colors.line),
              foregroundColor: colors.ink,
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(10),
              ),
              textStyle:
                  const TextStyle(fontSize: 12.5, fontWeight: FontWeight.w700),
            ),
            child: const Text('Mettre à jour'),
          ),
        ],
      ),
    );
  }
}
