import 'package:flutter/material.dart';

import '../../../core/network/not_migrated.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// CONSENTEMENT — SANS AMONT, ET DÉSORMAIS INATTEIGNABLE PAR CONSTRUCTION.
///
/// AUCUN ENDPOINT N'ENREGISTRE L'ACCEPTATION DES CONDITIONS VENDEUR.
///
/// L'écran envoyait `POST /seller/account/me/accept-terms` et se fiait à
/// `acceptedTermsVersion` renvoyé par le compte. Ces deux points appartenaient au
/// BFF vendeur du monolithe ; ni `/api/merchants/me` ni `/api/users` ne les
/// remplacent. Voir `consent_controller.dart` pour le détail.
///
/// CET ÉCRAN ÉTAIT VOLONTAIREMENT SANS ISSUE. C'EST CE QUI LE REND DANGEREUX
///    AUJOURD'HUI.
///
/// Retour Android neutralisé, hors de la coquille, seule destination tant que
/// l'accord manque : trois choix justes quand l'acceptation peut être
/// enregistrée. Sans endpoint, ils font d'un lancement d'application une impasse
/// — le bouton « J'accepte » lèverait, et il ne resterait que « Refuser », qui
/// déconnecte.
///
/// LA SORTIE CHOISIE EST DE TRAVERSER : le contrôleur résout à
/// `ConsentStatus.unavailable`, que le routeur laisse déjà passer. Cet écran
/// n'est donc plus atteint par le parcours de démarrage.
///
/// LA ROUTE `/consent` EST CONSERVÉE, ET CE N'EST PAS UNE ROUTE MORTE.
///
/// Elle reste nommée dans `redirect` — `onConsent` participe à l'aiguillage
/// d'atterrissage après connexion, aux côtés de `/splash` et `/login`. La
/// retirer obligerait à toucher le routeur maintenant, puis à le retoucher au
/// rebranchement.
///
/// Les textes eux-mêmes ne sont pas perdus : `Legal.terms` et `Legal.privacy`
/// restent lisibles par `/terms` et `/privacy`, qui n'ont jamais eu besoin de
/// serveur.
///
/// POUR REBRANCHER : exposer la version acceptée et son enregistrement côté HBA,
/// puis rendre à cet écran ses deux onglets déroulants, sa case à cocher
/// conditionnée à la lecture, et son refus qui déconnecte — que git conserve.
/// ═════════════════════════════════════════════════════════════════════════════
class ConsentScreen extends StatelessWidget {
  const ConsentScreen({super.key});

  @override
  Widget build(BuildContext context) => const NotMigratedScreen(
        title: 'Conditions d\'utilisation',
        message:
            'L\'acceptation en ligne des conditions arrive bientôt. Vous pouvez '
            'les lire dès maintenant depuis Compte, puis « Conditions '
            'générales » et « Confidentialité ».',
      );
}
