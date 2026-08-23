import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers/core_providers.dart';
import 'activities_data.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// ACTIVITÉ COURANTE — OU AUCUNE.
///
/// CE FICHIER VIVAIT DANS `core/mock/`, ET IL N'AVAIT RIEN À Y FAIRE.
///
/// Ce n'est pas une donnée simulée : c'est de l'ÉTAT D'INTERFACE — quelle
/// activité le partenaire regarde en ce moment. Il a survécu à la suppression du
/// dossier `core/mock/` et vit désormais à côté de la donnée qu'il résout.
///
/// `null` N'EST PAS UNE ABSENCE DE VALEUR : C'EST LA VUE CONSOLIDÉE.
///
/// L'accueil a TROIS formes, et non deux :
///
///   • aucune activité choisie  → tableau de bord GLOBAL, qui agrège les deux
///     univers et n'offre aucune action opérationnelle ;
///   • une boutique             → tableau de bord HBAExpress ;
///   • un restaurant            → tableau de bord HBA Food.
///
/// NE PAS REMPLACER `null` PAR UNE ACTIVITÉ FICTIVE « TOUTES ».
///
/// Elle aurait un nom, des initiales, un univers — trois valeurs fausses qu'il
/// faudrait ensuite exclure partout. L'absence se teste une fois ; un faux membre
/// se filtre sans fin.
///
/// LA LISTE VIENT DU RÉSEAU, ET CELA CHANGE LA RÉSOLUTION.
///
/// `activitiesProvider` est un `FutureProvider` : tant qu'il n'a pas répondu,
/// l'activité courante est `null` — donc la vue consolidée — même si un
/// identifiant a été choisi. C'est le seul repli qui n'affiche les chiffres de
/// personne : rendre une activité « en attendant » montrerait les données de la
/// précédente.
/// ═════════════════════════════════════════════════════════════════════════════

/// L'identifiant de l'activité regardée, restauré au lancement et persisté.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CE N'EST PLUS UN SIMPLE `StateProvider`.
///
/// Il rendait `null` au démarrage, et le choix n'était pas retenu. Or `null`
/// mène à `GlobalDashboardScreen` et `GlobalOrdersScreen`, qui sont TOUS DEUX
/// des écrans « bientôt disponible » (module `merchantConsolidated`).
///
/// Conséquence : chaque ouverture de l'application affichait deux écrans morts
/// sur les deux premiers onglets, avant même que le partenaire ait compris qu'il
/// devait passer par le troisième pour choisir une activité. C'était le défaut le
/// plus visible de toute l'application, et il ne tenait à aucune ligne de
/// serveur.
///
/// TROIS ÉTATS EN MÉMOIRE PERSISTANTE, ET NON DEUX.
///
/// La distinction est nécessaire, et elle n'est pas cosmétique :
///
///   • clé ABSENTE      — le partenaire n'a jamais choisi. On choisit pour lui.
///   • clé VIDE (`''`)  — il a choisi « Toutes mes activités », explicitement.
///                        On ne doit PAS le remplacer au prochain lancement,
///                        sinon son choix est écrasé à chaque démarrage.
///   • clé = identifiant — l'activité retenue.
///
/// Sans le troisième état, « je veux la vue consolidée » et « je n'ai rien dit »
/// seraient indiscernables, et l'un des deux serait forcément trahi.
///
/// LE CHOIX PAR DÉFAUT EST LA PREMIÈRE ACTIVITÉ — TANT QUE LA VUE CONSOLIDÉE
///    N'EXISTE PAS.
///
/// C'est un arbitrage daté, pas une règle de produit. Aujourd'hui la vue
/// consolidée est un écran vide : y envoyer quelqu'un par défaut, c'est lui
/// montrer une panne. Le jour où `merchantConsolidated` sera raccordé, ce défaut
/// devra être rediscuté — pour un compte à cinq boutiques, la vue d'ensemble est
/// probablement le bon point de départ.
///
/// LE COFFRE-FORT PLUTÔT QUE `SharedPreferences` : l'application n'embarque
/// pas cette dépendance, et `settings_data.dart` range déjà thème et langue ici.
/// Une seconde mécanique de persistance pour une seule chaîne ne se justifie pas.
/// ═════════════════════════════════════════════════════════════════════════════
class SelectedActivityId extends Notifier<String?> {
  static const _cle = 'activite_courante';

