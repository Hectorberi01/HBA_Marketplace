import 'package:flutter/material.dart';

import '../../../core/network/not_migrated.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// LITIGE — SANS AMONT.
///
/// LE MODULE DISPUTES N'A JAMAIS ÉTÉ EXTRAIT DU MONOLITHE.
///
/// L'écran parlait à `/seller/disputes/{id}` et `/seller/disputes/{id}/reply`.
/// Aucun service HBA ne porte le litige ni son fil de discussion, et la
/// passerelle n'a aucune route vers lui. Même constat côté acheteur, où l'app
/// Client range `disputes` parmi ses modules en attente.
///
/// ON GARDE `disputeId` MÊME SANS S'EN SERVIR, ET C'EST VOLONTAIRE.
///
/// La route `/dispute/:id` reste déclarée : elle est atteinte depuis les
/// notifications push et depuis les liens de l'app. Changer la signature
/// obligerait à toucher le routeur, puis à le retoucher au rebranchement — deux
/// occasions de se tromper pour aucun gain.
///
/// POUR REBRANCHER : extraire Disputes (avec les pièces jointes, qui passent
/// désormais par `media-service`), publier une route de passerelle, puis
/// restaurer `disputes_data.dart` (supprimé avec cet écran).
/// ═════════════════════════════════════════════════════════════════════════════
class DisputeScreen extends StatelessWidget {
  const DisputeScreen({super.key, required this.disputeId});

  final String disputeId;

  @override
  Widget build(BuildContext context) => const NotMigratedScreen(
        title: 'Réclamation',
        message:
            'Le suivi des réclamations arrive bientôt. En attendant, le service '
            'client HBA traite chaque dossier avec vous.',
      );
}
