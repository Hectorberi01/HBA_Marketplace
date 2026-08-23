import 'dart:async';

import 'package:app_links/app_links.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../push/push_service.dart' show pendingPushRouteProvider;

/// LIENS UNIVERSELS entrants (Universal Links iOS / App Links Android).
///
/// Quand l'utilisateur ouvre un lien partagé `https://<domaine>/p/{id}`, le
/// système ouvre l'app avec cette URL. On la traduit en route interne
/// `/product/{id}` et on la dépose dans `pendingPushRouteProvider`.
///
/// Pourquoi réutiliser CE provider ? Parce que la fiche produit est derrière la
/// porte d'authentification du routeur : au démarrage à froid, le lien arrive
/// AVANT la restauration de session. Le mécanisme « route en attente » de
/// `app.dart` navigue tout de suite si la session est déjà là, sinon rejoue la
/// destination une fois connecté — exactement ce qu'il nous faut, sans le
/// réécrire.
class DeepLinkService {
  DeepLinkService(this._ref);

  final Ref _ref;
  final AppLinks _appLinks = AppLinks();
  StreamSubscription<Uri>? _sub;
  bool _started = false;

  /// Démarre l'écoute : lien de démarrage à froid + liens reçus app ouverte.
  Future<void> start() async {
    if (_started) return;
    _started = true;

    // Démarrage à FROID : l'app était fermée, le lien l'a lancée.
    try {
      final initial = await _appLinks.getInitialLink();
      if (initial != null) _handle(initial);
    } catch (e) {
      debugPrint('[DeepLink] lien initial ignoré : $e');
    }

    // App déjà ouverte : liens suivants.
    _sub = _appLinks.uriLinkStream.listen(
      _handle,
      onError: (Object e) => debugPrint('[DeepLink] flux en erreur : $e'),
    );
  }

  void _handle(Uri uri) {
    final route = routeForUri(uri);
    if (route == null) return;
    // Déposer l'intention ; app.dart la consomme (immédiatement si connecté,
    // sinon après connexion).
    _ref.read(pendingPushRouteProvider.notifier).state = route;
  }

  /// Traduit un lien universel en route interne. Renvoie null si non reconnu.
  ///   • `https://<domaine>/p/{id}` → `/product/{id}`  (fiche produit)
  ///   • `https://<domaine>/s/{id}` → `/shop/{id}`     (vitrine boutique)
  ///
  /// Statique et PURE : réutilisée par le service et testable seule.
  static String? routeForUri(Uri uri) {
    final segs = uri.pathSegments;
    if (segs.length >= 2 && segs[1].isNotEmpty) {
      switch (segs.first) {
        case 'p':
          return '/product/${segs[1]}';
        case 's':
          return '/shop/${segs[1]}';
      }
    }
    return null;
  }

  void dispose() => _sub?.cancel();
}

final deepLinkServiceProvider = Provider<DeepLinkService>((ref) {
  final service = DeepLinkService(ref);
  ref.onDispose(service.dispose);
  return service;
});
