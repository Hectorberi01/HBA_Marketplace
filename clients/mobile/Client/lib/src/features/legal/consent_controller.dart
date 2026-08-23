import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../account/account_data.dart';
import '../auth/application/auth_controller.dart';
import 'legal_content.dart';

/// Où en est le consentement de l'utilisateur connecté.
enum ConsentStatus {
  /// On ne sait pas encore : la vérification est en cours. On n'ouvre PAS l'app
  /// dans le doute — mais on ne bloque pas non plus sur un écran de conditions
  /// qu'il a peut-être déjà acceptées.
  unknown,

  /// Il faut demander l'accord : rien n'a jamais été accepté, ou le texte a changé.
  required,

  /// Accord donné pour la version courante.
  granted,

  /// Impossible de vérifier (réseau, serveur). Voir le commentaire de [_check].
  unavailable,
}

/// Vérifie, à chaque ouverture de session, que l'acheteur a bien accepté la
/// version des conditions que CETTE application embarque.
///
/// La comparaison se fait ici, côté client, et c'est délibéré : le serveur ne
/// connaît pas la rédaction en vigueur dans chaque version publiée sur les stores.
/// Une app ancienne, restée sur un vieux texte, ne doit pas se croire à jour parce
/// que le serveur, lui, a avancé.
class ConsentController extends Notifier<ConsentStatus> {
  @override
  ConsentStatus build() {
    // Le consentement est attaché à une SESSION : il se revérifie à chaque
    // connexion, et se remet à zéro à la déconnexion — sinon l'accord de l'utilisateur
    // précédent vaudrait pour le suivant sur un téléphone partagé.
    ref.listen(authControllerProvider, (_, status) {
      if (status == AuthStatus.authenticated) {
        _check();
      } else {
        state = ConsentStatus.unknown;
      }
    });

    if (ref.read(authControllerProvider) == AuthStatus.authenticated) {
      _check();
    }
    return ConsentStatus.unknown;
  }

  Future<void> _check() async {
    state = ConsentStatus.unknown;
    try {
      final me = await ref.read(accountApiProvider).profile();
      state = me.acceptedTermsVersion == Legal.version
          ? ConsentStatus.granted
          : ConsentStatus.required;
    } catch (_) {
      // Réseau coupé, serveur muet : on ne peut RIEN affirmer.
      //
      // On ne présume pas l'accord — ce serait fabriquer un consentement. On ne
      // bloque pas non plus sur l'écran de conditions : l'acceptation aurait
      // échoué à l'envoi, et l'utilisateur tournerait en rond sans pouvoir entrer.
      // On laisse donc passer, avec une reprise à la prochaine ouverture. Le pire
      // cas est un acheteur qui parcourt le catalogue une fois de plus avant
      // d'être sollicité ; l'autre pire cas était une app inutilisable hors ligne.
      state = ConsentStatus.unavailable;
    }
  }

  /// L'utilisateur accepte. On n'affiche l'app qu'une fois le serveur d'accord :
  /// une acceptation qui n'a pas été enregistrée n'existe pas.
  Future<void> accept() async {
    await ref.read(accountApiProvider).acceptTerms(Legal.version);
    ref.invalidate(profileProvider);
    state = ConsentStatus.granted;
  }

  /// L'utilisateur refuse : il ne peut pas utiliser le service. On le déconnecte,
  /// proprement — plutôt que de le laisser sur un écran dont il ne sort pas.
  Future<void> decline() async {
    await ref.read(authControllerProvider.notifier).logout();
  }
}

final consentControllerProvider =
    NotifierProvider<ConsentController, ConsentStatus>(ConsentController.new);
