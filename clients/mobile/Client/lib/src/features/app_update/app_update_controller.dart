import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:package_info_plus/package_info_plus.dart';

import 'app_update_data.dart';

/// État de la porte « mise à jour requise », évalué UNE fois au démarrage.
enum AppUpdateStatus {
  /// Vérification en cours : on patiente sur le splash. On n'ouvre pas l'app
  /// « en attendant » — mais on ne bloque pas non plus définitivement.
  unknown,

  /// Le build installé est ≥ au minimum exigé : rien à faire.
  upToDate,

  /// Le build installé est trop ancien : plus rien n'est atteignable tant que
  /// l'utilisateur n'a pas mis à jour.
  updateRequired,

  /// Impossible de vérifier (réseau, serveur, plugin). On laisse passer :
  /// bloquer l'app sur une simple panne de vérification serait pire que le mal.
  unavailable,
}

/// Décide, au lancement, si l'app installée est encore autorisée à tourner.
///
/// La comparaison est faite ICI, côté client : l'app connaît son propre build,
/// le serveur ne fait qu'annoncer le seuil minimal (piloté par configuration).
///
/// FAIL-OPEN volontaire : toute erreur (hors ligne, endpoint pas encore déployé,
/// numéro de build illisible) aboutit à [AppUpdateStatus.unavailable], c.-à-d.
/// « on laisse passer ». Une porte de mise à jour ne doit jamais transformer une
/// panne réseau en application inutilisable.
class AppUpdateController extends Notifier<AppUpdateStatus> {
  /// Politique renvoyée par le serveur (liens store, message). Null tant que la
  /// vérification n'a pas abouti — l'écran de blocage sait s'en passer.
  AppVersionPolicy? policy;

  /// Build actuellement installé (pour l'affichage/diagnostic).
  int currentBuild = 0;

  @override
  AppUpdateStatus build() {
    _check();
    return AppUpdateStatus.unknown;
  }

  Future<void> _check() async {
    try {
      final info = await PackageInfo.fromPlatform();
      // buildNumber = versionCode (Android) / CFBundleVersion (iOS). Illisible → 0.
      currentBuild = int.tryParse(info.buildNumber) ?? 0;

      final p = await ref.read(appUpdateApiProvider).policy();
      policy = p;

      // Seuil à 0 (défaut backend) = rien n'est bloqué.
      final blocked = p.minSupportedBuild > 0 && currentBuild > 0 && currentBuild < p.minSupportedBuild;

      state = blocked ? AppUpdateStatus.updateRequired : AppUpdateStatus.upToDate;
    } catch (_) {
      state = AppUpdateStatus.unavailable;
    }
  }

  /// Relance la vérification depuis l'écran de blocage (« J'ai déjà mis à jour »).
  Future<void> recheck() => _check();
}

final appUpdateControllerProvider =
    NotifierProvider<AppUpdateController, AppUpdateStatus>(AppUpdateController.new);
