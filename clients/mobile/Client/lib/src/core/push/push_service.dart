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

/// Notifications push (Firebase Cloud Messaging) — application CLIENT.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POURQUOI CE FICHIER N'EXISTAIT PAS, ET POURQUOI C'ÉTAIT GRAVE
///
/// Le serveur créait bien les notifications (« Commande confirmée », « Colis
/// expédié »), les persistait, et appelait FCM. Mais cette application n'avait
/// AUCUN Firebase : pas de dépendance, pas de google-services.json, et elle
/// n'envoyait jamais son jeton d'appareil.
///
/// Le serveur cherchait donc les appareils de l'acheteur, n'en trouvait aucun,
/// journalisait un avertissement, et s'arrêtait là. En silence.
///
/// Concrètement : le client n'apprenait que sa commande était confirmée ou
/// expédiée QUE s'il ouvrait l'app et allait consulter sa boîte de réception. Un
/// colis pouvait arriver sans qu'il ait jamais été prévenu de son départ.
/// ─────────────────────────────────────────────────────────────────────────────
///
/// TOLÉRANT AUX PANNES, volontairement : si Firebase n'est pas configuré, si le
/// client refuse la permission, ou si le réseau échoue, l'app continue de
/// fonctionner normalement — sans push. Empêcher quelqu'un d'acheter parce qu'une
/// notification n'a pas pu s'abonner serait absurde.
class PushService {
  PushService(this._ref);

  final Ref _ref;

  /// Jeton courant, conservé pour pouvoir le RÉVOQUER à la déconnexion.
  String? _token;

  bool _started = false;

  /// À appeler une fois la session ouverte : le jeton s'enregistre sur le compte
  /// de l'acheteur connecté, jamais avant. Un jeton envoyé sans session partirait
  /// sans autorisation et serait rejeté (401).
  Future<void> start() async {
    if (_started) return;

    try {
      await Firebase.initializeApp();

      final messaging = FirebaseMessaging.instance;

      // iOS exige un consentement explicite. Android 13+ également.
      final settings = await messaging.requestPermission();
      if (settings.authorizationStatus == AuthorizationStatus.denied) {
        // Refus assumé : on n'insiste pas, et on n'enregistre aucun jeton.
        _started = true;
        return;
      }

      // Les ÉCOUTEURS d'abord, le jeton ensuite — l'ordre compte.
      //
      // Sur iOS, `getToken()` lève « apns-token-not-set » tant qu'APNs n'a pas
      // rendu son jeton. Appelé en premier, l'exception emporterait tout le reste
      // de `start()` : plus d'écoute des messages, plus de navigation au tap, et
      // `_started = true` interdirait toute reprise. Placés ici, les écouteurs
      // survivent à l'absence de jeton — et `onTokenRefresh` rattrapera
      // l'abonnement dès qu'APNs répondra.
      messaging.onTokenRefresh.listen(_register);

      // Push reçu app OUVERTE : le système n'affiche rien de lui-même. On
      // rafraîchit les données pour que le badge et les listes reflètent
      // l'événement — sinon le client verrait une notification « fantôme » sans
      // rien de nouveau à l'écran.
      FirebaseMessaging.onMessage.listen((_) => _refresh());

      // Push tapé, app en arrière-plan.
      FirebaseMessaging.onMessageOpenedApp.listen(_handleTap);

      // Push tapé, app FERMÉE : c'est le message qui a lancé l'app.
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

  /// Récupère le jeton FCM et l'enregistre. Isolé dans son propre try/catch : un
  /// échec d'abonnement ne doit jamais empêcher l'app de fonctionner.
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
  /// l'accord de l'utilisateur. FCM le refuse tant qu'il n'est pas là.
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
    _ref.invalidate(ordersListProvider);
    _ref.invalidate(conversationsProvider);
  }

  /// Le client a tapé la notification : on l'emmène là où l'information se trouve.
  ///
  /// Le backend envoie le TYPE d'entité (« Order », « Shipment »…) dans `data`,
  /// et son identifiant dans `entityId` — voir NotificationDispatcher.SendPushAsync.
  /// On s'en sert pour ouvrir la FICHE précise quand on la connaît, et seulement la
  /// liste sinon. Router vers `/order/:id` sans identifiant produirait un écran vide.
  void _handleTap(RemoteMessage message) {
    _refresh();

    final type = (message.data['type'] ?? '').toString().toLowerCase();
    final entityId = (message.data['entityId'] ?? '').toString();
    final hasId = entityId.isNotEmpty;

    final route = switch (type) {
      // « Commande confirmée », « Commande annulée » : on ouvre SA commande.
      'order' => hasId ? '/order/$entityId' : '/orders',

      // « Colis expédié / livré » : l'identifiant est celui de l'EXPÉDITION, pas
      // de la commande. On ne peut donc pas construire /order/:id/tracking avec
      // lui — ce serait une route valide menant à une commande inexistante. La
      // liste des commandes est la bonne destination : le client y retrouve la
      // sienne, et son suivi.
      'shipment' => '/orders',

      'message' || 'conversation' => hasId ? '/chat/$entityId' : '/conversations',
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
  /// sinon l'appel partirait sans autorisation.
  ///
  /// Sans cela, l'appareil continuerait de recevoir les notifications du client
  /// précédent — ses commandes, ses colis. Sur un téléphone partagé, c'est une
  /// fuite de données personnelles, pas une simple gêne.
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
