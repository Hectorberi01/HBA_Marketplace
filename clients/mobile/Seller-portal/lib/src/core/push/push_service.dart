import 'dart:io';

import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../features/account/account_data.dart';
import '../../features/messaging/messaging_data.dart';
import '../../features/orders/orders_data.dart';

/// Destination demandée par une notification tapée, en attente d'être consommée
/// par l'app. Un simple état : le service push n'a pas de contexte de navigation,
/// et au démarrage à froid le routeur n'existe pas encore quand le message arrive.
final pendingPushRouteProvider = StateProvider<String?>((ref) => null);

/// Résultat d'une activation MANUELLE des push sur cet appareil (bouton dans les
/// préférences). Chaque cas donne un message précis à l'utilisateur.
enum PushEnableOutcome {
  /// Jeton obtenu et enregistré côté serveur — tout est bon.
  registered,

  /// L'utilisateur a refusé la permission : à réactiver dans les Réglages système.
  permissionDenied,

  /// iOS n'a pas fourni de jeton APNs (simulateur, capacité/profil Push manquant,
  /// ou APNs simplement lent). Sur Android, cas très rare.
  apnsUnavailable,

  /// FCM n'a renvoyé aucun jeton malgré un APNs présent.
  noToken,

  /// Échec réseau/serveur lors de l'enregistrement du jeton.
  error,
}

/// Notifications push (Firebase Cloud Messaging).
///
/// Une commande n'a de valeur que si le vendeur l'apprend tout de suite : sans
/// push, il découvre ses ventes en ouvrant l'app, et le délai de préparation
/// annoncé au client est déjà entamé.
///
/// TOLÉRANT AUX PANNES, volontairement : si Firebase n'est pas configuré (fichier
/// google-services.json / GoogleService-Info.plist absent), si le vendeur refuse
/// la permission, ou si le réseau échoue, l'app continue de fonctionner
/// normalement — sans push. Faire planter une app de gestion parce qu'une
/// notification n'a pas pu s'abonner serait absurde.
class PushService {
  PushService(this._ref);

  final Ref _ref;

  /// Jeton courant, conservé pour pouvoir le RÉVOQUER à la déconnexion.
  String? _token;

  bool _started = false;

  /// À appeler une fois la session ouverte : le jeton s'enregistre sur le
  /// compte du vendeur connecté, jamais avant.
  Future<void> start() async {
    if (_started) return;

    try {
      await Firebase.initializeApp();

      final messaging = FirebaseMessaging.instance;

      // iOS exige un consentement explicite. Android 13+ aussi.
      final settings = await messaging.requestPermission();
      if (settings.authorizationStatus == AuthorizationStatus.denied) {
        // Refus assumé : on n'insiste pas et on n'enregistre aucun jeton.
        _started = true;
        return;
      }

      // Les ÉCOUTEURS d'abord, le jeton ensuite — l'ordre compte.
      //
      // Sur iOS, `getToken()` lève « apns-token-not-set » tant qu'APNs n'a pas
      // rendu son jeton. Si on l'appelait en premier, l'exception emporterait
      // tout le reste de `start()` : plus d'écoute des messages, plus de
      // navigation au tap, et `_started = true` interdirait toute reprise.
      // Placés ici, les écouteurs survivent à l'absence de jeton — et
      // `onTokenRefresh` rattrapera l'abonnement dès qu'APNs répondra.
      messaging.onTokenRefresh.listen(_register);

      // Push reçu app ouverte : le système n'affiche rien. On rafraîchit les
      // données pour que le badge et les listes reflètent l'événement — sinon le
      // vendeur voit une notification « fantôme » sans rien de nouveau à l'écran.
      FirebaseMessaging.onMessage.listen((_) => _refresh());

      // Push tapé, app en arrière-plan.
      FirebaseMessaging.onMessageOpenedApp.listen(_handleTap);

      // Push tapé, app fermée : le message ayant lancé l'app.
      final initial = await messaging.getInitialMessage();
      if (initial != null) _handleTap(initial);

      await _subscribe(messaging);

      _started = true;
    } catch (e) {
      // Firebase non configuré, ou service indisponible : on continue sans push.
      debugPrint('Push indisponible : $e');
      _started = true;
    }
  }

