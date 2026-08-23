import 'dart:convert';

import 'package:dio/dio.dart';

/// Exception normalisée pour les erreurs d'API, avec un message lisible.
class ApiException implements Exception {
  ApiException(this.message, {this.statusCode, this.code});

  final String message;
  final int? statusCode;
  final String? code;

  /// Extrait le message/code renvoyé par le backend (format Result / ProblemDetails).
  factory ApiException.fromDio(DioException e) {
    final response = e.response;
    if (response != null) {
      dynamic data = response.data;

      // Le backend renvoie ses erreurs en « application/problem+json » (title = code,
      // detail = message). Or Dio ne décode en JSON que « application/json » : le
      // corps arrive donc comme une CHAÎNE. Sans ce décodage, le message précis du
      // serveur était perdu et l'écran affichait un texte générique.
      if (data is String && data.trim().startsWith('{')) {
        try {
          data = jsonDecode(data);
        } catch (_) {
          // Corps non-JSON : on le gardera tel quel plus bas.
        }
      }

      String? message;
      String? code;
      if (data is Map) {
        message = (data['detail'] ?? data['message'] ?? data['title'] ?? data['error'])?.toString();
        code = (data['code'] ?? data['errorCode'] ?? data['title'])?.toString();
      } else if (data is String && data.trim().isNotEmpty) {
        message = data.trim();
      }

      // ═══════════════════════════════════════════════════════════════════════
      // UN 404 SANS LA ROUTE QUI L'A PRODUIT EST INDIAGNOSTICABLE.
      //
      // « Ressource introuvable. » sur un écran qui enchaîne trois appels — mettre
      // à jour le texte, changer le prix, rattacher une photo — ne dit pas lequel
      // a échoué. Or les causes sont opposées : une ROUTE absente signifie que le
      // service déployé est plus ancien que l'application, une RESSOURCE absente
      // signifie que la donnée n'est pas là. On a cherché l'une en croyant à
      // l'autre.
      //
      // 404 ET 405 SEULEMENT, PAS TOUS LES CODES.
      //
      // Un 400 ou un 409 porte déjà le message du serveur, qui est plus utile
      // qu'un chemin. Coller la route partout transformerait chaque erreur en
      // ligne de journal — et l'information utile se noierait, ce qui est le seul
      // échec qui compte pour un message d'erreur.
      // ═══════════════════════════════════════════════════════════════════════
      final brut = (message == null || message.isEmpty)
          ? _statusMessage(response.statusCode)
          : message;

      final requete = response.requestOptions;
      final situe = (response.statusCode == 404 || response.statusCode == 405)
          ? '$brut\n(${requete.method} ${requete.path})'
          : brut;

      return ApiException(
        situe,
        statusCode: response.statusCode,
        code: code,
      );
    }

    switch (e.type) {
      case DioExceptionType.connectionTimeout:
      case DioExceptionType.receiveTimeout:
      case DioExceptionType.sendTimeout:
        return ApiException('Délai dépassé. Vérifiez votre connexion.');
      case DioExceptionType.connectionError:
        return ApiException('Impossible de joindre le serveur.');
      default:
        return ApiException(e.message ?? 'Une erreur est survenue.');
    }
  }

  /// Vrai si la fonctionnalité n'existe pas (encore) côté serveur : permet
  /// d'afficher « pas encore disponible » plutôt qu'une erreur inquiétante.
  bool get isNotFound => statusCode == 404;

  /// ═══════════════════════════════════════════════════════════════════════════
  /// 502, 503 ET 504 MANQUAIENT, ET C'EST LA PANNE LA PLUS FRÉQUENTE.
  ///
  /// Ils tombaient dans le cas par défaut, donc sur « Une erreur est survenue. »
  /// — sans code, sans piste. Or ces trois-là ne signifient PAS « votre requête
  /// est mauvaise » : ils signifient qu'un service derrière la passerelle est
  /// tombé ou refuse de démarrer. C'est exactement ce qui s'est produit quand
  /// catalog-service a refusé de booter sur une dépendance non enregistrée :
  /// l'écran « Mes produits » affichait une erreur muette alors que la cause
  /// était lisible en une ligne dans les journaux.
  ///
  /// La distinction compte pour le vendeur aussi : « vérifiez votre connexion »
  /// l'enverrait redémarrer son téléphone pour une panne serveur.
  ///
  /// LE CODE EST JOINT AU MESSAGE PAR DÉFAUT. Un statut inattendu doit rester
  /// diagnosticable sans brancher un débogueur : c'est la seule information dont
  /// on est sûr de disposer.
  /// ═══════════════════════════════════════════════════════════════════════════
  static String _statusMessage(int? status) {
    switch (status) {
      case 400:
        return 'Requête invalide.';
      case 401:
        return 'Session expirée, veuillez vous reconnecter.';
      case 403:
        return "Action non autorisée. Votre compte n'est peut-être pas un compte vendeur.";
      case 404:
        return 'Ressource introuvable.';
      case 409:
        return "Conflit avec l'état actuel.";
      case 413:
        return 'Fichier trop volumineux.';
      case 422:
        return 'Données refusées par le serveur.';
      case 500:
        return 'Erreur serveur.';
      case 502:
      case 503:
        return 'Service momentanément indisponible. '
            'Ce n\'est pas votre connexion : réessayez dans un instant.';
      case 504:
        return 'Le serveur a mis trop de temps à répondre.';
      default:
        return status == null
            ? 'Une erreur est survenue.'
            : 'Une erreur est survenue (code $status).';
    }
  }

  @override
  String toString() => message;
}
