import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../activities_data.dart';
import '../selected_activity.dart';
import '../../catalog/presentation/partner_products_screen.dart';
import '../../menu/presentation/partner_menu_screen.dart';
import 'my_activities_screen.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// LE 3ᵉ ONGLET — trois contenus derrière une seule route.
///
///   • aucune activité choisie → la LISTE des activités ;
///   • une boutique            → le catalogue « Mes produits » ;
///   • un restaurant           → la carte « Menu ».
///
/// UNE SEULE ROUTE, `/activities`, ET NON TROIS.
///
/// Faire naviguer la barre vers `/products` ou `/menu` selon le contexte paraît
/// plus direct, mais casse au moment le plus banal : on est sur `/products`, on
/// bascule vers un restaurant par la feuille, et la route ne correspond plus à
/// l'onglet. Deux choses cassent alors ensemble — l'onglet actif se calcule par
/// `location.startsWith`, donc plus rien n'est surligné ; et l'écran affiché
/// reste le catalogue d'une boutique qu'on ne regarde plus.
///
/// Avec une route unique, la bascule d'activité recompose l'écran et laisse
/// l'onglet en place. C'est le même choix que pour `/home`.
/// ═════════════════════════════════════════════════════════════════════════════
class ActivitiesTabScreen extends ConsumerWidget {
  const ActivitiesTabScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final activity = ref.watch(selectedActivityProvider);

    return switch (activity?.universe) {
      null => const MyActivitiesScreen(),
      HbaUniverse.express => PartnerProductsScreen(activity: activity!),
      HbaUniverse.food => PartnerMenuScreen(activity: activity!),
    };
  }
}
