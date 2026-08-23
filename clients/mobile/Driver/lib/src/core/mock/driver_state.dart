import 'package:flutter_riverpod/flutter_riverpod.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// DISPONIBILITÉ DU LIVREUR — l'état le plus important de l'application.
///
/// HORS LIGNE PAR DÉFAUT, ET CE N'EST PAS UN DÉTAIL.
///
/// Passer disponible à l'ouverture ferait recevoir des missions à quelqu'un qui
/// vient de déverrouiller son téléphone pour consulter ses gains. Le refus d'une
/// mission a un coût chez tous les opérateurs de livraison — taux d'acceptation,
/// priorité d'attribution. On ne met personne en ligne à sa place.
///
/// C'est aussi pourquoi la maquette ne montre AUCUNE confirmation sur la
/// bascule : le geste est réversible d'un tap, et un livreur qui veut se mettre
/// en ligne est déjà sur son scooter.
/// ═════════════════════════════════════════════════════════════════════════════
final driverOnlineProvider = StateProvider<bool>((ref) => false);

/// Session simulée. `true` après une connexion réussie.
///
/// AUCUN JETON, AUCUN COFFRE-FORT, AUCUNE PERSISTANCE.
///
/// Tant qu'aucun service ne répond, stocker un faux jeton dans le Keychain
/// n'apporterait rien et laisserait une session fantôme sur l'appareil après la
/// démonstration.
final driverSignedInProvider = StateProvider<bool>((ref) => false);
