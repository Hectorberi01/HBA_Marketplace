import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'app_update_data.dart';

/// État de la porte « mise à jour requise », évalué UNE fois au démarrage.
///
/// LES QUATRE VALEURS SONT CONSERVÉES BIEN QUE DEUX SOIENT INATTEIGNABLES.
///
/// Le `switch` du routeur les traite toutes, sans `default` : c'est ce qui
/// garantit qu'aucun cas ne sera oublié le jour du rebranchement. En retirer
/// deux ferait disparaître la porte de blocage du code, et il faudrait la
/// réécrire de mémoire.
enum AppUpdateStatus {
  /// Vérification en cours : on patiente sur le splash. On n'ouvre pas l'app
  /// « en attendant » — mais on ne bloque pas non plus définitivement.
  unknown,

  /// Le build installé est ≥ au minimum exigé : rien à faire.
  upToDate,

  /// Le build installé est trop ancien : plus rien n'est atteignable tant que
  /// l'utilisateur n'a pas mis à jour.
  updateRequired,

  /// Impossible de vérifier (réseau, serveur, plugin, ou — aujourd'hui —
  /// endpoint inexistant). On laisse passer.
  unavailable,
}

/// ═════════════════════════════════════════════════════════════════════════════
/// LA PORTE DE VERSION EST BRANCHÉE (tâche #228).
///
/// ELLE ÉTAIT NEUTRALISÉE, ET L'ENCADRÉ QUI SUIT DÉCRIT CE QUI A CHANGÉ.
///
/// `GET /api/app/seller/version` est servi par `AppVersionController` sur la
/// PASSERELLE, depuis `IConfiguration` — pas par un des treize services. Le
/// contrôleur existait déjà, entièrement écrit ; il manquait la section
/// `AppVersions` dans `appsettings.json` et cet appel-ci.
///
/// L'ancien constat, conservé pour mémoire : `GET /seller/app/version` VIVAIT DANS
/// LE MONOLITHE, et aucun service HBA ne publiait de politique de version. Garder
/// l'appel aurait coûté, à CHAQUE lancement, une requête vouée au 404 — puis les
/// 15 secondes de `connectTimeout` hors ligne — pendant lesquelles le routeur
/// retient le vendeur sur l'écran de démarrage.
///
/// ON RÉSOUT À `unavailable` ET NON À `upToDate`.
///
/// Les deux laissent passer la porte. Mais `upToDate` affirmerait que le build
/// a été vérifié et jugé conforme — ce qui est faux, et masquerait la panne le
/// jour où l'endpoint existera mais répondra mal. `unavailable` dit ce qui s'est
/// réellement produit : on n'a pas pu savoir.
///
/// L'`await` N'EST PAS DÉCORATIF. NE PAS LE SUPPRIMER.
///
/// `_resolve()` est appelé depuis `build()`, AVANT que celui-ci n'ait rendu son
/// état initial. Écrire `state` de façon synchrone à cet instant revient à
/// modifier un provider pas encore initialisé : Riverpod lève, la lecture faite
/// par le routeur échoue, et l'application reste indéfiniment sur l'écran de
/// démarrage. `Future<void>.delayed(Duration.zero)` rend la main à la boucle
/// d'événements — `build()` se termine, puis l'état s'écrit.
///
/// C'est exactement le piège qu'avait rencontré le raccourci de simulation, dont
/// la première instruction n'était plus un appel réseau.
/// ═════════════════════════════════════════════════════════════════════════════
class AppUpdateController extends Notifier<AppUpdateStatus> {
  /// La politique retenue, conservée pour l'écran de blocage.
  ///
  /// SANS ELLE, L'ÉCRAN NE PEUT RIEN PROPOSER. C'était le bloquant App Store
  /// 5.1.1(v) : une application qui se bloque doit dire OÙ aller. Le lien de la
  /// fiche store vient d'ici, pas d'une constante compilée — changer d'identifiant
  /// d'application ne doit pas exiger une livraison.
  AppVersionPolicy? politique;

  @override
  AppUpdateStatus build() {
    _resolve();
    return AppUpdateStatus.unknown;
  }

  Future<void> _resolve() async {
    // L'`await` RESTE INDISPENSABLE, MÊME AVEC UN VRAI APPEL RÉSEAU.
    //
    // `_resolve()` part de `build()`, avant que celui-ci n'ait rendu son état
    // initial. Écrire `state` de façon synchrone à cet instant fait lever
    // Riverpod, la lecture du routeur échoue, et l'application reste sur le
    // splash. Un appel réseau rend la main de lui-même — mais la première
    // instruction pourrait cesser d'en être un, comme c'est déjà arrivé avec le
    // raccourci de simulation. On ne compte donc pas sur lui.
    await Future<void>.delayed(Duration.zero);

    try {
      final installe = await ref.read(installedBuildProvider.future);
      final p = await ref.read(appVersionApiProvider).policy();
      politique = p;

      // COMPARAISON STRICTEMENT INFÉRIEURE, et `minSupportedBuild == 0` ne
      // bloque personne : aucun build ne peut être négatif. C'est ainsi que la
      // politique permissive de la passerelle — rendue pour une application non
      // configurée — laisse passer sans cas particulier ici.
      state = installe < p.minSupportedBuild
          ? AppUpdateStatus.updateRequired
          : AppUpdateStatus.upToDate;
    } catch (_) {
      // `unavailable` ET NON `upToDate`, ET LA NUANCE COMPTE.
      //
      // Les deux laissent passer la porte. Mais `upToDate` affirmerait que le
      // build a été vérifié et jugé conforme, ce qui est faux — et masquerait la
      // panne le jour où l'endpoint répond mal. `unavailable` dit ce qui s'est
      // réellement produit : on n'a pas pu savoir.
      //
      // ON NE BLOQUE JAMAIS SUR UN ÉCHEC DE VÉRIFICATION. Une panne de
      // passerelle mettrait sinon TOUS les vendeurs dehors, simultanément, sans
      // qu'aucun d'eux puisse rien y faire — une indisponibilité transformée en
      // panne totale du parc.
      state = AppUpdateStatus.unavailable;
    }
  }
}

final appUpdateControllerProvider =
    NotifierProvider<AppUpdateController, AppUpdateStatus>(AppUpdateController.new);
