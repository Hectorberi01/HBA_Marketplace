import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/mock/mission_mock_data.dart';
import '../../../core/mock/mission_state.dart';
import '../../../core/theme/app_theme.dart';
import 'stages/accepted_stage.dart';
import 'stages/arrived_dropoff_stage.dart';
import 'stages/arrived_pickup_stage.dart';
import 'stages/delivered_stage.dart';
import 'stages/navigation_stage.dart';
import 'stages/offered_stage.dart';
import 'stages/pickup_confirm_stage.dart';
import 'stages/proof_stage.dart';
import 'stages/waiting_stage.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// LE FLUX MISSION — un écran, dix visages.
///
/// UNE SEULE ROUTE POUR LES ÉCRANS 06 À 15.
///
/// Dix routes auraient laissé le bouton retour du système remonter à une étape
/// révolue : revenir de « Arrivé chez le client » à « En route vers le retrait »
/// n'a aucun sens, et remettrait l'affichage en contradiction avec le serveur.
///
/// Ici l'étape est une DONNÉE, pas une position dans une pile. Le retour système
/// sort du flux ; il ne le rembobine pas.
/// ═════════════════════════════════════════════════════════════════════════════
class MissionFlowScreen extends ConsumerStatefulWidget {
  const MissionFlowScreen({super.key});

  @override
  ConsumerState<MissionFlowScreen> createState() => _MissionFlowScreenState();
}

class _MissionFlowScreenState extends ConsumerState<MissionFlowScreen> {
  @override
  void initState() {
    super.initState();
    // APRÈS LA PREMIÈRE FRAME, JAMAIS PENDANT `build`.
    //
    // `offer()` écrit dans deux fournisseurs. Le faire pendant la construction
    // du widget déclenche « setState during build » — l'erreur qui a déjà coûté
    // un écran de démarrage bloqué sur le portail vendeur.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) ref.read(missionFlowProvider.notifier).offer();
    });
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final stage = ref.watch(missionFlowProvider);

    // Sortie du flux : l'offre a expiré, été refusée, ou la course est close.
    if (stage == null) {
      return Scaffold(
        backgroundColor: colors.bg,
        body: const Center(child: CircularProgressIndicator()),
      );
    }

    return switch (stage) {
      MissionStage.offered => const OfferedStage(),
      MissionStage.accepted => const AcceptedStage(),
      MissionStage.goingToPickup => const NavigationStage(toPickup: true),
      MissionStage.arrivedAtPickup => const ArrivedPickupStage(),
      MissionStage.waitingForPickup => const WaitingStage(),
      MissionStage.pickedUp => const PickupConfirmStage(),
      MissionStage.goingToDropoff => const NavigationStage(toPickup: false),
      MissionStage.arrivedAtDropoff => const ArrivedDropoffStage(),
      MissionStage.verifying => const ProofStage(),
      MissionStage.delivered => const DeliveredStage(),
    };
  }
}

/// Sortie commune : on quitte le flux et on revient à l'accueil.
///
/// `go` ET NON `pop` : la pile peut être vide.
///
/// On entre dans le flux par `push` depuis l'accueil, mais aussi — un jour — par
/// une notification, qui ouvre l'application directement ici. `pop` n'aurait
/// alors rien à dépiler.
void leaveMission(BuildContext context, WidgetRef ref) {
  ref.read(missionFlowProvider.notifier).finish();
  context.go('/home');
}
