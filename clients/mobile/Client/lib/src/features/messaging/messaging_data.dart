import 'dart:io';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:http_parser/http_parser.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

class Conversation {
  Conversation({required this.id, required this.title, required this.lastMessage, required this.updatedAt, required this.unread});
  final String id;
  final String title;
  final String lastMessage;
  final DateTime? updatedAt;
  final int unread;

  factory Conversation.fromJson(Map d) => Conversation(
        id: Json.str(d['id'] ?? d['conversationId']),
        title: Json.str(d['title'] ?? d['sellerName'] ?? d['subject'], 'Conversation'),
        lastMessage: Json.str(d['lastMessage'] ?? d['lastMessagePreview']),
        updatedAt: Json.asDate(d['updatedAtUtc'] ?? d['lastMessageAtUtc'] ?? d['updatedAt']),
        unread: Json.asInt(d['unreadCount'] ?? d['unread']),
      );
}

/// Réaction agrégée sur un message : l'emoji, combien de personnes l'ont posé,
/// et si l'utilisateur courant en fait partie (pastille en surbrillance).
class MessageReaction {
  MessageReaction({required this.emoji, required this.count, required this.mine});
  final String emoji;
  final int count;
  final bool mine;

  factory MessageReaction.fromJson(Map d) => MessageReaction(
        emoji: Json.str(d['emoji']),
        count: Json.asInt(d['count']),
        mine: Json.asBool(d['mine']),
      );
}

/// Palette autorisée — doit rester alignée sur le domaine serveur, qui la valide.
const kReactionPalette = <String>['👍', '❤️', '😂', '😮', '😢', '🙏'];

class Message {
  Message({
    required this.id,
    required this.body,
    required this.fromMe,
    required this.sentAt,
    this.isDeleted = false,
    this.reactions = const [],
    this.attachments = const [],
  });
  final String id;
  final String body;
  final bool fromMe;
  final DateTime? sentAt;

  /// Supprimé pour tout le monde : `body` vaut alors « Message supprimé ».
  final bool isDeleted;
  final List<MessageReaction> reactions;

  /// URLs des images jointes au message.
  final List<String> attachments;

  factory Message.fromJson(Map d, {String? selfMarker}) {
    final senderRole = Json.str(d['senderRole'] ?? d['authorRole']).toLowerCase();
    final fromMe = Json.asBool(d['fromMe']) ||
        senderRole == 'buyer' ||
        senderRole == 'customer';
    return Message(
      id: Json.str(d['id']),
      body: Json.str(d['body'] ?? d['content']),
      fromMe: fromMe,
      sentAt: Json.asDate(d['sentAtUtc'] ?? d['createdAtUtc'] ?? d['sentAt']),
      isDeleted: Json.asBool(d['isDeleted']),
      reactions: Json.list(d['reactions']).map(MessageReaction.fromJson).toList(),
      // `attachments` est une liste de CHAÎNES (URLs), pas d'objets JSON : on ne
      // peut pas utiliser Json.list (qui ne garde que les Map) — elle reviendrait
      // toujours vide.
      attachments: (d['attachments'] is List)
          ? (d['attachments'] as List).map((e) => e.toString()).where((s) => s.trim().isNotEmpty).toList()
          : const <String>[],
    );
  }
}

class MessagingApi {
  MessagingApi(this._dio);
  final Dio _dio;
  static const _p = '${AppConfig.apiPrefix}/conversations';

  Future<List<Conversation>> conversations() => _wrap(() async {
        final resp = await _dio.get('$_p/');
        return Json.list(resp.data).map(Conversation.fromJson).toList();
      });

  Future<List<Message>> messages(String conversationId) => _wrap(() async {
        final resp = await _dio.get('$_p/$conversationId/messages');
        return Json.list(resp.data).map((m) => Message.fromJson(m)).toList();
      });

  Future<void> send(String conversationId, String body, {List<String> attachments = const []}) => _wrap(() async {
        await _dio.post('$_p/$conversationId/messages', data: {
          'body': body,
          'attachments': attachments.isEmpty ? null : attachments,
        });
      });

  /// Téléverse une image de pièce jointe (multipart « file ») et renvoie son URL,
  /// à joindre ensuite au message via [send].
  Future<String> uploadAttachment(File file) => _wrap(() async {
        final name = file.path.split(Platform.pathSeparator).last;
        final form = FormData();
        form.files.add(MapEntry(
          'file',
          await MultipartFile.fromFile(file.path, filename: name, contentType: _mediaType(name)),
        ));
        final resp = await _dio.post('$_p/attachments', data: form);
        final data = resp.data;
        final url = data is Map ? Json.str(data['url']) : '';
        if (url.trim().isEmpty) {
          throw ApiException("L'image n'a pas pu être téléversée (aucune URL renvoyée par le serveur).");
        }
        return url;
      });

  static MediaType _mediaType(String fileName) {
    final ext = fileName.toLowerCase();
    if (ext.endsWith('.png')) return MediaType('image', 'png');
    if (ext.endsWith('.webp')) return MediaType('image', 'webp');
    return MediaType('image', 'jpeg');
  }

  /// Réagit à un message. Le serveur bascule : même emoji = retire, autre = remplace.
  Future<void> react(String conversationId, String messageId, String emoji) => _wrap(() async {
        await _dio.post('$_p/$conversationId/messages/$messageId/reactions', data: {'emoji': emoji});
      });

  /// Supprime pour tout le monde (ses propres messages uniquement — le serveur vérifie).
  Future<void> deleteForEveryone(String conversationId, String messageId) => _wrap(() async {
        await _dio.delete('$_p/$conversationId/messages/$messageId');
      });

  /// Masque le message pour soi seulement (l'autre participant continue de le voir).
  Future<void> hideForMe(String conversationId, String messageId) => _wrap(() async {
        await _dio.delete('$_p/$conversationId/messages/$messageId/for-me');
      });

  /// Démarre (ou ouvre) une conversation avec un vendeur ; renvoie l'id du fil.
  Future<String> startWithSeller(String sellerId, {String? message}) => _wrap(() async {
        final resp = await _dio.post('$_p/seller/$sellerId', data: {'message': message});
        final data = resp.data;
        return data is Map ? Json.str(data['conversationId'] ?? data['id']) : '';
      });

  Future<T> _wrap<T>(Future<T> Function() fn) async {
    try {
      return await fn();
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final messagingApiProvider = Provider<MessagingApi>((ref) => MessagingApi(ref.watch(dioProvider)));
final conversationsProvider = FutureProvider<List<Conversation>>((ref) => ref.watch(messagingApiProvider).conversations());

/// Total des messages non lus, dérivé des conversations — pour le badge navbar.
final unreadCountProvider = Provider<int>((ref) {
  final convs = ref.watch(conversationsProvider).valueOrNull;
  if (convs == null) return 0;
  return convs.fold<int>(0, (sum, c) => sum + c.unread);
});
final messagesProvider =
    FutureProvider.family<List<Message>, String>((ref, id) => ref.watch(messagingApiProvider).messages(id));
