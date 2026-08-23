import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../activities/activities_data.dart';
import '../../activities/selected_activity.dart';
import 'express_dashboard_screen.dart';
import 'food_dashboard_screen.dart';
import 'global_dashboard_screen.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// ONGLET « ACCUEIL » — AIGUILLE VERS LE TABLEAU DE BORD DE L'UNIVERS COURANT.
///
/// L'accueil a TROIS formes : consolidée, boutique, restaurant.
///
/// REMPLACE LA ROUTE `/food-home`, QUI ÉTAIT UNE FAUSSE BONNE IDÉE.
///
/// J'avais créé deux routes, `/home` pour la boutique et `/food-home` pour le
/// restaurant, en écrivant que « le choix se fait à la sélection d'activité ».
/// Le défaut sautait aux yeux dès qu'on touchait la barre du bas : l'onglet
/// Accueil pointe sur `/home`, donc un restaurateur retombait sur le tableau de
/// bord d'une boutique qui n'est pas la sienne.
///
/// La maquette tranche autrement, et mieux : une seule destination, dont le
/// CONTENU suit l'activité courante. Le changement d'activité devient un
/// changement d'état — instantané, sans navigation — au lieu d'un changement
/// d'adresse.
///
/// CE N'EST PAS UN ÉCRAN. IL NE DOIT RIEN DESSINER.
///
/// Y ajouter un en-tête commun ou un `Scaffold` obligerait les deux tableaux de
/// bord à s'y adapter, et l'aiguillage deviendrait une couche de mise en page de
/// plus. Il choisit, et c'est tout.
/// ═════════════════════════════════════════════════════════════════════════════
class PartnerHomeScreen extends ConsumerWidget {
  const PartnerHomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final activity = ref.watch(selectedActivityProvider);

    // TROIS BRANCHES, PAS DEUX. `null` EST LA VUE CONSOLIDÉE.
    //
    // La première version n'en avait que deux et prenait la première activité
    // par défaut : le bouton « Toutes mes activités » de l'aiguillage de
    // connexion n'avait alors nulle part où aller, et la vue globale était
    // inatteignable depuis l'application.
    return switch (activity?.universe) {
      null => const GlobalDashboardScreen(),
      HbaUniverse.food => const FoodDashboardScreen(),
      HbaUniverse.express => const ExpressDashboardScreen(),
    };
  }
}
