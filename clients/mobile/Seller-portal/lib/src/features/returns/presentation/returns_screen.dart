import 'package:flutter/material.dart';

import '../../../core/network/not_migrated.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// RETOURS — SANS AMONT.
///
/// LE MODULE RETURNS N'A JAMAIS ÉTÉ EXTRAIT DU MONOLITHE.
///
/// L'écran parlait à `/seller/returns*` : la liste, puis `approve`, `reject`,
/// `received`, `refund`, `tracking`. Aucun service HBA ne porte l'agrégat, et la
/// passerelle n'a aucune route vers un domaine de retour. C'est le même constat
/// que côté acheteur : l'app Client neutralise déjà sa demande de retour par
/// `NotMigrated.call('returns', …)`.
///
/// LE REMBOURSEMENT EST LA PARTIE QUI COÛTE, ET ELLE MANQUE AUSSI.
///
/// Un retour se solde par un mouvement d'argent. `financial-service` sait
/// rembourser un paiement, mais rien ne relie une demande de retour vendeur à ce
/// remboursement : ce n'est donc pas un écran à recâbler, c'est une chaîne à
/// construire.
///
/// POUR REBRANCHER : extraire Returns, le relier au remboursement PSP, publier
/// une route de passerelle, puis restaurer `returns_data.dart` (supprimé avec
/// cet écran).
/// ═════════════════════════════════════════════════════════════════════════════
class ReturnsScreen extends StatelessWidget {
  const ReturnsScreen({super.key});

  @override
  Widget build(BuildContext context) => const NotMigratedScreen(
        title: 'Retours',
        message:
            'La gestion des retours et des remboursements arrive bientôt. En '
            'attendant, contactez le service client HBA pour chaque demande.',
      );
}