  /// Vrai dès que la restauration ET le choix par défaut ont été tranchés.
  /// Empêche un second passage si la liste d'activités est rechargée.
  bool _resolu = false;

  @override
  String? build() {
    unawaited(_initialiser());
    return null;
  }

  /// Retient le choix du partenaire. `null` = vue consolidée, explicitement.
  ///
  /// L'ÉTAT BASCULE AVANT L'ÉCRITURE, ET C'EST VOULU. Le coffre-fort peut
  /// être indisponible (appareil verrouillé au réveil, profil d'entreprise) :
  /// faire attendre l'interface sur un stockage capricieux ferait paraître
  /// l'application figée sur un simple changement d'onglet.
  void choisir(String? id) {
    _resolu = true;
    state = id;
    unawaited(_ecrire(id ?? ''));
  }

  Future<void> _ecrire(String valeur) async {
    try {
      await ref.read(secureStorageProvider).write(key: _cle, value: valeur);
    } catch (_) {
      // Un choix non persisté se repose au prochain lancement. C'est un confort
      // perdu, pas une panne : on ne dérange pas le partenaire avec ça.
    }
  }

  Future<void> _initialiser() async {
    String? stocke;
    try {
      stocke = await ref.read(secureStorageProvider).read(key: _cle);
    } catch (_) {
      // Coffre-fort illisible : on retombe sur le comportement d'un premier
      // lancement, qui est sûr.
      stocke = null;
    }

    // Choix explicite de la vue consolidée : on n'y touche pas.
    if (stocke != null && stocke.isEmpty) {
      _resolu = true;
      return;
    }

    if (stocke != null && stocke.isNotEmpty) {
      state = stocke;
      // PAS ENCORE `_resolu`. L'identifiant restauré peut désigner une
      // activité disparue — boutique fermée, restaurant suspendu, rôle retiré.
      // On laisse la vérification ci-dessous s'exécuter, sinon le partenaire
      // retomberait sur la vue consolidée sans comprendre pourquoi.
    }

    await _appliquerDefaut(stocke);
  }

  /// Attend la liste, puis vérifie que le choix restauré tient encore debout.
  Future<void> _appliquerDefaut(String? stocke) async {
    List<SellerActivity> activites;
    try {
      // `.future` ET NON `.valueOrNull` : au lancement, la requête n'est pas
      // encore partie. Lire la valeur courante rendrait `null` à coup sûr, et le
      // défaut ne s'appliquerait jamais.
      final resultat = await ref.read(activitiesProvider.future);
      activites = resultat.data;
    } catch (_) {
      // Hors ligne, ou façade en erreur : on ne choisit rien. Le partenaire
      // verra l'erreur de la liste, ce qui est plus juste qu'une activité
      // sélectionnée au hasard.
      return;
    }

    if (_resolu || activites.isEmpty) {
      _resolu = true;
      return;
    }

    final encoreLa = stocke != null && activites.any((a) => a.id == stocke);
    if (!encoreLa) {
      state = activites.first.id;
      unawaited(_ecrire(activites.first.id));
    }

    _resolu = true;
  }
}

final selectedActivityIdProvider =
    NotifierProvider<SelectedActivityId, String?>(SelectedActivityId.new);

/// L'activité courante, résolue — ou `null` pour la vue consolidée.
///
/// Rend `null` si l'identifiant retenu ne correspond plus à rien : activité
/// supprimée pendant la session, ou compte dont le rôle a changé.
final selectedActivityProvider = Provider<SellerActivity?>((ref) {
  final id = ref.watch(selectedActivityIdProvider);
  if (id == null) return null;

  final activities = ref.watch(activitiesProvider).valueOrNull?.data;
  if (activities == null) return null;

  for (final activity in activities) {
    if (activity.id == id) return activity;
  }
  return null;
});