  /// Active/ré-enregistre EXPLICITEMENT les push sur cet appareil, à la demande de
  /// l'utilisateur (bouton dans les préférences de notification).
  ///
  /// Pourquoi une action manuelle en plus de [start] ? Sur iOS, le jeton APNs est
  /// délivré de façon asynchrone APRÈS l'accord de l'utilisateur ; au tout premier
  /// lancement il arrive parfois trop tard pour la fenêtre d'attente de [start], et
  /// l'enregistrement ne se refait plus dans la session. Ce bouton relance toute la
  /// séquence quand le jeton est, lui, prêt — et renvoie un résultat PRÉCIS pour
  /// dire à l'utilisateur ce qui bloque (permission, APNs, réseau).
  Future<PushEnableOutcome> enableOnThisDevice() async {
    try {
      await Firebase.initializeApp();
      final messaging = FirebaseMessaging.instance;

      // Redemande la permission. Si elle a déjà été refusée, iOS/Android ne
      // represente PAS la boîte de dialogue : le statut revient « denied » et
      // l'utilisateur devra l'activer dans les Réglages système.
      final settings = await messaging.requestPermission();
      if (settings.authorizationStatus == AuthorizationStatus.denied) {
        return PushEnableOutcome.permissionDenied;
      }

      // Rattache les écouteurs si [start] n'a pas (ou mal) tourné, pour que les
      // messages reçus/tapés soient traités après activation manuelle.
      if (!_started) {
        messaging.onTokenRefresh.listen(_register);
        FirebaseMessaging.onMessage.listen((_) => _refresh());
        FirebaseMessaging.onMessageOpenedApp.listen(_handleTap);
        _started = true;
      }

      // iOS : sans jeton APNs, FCM refuse d'émettre son jeton. On patiente.
      if (Platform.isIOS && !await _awaitApnsToken(messaging)) {
        return PushEnableOutcome.apnsUnavailable;
      }

      final token = await messaging.getToken();
      if (token == null || token.isEmpty) {
        return PushEnableOutcome.noToken;
      }

      // Enregistrement serveur : ici on veut CONNAÎTRE l'échec (contrairement à
      // [_register] qui l'avale), pour le remonter à l'utilisateur.
      try {
        await _ref.read(accountApiProvider).registerDevice(
              token: token,
              platform: Platform.isIOS ? 'ios' : 'android',
            );
        _token = token;
        return PushEnableOutcome.registered;
      } catch (e) {
        debugPrint("enableOnThisDevice : enregistrement serveur échoué : $e");
        return PushEnableOutcome.error;
      }
    } catch (e) {
      debugPrint('enableOnThisDevice : $e');
      return PushEnableOutcome.error;
    }
  }

  /// Récupère le jeton FCM et l'enregistre. Isolé dans son propre try/catch :
  /// un échec d'abonnement ne doit jamais empêcher l'app de fonctionner.
  Future<void> _subscribe(FirebaseMessaging messaging) async {
    try {
      if (Platform.isIOS && !await _awaitApnsToken(messaging)) {
        // Simulateur iOS (pas d'APNs), ou capacité Push absente du profil de
        // signature. Ce n'est pas une erreur : l'app tourne, simplement sans push.
        debugPrint('Push : jeton APNs indisponible — abonnement FCM ignoré.');
        return;
      }

      final token = await messaging.getToken();
      if (token != null && token.isNotEmpty) {
        await _register(token);
      }
    } catch (e) {
      debugPrint('Abonnement push impossible : $e');
    }
  }

  /// Attend le jeton APNs, que le système délivre de façon ASYNCHRONE après
  /// l'accord de l'utilisateur.
  ///
  /// FCM le refuse tant qu'il n'est pas là ; l'appeler trop tôt — ce que nous
  /// faisions — donne « apns-token-not-set ». On patiente donc un peu, en
  /// espaçant les tentatives, puis on renonce proprement.
  Future<bool> _awaitApnsToken(FirebaseMessaging messaging) async {
    for (var attempt = 1; attempt <= 5; attempt++) {
      final apns = await messaging.getAPNSToken();
      if (apns != null && apns.isNotEmpty) return true;
      await Future<void>.delayed(Duration(milliseconds: 400 * attempt));
    }
    return false;
  }

  /// Un push signale un événement serveur : on invalide ce qui a pu changer.
  void _refresh() {
    _ref.invalidate(notificationsProvider);
    _ref.invalidate(conversationsProvider);
    _ref.invalidate(ordersProvider);
  }

  /// Le vendeur a tapé la notification : on l'emmène là où il doit agir.
  ///
  /// Le backend n'envoie que le TYPE d'entité (« order », « message »…), pas son
  /// identifiant : on ouvre donc la bonne LISTE, pas la fiche précise. Prétendre
  /// ouvrir une commande sans en connaître l'id produirait un écran vide.
  void _handleTap(RemoteMessage message) {
    _refresh();

    final type = (message.data['type'] ?? '').toString().toLowerCase();
    final route = switch (type) {
      'order' => '/orders',
      'shipment' => '/shipments',
      'message' || 'conversation' => '/messages',
      'review' => '/reviews',
      'withdrawal' || 'payout' => '/wallet',
      _ => '/notifications',
    };

    // On ne navigue pas depuis le service : il n'a pas de contexte. On dépose
    // l'intention, l'app la consomme quand elle est prête (au démarrage à froid,
    // le routeur n'existe pas encore au moment où le message arrive).
    _ref.read(pendingPushRouteProvider.notifier).state = route;
  }

  Future<void> _register(String token) async {
    _token = token;
    try {
      await _ref.read(accountApiProvider).registerDevice(
            token: token,
            platform: Platform.isIOS ? 'ios' : 'android',
          );
    } catch (e) {
      debugPrint("Enregistrement du jeton push impossible : $e");
    }
  }

  /// À la déconnexion : on retire le jeton AVANT que la session ne se ferme,
  /// sinon l'appel partirait sans autorisation. Sans cela, l'appareil
  /// continuerait de recevoir les notifications du vendeur précédent.
  Future<void> stop() async {
    final token = _token;
    _token = null;
    _started = false;
    if (token == null) return;

    try {
      await _ref.read(accountApiProvider).unregisterDevice(token);
    } catch (e) {
      debugPrint('Désabonnement push impossible : $e');
    }
  }
}

final pushServiceProvider = Provider<PushService>((ref) => PushService(ref));
