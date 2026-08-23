import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../activities/selected_activity.dart';
import 'activity_orders_screen.dart';
import 'global_orders_screen.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// L'ONGLET COMMANDES — consolidé, ou celui d'une activité.
///
/// Quatrième aiguillage bâti sur le même axe que l'accueil, le 3ᵉ onglet et les
/// finances : `null` donne la vue de toutes les activités, une activité donne la
/// sienne.
///
/// DEUX BRANCHES ICI, ET NON TROIS.
///
/// À la différence du 3ᵉ onglet, une commande de boutique et une commande de
/// restaurant se lisent de la même façon : une référence, un montant, une heure,
/// un statut.
///
/// ET MÊME LES STATUTS SONT LES MÊMES, DÉSORMAIS.
///
/// La maquette distinguait « À préparer » (boutique) de « En préparation »
/// (restaurant). Aucun des deux n'existe : order-service ne connaît qu'un seul
/// jeu — `Pending`, `AwaitingPayment`, `Paid`, `Confirmed`, `Cancelled`,
/// `Failed`, `Delivered`, `UnderReview` — pour les deux univers. Voir
/// `SellerOrderStatus` dans `orders_data.dart`.
///
/// L'ÉCRAN CUISINE (KDS) RESTE UN ÉCRAN À PART.
///
/// C'est lui qui porte le flux propre au restaurant — postes, tickets, minuteur.
/// Il n'a pas sa place dans un onglet qu'un commerçant partage.
/// ═════════════════════════════════════════════════════════════════════════════
class OrdersTabScreen extends ConsumerWidget {
  const OrdersTabScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final activity = ref.watch(selectedActivityProvider);

    return activity == null
        ? const GlobalOrdersScreen()
        : ActivityOrdersScreen(activity: activity);
  }
}
