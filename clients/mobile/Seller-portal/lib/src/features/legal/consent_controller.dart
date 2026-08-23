import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Où en est le consentement de l'utilisateur connecté.
///
/// LES QUATRE VALEURS SONT CONSERVÉES BIEN QUE TROIS SOIENT INATTEIGNABLES.
///
/// Le `switch` du routeur les traite toutes, sans `default`. Les retirer ferait
/// disparaître du code la porte bloquante qu'il faudra rétablir, et il faudrait
/// la réécrire de mémoire.
enum ConsentStatus {
  /// On ne sait pas encore : la vérification est en cours. On n'ouvre PAS l'app
  /// dans le doute — mais on ne bloque pas non plus sur un écran de conditions
  /// qu'il a peut-être déjà acceptées.
  unknown,

  /// Il faut demander l'accord : rien n'a jamais été accepté, ou le texte a changé.
  required,

  /// Accord donné pour la version courante.
  granted,

  /// Impossible de vérifier (réseau, serveur, ou — aujourd'hui — endpoint
  /// inexistant). Voir le bloc ci-dessous.
  unavailable,
}

/// ═════════════════════════════════════════════════════════════════════════════
/// LA PORTE DE CONSENTEMENT EST NEUTRALISÉE, PAS SUPPRIMÉE.
///
/// IL N'Y A NI LECTURE NI ÉCRITURE POSSIBLE DU CONSENTEMENT VENDEUR.
///
/// Le contrôleur lisait `acceptedTermsVersion` sur `GET /seller/account/me` et
/// écrivait par `POST /seller/account/me/accept-terms`. Les deux appartenaient
/// au BFF vendeur du MONOLITHE. Côté HBA, `GET /api/merchants/me` rend le
/// vendeur et son KYB, `/api/users` son profil — aucun des deux ne porte de
/// version de conditions acceptée, et aucune route n'enregistre une acceptation.
///
/// UN ÉCRAN DE CONSENTEMENT QUI BLOQUE AU LANCEMENT REND L'APP INUTILISABLE.
///
/// C'est le point qui décide de tout ici. `redirect` place `ConsentStatus.required`
/// en garde absolue : « rien d'autre n'est atteignable ». Si la vérification ne
/// peut pas aboutir, cet état ne se lève jamais — et le bouton « J'accepte »
/// échouerait de toute façon à l'envoi, faute d'endpoint. Le vendeur tournerait
/// en rond sur un écran sans issue, sans qu'aucun message n'explique pourquoi.
///
/// ON TRAVERSE DONC PLUTÔT QUE DE BLOQUER : `unavailable`, que le routeur laisse
/// déjà passer, et qui était prévu pour exactement ce cas de figure — la mention
/// « on ne peut RIEN affirmer, on redemandera » existait avant cette bascule.
///
/// CE N'EST PAS UN CONSENTEMENT PRÉSUMÉ. `granted` AURAIT ÉTÉ CELA.
///
/// La différence n'est pas cosmétique : `granted` inscrirait dans le code que
/// l'accord a été donné, alors que rien ne l'a été et que rien ne l'a enregistré.
/// `unavailable` dit la vérité — l'accord n'a pas pu être demandé — et laissera
/// la porte se refermer d'elle-même le jour où l'endpoint existera.
///
/// LA VÉRIFICATION N'EST PLUS ATTACHÉE À LA SESSION.
///
/// L'écoute de `authControllerProvider` disparaît : il n'y a plus rien à
/// revérifier à chaque connexion, et la garder ferait tourner le routeur à vide
/// (il se réévalue à chaque changement de cet état). Elle devra revenir avec
/// l'appel — le consentement reste attaché à une session, sans quoi l'accord du
/// vendeur précédent vaudrait pour le suivant sur un téléphone partagé.
///
/// POUR REBRANCHER : exposer la version acceptée et sa mise à jour côté HBA,
/// rétablir l'écoute de session et la comparaison avec `Legal.version`, puis
/// rendre à `ConsentScreen` son contenu — que l'historique git conserve.
/// ═════════════════════════════════════════════════════════════════════════════
class ConsentController extends Notifier<ConsentStatus> {
  @override
  ConsentStatus build() => ConsentStatus.unavailable;
}

final consentControllerProvider =
    NotifierProvider<ConsentController, ConsentStatus>(ConsentController.new);
