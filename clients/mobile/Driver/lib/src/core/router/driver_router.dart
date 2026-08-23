import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/auth/presentation/driver_login_screen.dart';
import '../../features/account/presentation/account_screen.dart';
import '../../features/account/presentation/documents_screen.dart';
import '../../features/account/presentation/notifications_screen.dart';
import '../../features/account/presentation/vehicle_screen.dart';
import '../../features/dashboard/presentation/driver_home_screen.dart';
import '../../features/earnings/presentation/earnings_screen.dart';
import '../../features/earnings/presentation/movements_screen.dart';
import '../../features/earnings/presentation/payout_screen.dart';
import '../../features/support/presentation/incident_screen.dart';
import '../../features/support/presentation/offline_screen.dart';
import '../../features/support/presentation/support_screen.dart';
import '../../features/history/presentation/history_screen.dart';
import '../../features/mission/presentation/mission_flow_screen.dart';
import '../../features/missions/presentation/missions_screen.dart';
import '../../features/onboarding/presentation/driver_onboarding_screen.dart';
import '../../features/onboarding/presentation/driver_verification_screen.dart';
import '../../navigation/driver_shell.dart';
import '../../shared/widgets/driver_widgets.dart';
import '../mock/driver_state.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// ROUTAGE.
///
/// UNE SEULE PORTE : ÊTRE CONNECTÉ.
///
/// Le portail vendeur en empile plusieurs (mise à jour requise, consentement,
/// session). Ici il n'y a qu'une session simulée. Recopier la mécanique de
/// redirection complète pour une seule condition aurait produit un code que
/// personne ne saurait relire — et qu'il faudrait démonter au premier vrai
/// contrôle.
///
/// LA COQUILLE À ONGLETS NE COUVRE QUE LES CINQ ONGLETS.
///
/// Connexion, inscription et vérification restent DEHORS : une barre d'onglets
/// pendant l'inscription laisserait quitter le formulaire d'un tap, et
/// « Revenus » n'aurait rien à afficher pour un dossier non validé.
/// ═════════════════════════════════════════════════════════════════════════════
final driverRouterProvider = Provider<GoRouter>((ref) {
  return GoRouter(
    initialLocation: '/login',
    redirect: (context, state) {
      final signedIn = ref.read(driverSignedInProvider);
      final path = state.uri.path;

      // Les trois écrans accessibles sans session. `/verification` en fait
      // partie : on y arrive au bout de l'inscription, avant tout compte actif.
      const public = {'/login', '/register', '/verification'};

      if (!signedIn && !public.contains(path)) return '/login';

      // Déjà connecté et de retour sur la connexion : on renvoie à l'accueil
      // plutôt que de laisser ouvrir une seconde session par-dessus la première.
      if (signedIn && path == '/login') return '/home';

      return null;
    },
    routes: [
      GoRoute(path: '/login', builder: (_, __) => const DriverLoginScreen()),
      GoRoute(path: '/register', builder: (_, __) => const DriverOnboardingScreen()),
      GoRoute(
        path: '/verification',
        builder: (_, __) => const DriverVerificationScreen(),
      ),

      ShellRoute(
        builder: (context, state, child) =>
            DriverShell(location: state.uri.path, child: child),
        routes: [
          GoRoute(path: '/home', builder: (_, __) => const DriverHomeScreen()),
          GoRoute(path: '/missions', builder: (_, __) => const MissionsScreen()),
          GoRoute(path: '/earnings', builder: (_, __) => const EarningsScreen()),
          GoRoute(path: '/history', builder: (_, __) => const HistoryScreen()),
          GoRoute(path: '/account', builder: (_, __) => const AccountScreen()),
        ],
      ),

      // Hors coquille : le flux mission prend tout l'écran, et le support est
      // atteignable depuis la vérification, donc avant toute session.
      // UNE SEULE ROUTE POUR LES ÉCRANS 06 À 15 — cf. `MissionFlowScreen`.
      //
      // Dix routes auraient laissé le bouton retour du système remonter à une
      // étape révolue : revenir de « Arrivé chez le client » à « En route vers le
      // retrait » remettrait l'affichage en contradiction avec le serveur.
      GoRoute(path: '/mission/new', builder: (_, __) => const MissionFlowScreen()),
      // HORS COQUILLE, ET ATTEIGNABLE SANS SESSION.
      //
      // L'assistance est offerte depuis l'écran de vérification des documents,
      // donc avant qu'un compte soit actif. La ranger dans la coquille la
      // rendrait inaccessible à qui en a le plus besoin : un candidat bloqué.
      GoRoute(path: '/support', builder: (_, __) => const SupportScreen()),

      // Écrans empilés du compte et des revenus.
      GoRoute(path: '/earnings/detail', builder: (_, __) => const MovementsScreen()),
      GoRoute(path: '/payout', builder: (_, __) => const PayoutScreen()),
      GoRoute(path: '/vehicle', builder: (_, __) => const VehicleScreen()),
      GoRoute(path: '/documents', builder: (_, __) => const DocumentsScreen()),
      GoRoute(path: '/notifications', builder: (_, __) => const NotificationsScreen()),

      // Terrain.
      GoRoute(path: '/incident', builder: (_, __) => const IncidentScreen()),
      GoRoute(path: '/offline', builder: (_, __) => const OfflineScreen()),
    ],

    errorBuilder: (_, state) => DriverPlaceholderScreen(
      title: 'Page introuvable',
      note: 'Aucun écran pour « ${state.uri.path} ».',
    ),
  );
});
