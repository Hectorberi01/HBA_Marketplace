import 'package:flutter/material.dart';

import '../../../core/network/not_migrated.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// COMMANDES, TOUTES ACTIVITÉS — SANS AMONT.
///
/// SEULE LA VUE CONSOLIDÉE BASCULE. LA VUE PAR ACTIVITÉ RESTE.
///
/// `OrdersTabScreen` aiguille `/orders` sur deux branches : `null` mène ici, une
/// activité mène à `ActivityOrdersScreen`. Cette dernière est conservée telle
/// quelle — c'est elle que VEN3 câblera sur les commandes d'UNE boutique, et
/// c'est aussi elle qui porte `OrderListCard`, la carte partagée.
///
/// IL N'EXISTE AUCUNE REQUÊTE « MES COMMANDES, TOUTES ACTIVITÉS ».
///
/// Les commandes d'un vendeur se lisent par `GET /api/sellers/{sellerId}/orders`
/// — une activité à la fois, et cette route n'est routée par AUCUNE entrée de la
/// passerelle à ce jour. Rien n'existe qui mélange les files de plusieurs
/// boutiques et restaurants, et rien ne le pourrait sans une agrégation nouvelle
/// au BFF merchant : les deux façades sont gardées par des rôles distincts.
///
/// UNE FILE MÉLANGÉE MENSONGÈRE EST PARTICULIÈREMENT COÛTEUSE.
///
/// Cette vue montrait des commandes de plusieurs activités, avec un bandeau
/// d'origine et un bouton « Voir la commande ». Un partenaire qui prépare une
/// commande fabriquée perd un après-midi, et le vrai client, lui, attend.
///
/// POUR REBRANCHER : exposer `GET /api/sellers/{sellerId}/orders` (tâche « La
/// route /api/sellers/{id}/orders n'est exposée par aucune entrée YARP »), puis
/// ajouter au BFF merchant l'agrégation multi-activités. La mise en page — les
/// filtres et le bandeau d'origine — est dans l'historique git.
/// ═════════════════════════════════════════════════════════════════════════════
class GlobalOrdersScreen extends StatelessWidget {
  const GlobalOrdersScreen({super.key});

  @override
  Widget build(BuildContext context) => const NotMigratedScreen(
        inShell: true,
        title: 'Commandes',
        message:
            'La vue de toutes vos commandes arrive bientôt : elle réunira les '
            'files de vos boutiques et de vos restaurants au même endroit.',
        detail:
            'Ouvrez une activité depuis l\'onglet « Activités » pour voir ses '
            'commandes.',
      );
}
