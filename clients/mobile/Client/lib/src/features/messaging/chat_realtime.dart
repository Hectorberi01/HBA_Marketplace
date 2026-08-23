import 'package:signalr_netcore/signalr_client.dart';

import '../../core/config/app_config.dart';

/// Connexion SignalR au hub de messagerie, scoptée à une conversation.
///
/// Tolérante aux pannes : si la connexion échoue (réseau, ngrok, WebSocket
/// bloqué…), on ne lève pas — le chat continue de fonctionner via le polling.
class ChatRealtime {
  HubConnection? _conn;

  Future<void> connect({
    required String conversationId,
    required String? accessToken,
    required void Function() onMessage,
  }) async {
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

      conn.on('message', (_) => onMessage());
      await conn.start();
      await conn.invoke('JoinConversation', args: [conversationId]);
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
