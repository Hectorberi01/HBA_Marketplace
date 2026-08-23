import 'api_exception.dart';

/// Marque un appel dont l'amont n'existe pas encore sur la passerelle HBA.
///
/// ═══════════════════════════════════════════════════════════════════════════
/// POURQUOI LEVER PLUTÔT QUE LAISSER UNE URL QUI COMPILE.
///
/// Sept domaines de cette application n'ont aucun équivalent côté HBA :
/// litiges, retours, fidélité, communes, contenus d'aide, vitrine boutique,
/// version applicative. Le module correspondant n'a pas été extrait du
/// monolithe.
///
/// La tentation est de garder l'ancien chemin `/mobile/...` : ça compile, et
/// « on verra plus tard ». Mais ça produirait exactement le pire des résultats —
/// une application qui semble marcher, avec quelques écrans qui rendent 404
/// sans que rien ne dise pourquoi, et un doute permanent sur la question de
/// savoir si le serveur est en panne ou la fonctionnalité absente.
///
/// Lever ici donne trois choses : un message clair à l'écran, un point d'arrêt
/// évident au débogueur, et une liste greppable de ce qui reste à faire —
/// `grep -rn NotMigrated`.
///
/// CE N'EST PAS UN `TODO`. Un TODO se contourne du regard ; ceci s'exécute.
/// ═══════════════════════════════════════════════════════════════════════════
class NotMigrated {
  const NotMigrated._();

  /// Lève systématiquement. [domain] nomme le module manquant, [screen] l'écran
  /// qui en dépend.
  static Never call(String domain, {required String screen}) {
    throw ApiException(
      'Cette fonctionnalité n\'est pas encore disponible : le module « $domain » '
      'n\'a pas encore été repris sur la nouvelle plateforme.',
      code: 'not_migrated',
    );
  }

  /// Les modules concernés, pour mémoire — et pour que la liste vive à UN seul
  /// endroit plutôt que dispersée en commentaires.
  ///
  /// Chacun demande une extraction côté serveur, pas une ligne de Dart :
  ///
  ///   • `disputes`  — litiges et leur fil de discussion
  ///   • `returns`   — demandes de retour et remboursements
  ///   • `loyalty`   — points de fidélité
  ///   • `geo`       — communes du Bénin, pour la saisie d'adresse
  ///   • `content`   — contenus éditoriaux (aide, FAQ)
  ///   • `shop`      — vitrine publique d'une boutique
  ///   • `appUpdate` — version minimale exigée du client
  static const List<String> pendingModules = [
    'disputes',
    'returns',
    'loyalty',
    'geo',
    'content',
    'shop',
    'appUpdate',
  ];
}
