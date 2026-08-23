import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'mission_mock_data.dart';

/// Secondes restantes pour accepter la mission proposée.
///
/// FOURNISSEUR SÉPARÉ, ET CE N'EST PAS UN CAPRICE D'ARCHITECTURE.
///
/// Le rebours vivait d'abord dans `MissionFlow`, qui appelait
/// `ref.notifyListeners()` à chaque seconde pour se réémettre. Cette méthode
/// n'existe pas sur `Notifier` en Riverpod 2 : le code n'aurait pas compilé.
///
/// Le séparer est de toute façon meilleur. `MissionFlow` ne change d'état que
/// dix fois par course ; le rebours change quinze fois en quinze secondes. Les
/// fondre aurait fait reconstruire tout l'écran de mission à chaque tic.
final missionCountdownProvider = StateProvider<int>(
  (ref) => MissionMockData.acceptSeconds,
);

/// ═════════════════════════════════════════════════════════════════════════════
/// LA MACHINE À ÉTATS DE LA MISSION.
///
/// L'ENCHAÎNEMENT EST DÉCRIT ICI, ET NULLE PART AILLEURS.
///
/// Dix écrans se succèdent. Si chacun décidait de son suivant par un
/// `context.push`, l'ordre réel du parcours ne serait lisible qu'en ouvrant les
/// dix fichiers — et le premier écart entre deux d'entre eux passerait inaperçu.
///
/// AUCUNE MISSION N'EST PERSISTÉE.
///
/// Quitter le flux perd tout. En production c'est le serveur qui porte l'état :
/// un livreur dont le téléphone s'éteint au milieu d'une course doit la
/// retrouver au redémarrage, et cela ne se simule pas honnêtement.
/// ═════════════════════════════════════════════════════════════════════════════
class MissionFlow extends Notifier<MissionStage?> {
  Timer? _countdown;

  @override
  MissionStage? build() {
    // Sans cela, le minuteur continuerait de battre après la sortie du flux et
    // écrirait dans un état détruit.
    ref.onDispose(() => _countdown?.cancel());
    return null;
  }

  /// Une mission est proposée : le rebours démarre.
  void offer() {
    _countdown?.cancel();
    ref.read(missionCountdownProvider.notifier).state =
        MissionMockData.acceptSeconds;
    state = MissionStage.offered;

    _countdown = Timer.periodic(const Duration(seconds: 1), (timer) {
      final left = ref.read(missionCountdownProvider) - 1;
      ref.read(missionCountdownProvider.notifier).state = left;

      if (left <= 0) {
        timer.cancel();
        // À ZÉRO, L'OFFRE SE FERME — ELLE N'EST PAS « REFUSÉE ».
        //
        // Cf. `MissionMockData.acceptSeconds` : compter un silence comme un refus
        // pénaliserait un livreur qui conduisait. Hypothèse la moins pénalisante,
        // à confirmer avec le service.
        decline();
      }
    });
  }

  void decline() {
    _countdown?.cancel();
    state = null;
  }

  /// Passe à l'étape suivante du parcours.
  ///
  /// `arrivedAtPickup` NE MÈNE PAS TOUJOURS À L'ATTENTE.
  ///
  /// Une commande déjà prête saute directement à la confirmation du retrait. Ici
  /// elle est « En préparation », donc on attend — mais le branchement doit
  /// exister dès maintenant, sinon il sera oublié le jour où le statut réel
  /// arrivera, et l'on fera patienter devant un sac posé sur le comptoir.
  void advance({bool orderReady = false}) {
    _countdown?.cancel();

    state = switch (state) {
      MissionStage.offered => MissionStage.accepted,
      MissionStage.accepted => MissionStage.goingToPickup,
      MissionStage.goingToPickup => MissionStage.arrivedAtPickup,
      MissionStage.arrivedAtPickup =>
        orderReady ? MissionStage.pickedUp : MissionStage.waitingForPickup,
      MissionStage.waitingForPickup => MissionStage.pickedUp,
      MissionStage.pickedUp => MissionStage.goingToDropoff,
      MissionStage.goingToDropoff => MissionStage.arrivedAtDropoff,
      MissionStage.arrivedAtDropoff => MissionStage.verifying,
      MissionStage.verifying => MissionStage.delivered,
      MissionStage.delivered => null,
      null => null,
    };
  }

  /// Fin de course : on repart d'une file vide.
  void finish() {
    _countdown?.cancel();
    state = null;
  }
}

final missionFlowProvider =
    NotifierProvider<MissionFlow, MissionStage?>(MissionFlow.new);

/// Le mode de preuve retenu à l'écran 14.
///
/// IL EST IMPOSÉ PAR LE SERVICE — cf. `ProofMode`. Ce fournisseur existe pour
/// que la démonstration montre les trois, pas pour offrir un choix au livreur.
final proofModeProvider =
    StateProvider<ProofMode>((ref) => MissionMockData.proofMode);

/// Chronomètre d'attente devant le comptoir (écran 10).
///
/// Il monte, il ne descend pas : on ne promet pas une durée qu'on ne maîtrise
/// pas. « 3 min 12 » d'attente écoulée est un fait ; « plus que 2 min » serait
/// une promesse faite à la place du restaurant.
class PickupWait extends Notifier<int> {
  Timer? _timer;

  @override
  int build() {
    ref.onDispose(() => _timer?.cancel());
    return 192; // 3 min 12, la valeur de la maquette.
  }

  void start() {
    _timer?.cancel();
    _timer = Timer.periodic(const Duration(seconds: 1), (_) => state = state + 1);
  }

  void stop() => _timer?.cancel();

  /// « 3 min 12 »
  String get label => '${state ~/ 60} min ${(state % 60).toString().padLeft(2, '0')}';
}

final pickupWaitProvider = NotifierProvider<PickupWait, int>(PickupWait.new);
