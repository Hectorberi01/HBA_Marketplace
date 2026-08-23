import 'package:signalr_netcore/signalr_client.dart';

import '../../core/config/app_config.dart';

/// Connexion SignalR « boîte de réception », active pour toute la session.
///
/// Le serveur rattache automatiquement la connexion au groupe de l'utilisateur
/// (d'après le JWT) : on reçoit alors l'événement « inbox » à chaque nouveau
/// message reçu, même hors conversation ouverte — de quoi rafraîchir le badge
/// de messages non lus en temps réel.
///
/// Tolérante aux pannes : si la connexion échoue, on ne lève pas (le badge
/// reste alimenté par le polling de repli).
class InboxRealtime {
  HubConnection? _conn;

  Future<void> connect({
    required String? accessToken,
    required void Function() onInbox,
  }) async {
    if (_conn != null) return;
    try {
      final conn = HubConnectionBuilder()
          .withUrl(
            '${AppConfig.baseUrl}${AppConfig.apiPrefix}/hubs/chat',
            options: HttpConnectionOptions(
              accessTokenFactory: () async => accessToken ?? '',
            ),
          )
          .withAutomaticReconnect()
          .build();

      conn.on('inbox', (_) => onInbox());
      // Un nouveau message dans une conversation ouverte met aussi le badge à jour.
      conn.on('message', (_) => onInbox());
      await conn.start();
      _conn = conn;
    } catch (_) {
      _conn = null; // repli silencieux sur le polling
    }
  }

  Future<void> dispose() async {
    try {
      await _conn?.stop();
    } catch (_) {}
    _conn = null;
  }
}
