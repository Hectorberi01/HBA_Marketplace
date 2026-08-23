import 'dart:io';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/identity/seller_identity.dart';
import '../../core/media/media_upload.dart';
import '../../core/network/api_base.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

/// Réaction agrégée : l'emoji, combien de personnes, et si j'en fais partie.
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

/// Pièce jointe d'un message (`MessageAttachmentSummary`).
class MessageAttachment {
  MessageAttachment({required this.mediaId, required this.type, required this.legacyUrl});

  /// CE N'EST PLUS UNE URL, ET C'ÉTAIT LA CAUSE DES IMAGES ABSENTES.
  ///
  /// Le modèle lisait une liste de CHAÎNES. Le contrat rend des OBJETS
  /// `{ mediaId, type, legacyUrl }` : `attachments.map((e) => e.toString())`
  /// produisait des « Instance of '_Map' », affichés comme des URLs cassées.
  ///
  /// Les pièces récentes n'ont qu'un `mediaId`, et communication-service n'expose
  /// AUCUNE route d'URL signée (`grep attachment` sur `HBA.Communication.Api` ne
  /// rend rien). Il faut donc passer par media-service — cf. [attachmentUrl].
  final String mediaId;

  /// `Image` | `Video` | `Audio` | `Document` | `Archive` | `Other`.
  final String type;

  /// URL directe des pièces ANTÉRIEURES à la bascule vers media-service. `null`
  /// pour toutes les nouvelles : ne pas la traiter comme la source principale.
  final String? legacyUrl;

  bool get isImage => type.toLowerCase() == 'image';

  factory MessageAttachment.fromJson(Map d) => MessageAttachment(
        mediaId: Json.str(d['mediaId']),
        type: Json.str(d['type'], 'Other'),
        legacyUrl: (d['legacyUrl']?.toString().isNotEmpty ?? false)
            ? d['legacyUrl'].toString()
            : null,
      );
}

class Message {
  Message({
    required this.id,
    required this.senderId,
    required this.body,
    required this.fromMe,
    required this.sentAt,
    this.readAt,
    this.isDeleted = false,
    this.reactions = const [],
    this.attachments = const [],
  });

  final String id;
  final String senderId;
  final String body;

  /// DÉDUIT DE `senderId`, ET NON D'UN CHAMP `fromSeller`.
  ///
  /// Le modèle lisait `d['fromSeller']`, absent de `MessageSummary` : la valeur
  /// était TOUJOURS `false`, donc TOUS les messages s'affichaient à gauche, y
  /// compris ceux du vendeur. Le contrat ne rend que `SenderId` ; l'identité du
  /// lecteur vient du socle (`SellerIdentity.userId`, résolu par
  /// `GET /api/merchants/me`).
  final bool fromMe;

  final DateTime? sentAt;

  /// Date de lecture par l'autre participant. Sert à l'accusé (✓ / ✓✓).
  final DateTime? readAt;

  bool get isRead => readAt != null;

  /// Supprimé pour tout le monde : le serveur remplace alors `body` par
  /// « Message supprimé », vide les pièces jointes et les réactions.
  final bool isDeleted;

  final List<MessageReaction> reactions;
  final List<MessageAttachment> attachments;

  factory Message.fromJson(Map d, {required String myUserId}) {
    final senderId = Json.str(d['senderId']);
    return Message(
      id: Json.str(d['id']),
      senderId: senderId,
      body: Json.str(d['body']),
      fromMe: myUserId.isNotEmpty && senderId == myUserId,
      sentAt: Json.asDate(d['createdAtUtc']),
      readAt: Json.asDate(d['readAtUtc']),
      isDeleted: Json.asBool(d['isDeleted']),
      reactions: Json.list(d['reactions']).map(MessageReaction.fromJson).toList(),
      attachments: Json.list(d['attachments']).map(MessageAttachment.fromJson).toList(),
    );
  }
}

/// Fil de discussion (`ConversationSummary`).
///
/// LA CONVERSATION EMBARQUE TOUS SES MESSAGES.
///
/// `GET /conversations` rend donc l'intégralité de chaque fil, sans pagination ni
/// curseur. Il n'y a PAS de route « messages seuls » : `messages(id)` ci-dessous
/// relit la conversation entière. Sur un vendeur bavard, le coût est réel — c'est
/// une limite du service, à signaler plutôt qu'à contourner par du cache.
class Conversation {
  Conversation({
    required this.id,
    required this.participantIds,
    required this.contextType,
    required this.contextId,
    required this.status,
    required this.lastAt,
    required this.messages,
    required this.unread,
  });

  final String id;

  /// IL N'Y A AUCUN NOM DE CLIENT, ET IL NE FAUT PAS EN FABRIQUER UN.
  ///
  /// Le modèle lisait un champ `customer` qui n'existe pas : la valeur retombait
  /// TOUJOURS sur « Client », pour tous les fils. Le contrat ne rend que des
  /// `ParticipantIds` (GUID). Résoudre le nom demanderait un appel à user-service
  /// par participant, sur des profils qu'un vendeur n'a pas à consulter en bloc.
  /// L'écran affiche donc un libellé générique assumé.
  final List<String> participantIds;

  /// De quoi on parle : `order`, `product`… `null` pour un fil libre.
  final String? contextType;
  final String? contextId;

  /// `Open` | `Archived` | `Blocked`.
  final String status;

  final DateTime? lastAt;
  final List<Message> messages;

  /// COMPTEUR DÉDUIT : `unreadCount` N'EXISTE PAS DANS LE CONTRAT.
  ///
  /// Le modèle lisait `unread` puis `unreadCount`, deux champs absents — le
  /// badge valait donc TOUJOURS zéro, et l'onglet Messages ne signalait jamais
  /// rien. On compte ici les messages REÇUS que l'on n'a pas encore marqués lus.
  /// C'est une approximation honnête du contrat disponible, et non un chiffre
  /// inventé : elle bouge quand le serveur bouge.
  final int unread;

  /// Dernier message affichable — pour l'aperçu de la liste.
  String get lastMessage {
    for (var i = messages.length - 1; i >= 0; i--) {
      final m = messages[i];
      if (m.body.trim().isNotEmpty) return m.body;
      if (m.attachments.isNotEmpty) return 'Pièce jointe';
    }
    return '';
  }

  bool get isArchived => status.toLowerCase() == 'archived';

  factory Conversation.fromJson(Map d, {required String myUserId}) {
    final messages =
        Json.list(d['messages']).map((m) => Message.fromJson(m, myUserId: myUserId)).toList();

    return Conversation(
      id: Json.str(d['id']),
      participantIds: (d['participantIds'] is List)
          ? (d['participantIds'] as List).map((e) => e.toString()).toList()
          : const <String>[],
      contextType: (d['contextType']?.toString().isNotEmpty ?? false)
          ? d['contextType'].toString()
          : null,
      contextId:
          (d['contextId']?.toString().isNotEmpty ?? false) ? d['contextId'].toString() : null,
      status: Json.str(d['status'], 'Open'),
      lastAt: Json.asDate(d['lastMessageAtUtc']),
      messages: messages,
      unread: messages.where((m) => !m.fromMe && !m.isRead && !m.isDeleted).length,
    );
  }
}

/// ═════════════════════════════════════════════════════════════════════════════
/// MESSAGERIE — communication-service, sous `/api/notifications/messaging`.
///
/// LE PRÉFIXE SURPREND, ET C'EST BIEN CELUI-LÀ.
///
/// La messagerie vit dans le MÊME service que les notifications, et la passerelle
/// route `/api/notifications/**` sans réécriture. Le chemin complet est donc
/// `/api/notifications/messaging/conversations`, et non `/api/messaging/…` —
/// qu'aucune entrée YARP ne connaît.
///
/// SONDAGE SEUL : IL N'Y A PAS DE HUB TEMPS RÉEL.
///
/// Aucun service HBA n'appelle `MapHub`, et la passerelle n'a aucune route
/// WebSocket. `chatHubPath` a disparu d'`AppConfig`, et la neutralisation vit
/// dans `features/messaging/chat_realtime.dart`. Un fil ne se met donc à jour
/// qu'au rafraîchissement — l'écran doit le refléter, pas le masquer par une
/// animation de frappe qui ne viendra jamais.
/// ═════════════════════════════════════════════════════════════════════════════
class MessagingApi extends ApiBase {
  MessagingApi(super.dio, this._media);

  final MediaApi _media;

  static const _p = '${AppConfig.notifications}/messaging/conversations';

  Future<List<Conversation>> conversations(String myUserId) => guard(() async {
        final resp = await dio.get(_p);
        final items = Json.list(resp.data)
            .map((e) => Conversation.fromJson(e, myUserId: myUserId))
            .toList();
        // Le fil le plus récent en premier : le serveur ne garantit pas l'ordre.
        items.sort((a, b) => (b.lastAt ?? DateTime(0)).compareTo(a.lastAt ?? DateTime(0)));
        return items;
      });

  /// Les messages d'un fil.
  ///
  /// CHARGER NE MARQUE PLUS RIEN COMME LU, CONTRAIREMENT À AVANT.
  ///
  /// `GET /conversations/{id}` est une lecture pure. Le marquage est un geste
  /// explicite : `PUT /conversations/{id}/read` — noter le `PUT`, alors que les
  /// notifications utilisent `POST /{id}/read`. L'appelant doit donc appeler
  /// [markRead] lui-même, sans quoi le badge de non-lus ne redescend jamais.
  Future<List<Message>> messages(String conversationId, String myUserId) => guard(() async {
        final resp = await dio.get('$_p/$conversationId');
        final data = Json.map(resp.data);
        return Json.list(data['messages'])
            .map((m) => Message.fromJson(m, myUserId: myUserId))
            .toList();
      });

  /// Envoie un message.
  ///
  /// RÉPOND 204 SANS CORPS : IL FAUT RELIRE LE FIL.
  ///
  /// L'ancienne signature rendait la `Map` du message créé ; `SendMessageAsync`
  /// ne renvoie rien. L'appelant doit invalider le fournisseur du fil.
  ///
  /// LES PIÈCES JOINTES SONT DES OBJETS, PAS DES URLS.
  ///
  /// `MessageAttachmentInput(Guid MediaId, string ContentType)`. Envoyer une
  /// liste de chaînes fait échouer la liaison du Guid.
  ///
  /// UN MESSAGE SANS TEXTE NI PIÈCE JOINTE EST REFUSÉ (validation serveur),
  /// et le corps est plafonné à 4000 caractères.
  Future<void> send(
    String conversationId,
    String body, {
    List<UploadedAttachment> attachments = const [],
  }) =>
      guard(() async {
        await dio.post('$_p/$conversationId/messages', data: {
          'body': body,
          'attachments': [
            for (final a in attachments)
              {'mediaId': a.mediaId, 'contentType': a.contentType},
          ],
        });
      });

  /// Téléverse une pièce jointe et rend de quoi la joindre à un message.
  ///
  /// IL N'Y A PAS DE ROUTE D'UPLOAD DANS communication-service. Le fichier va
  /// sur media-service en nature `Attachment` (restreinte), sous le
  /// propriétaire `User` — c'est le seul couple que le contrat permette : il
  /// n'existe ni `MediaOwnerType.Conversation` ni `MediaType.MessageAttachment`.
  Future<UploadedAttachment> uploadAttachment(File file, {required String myUserId}) async {
    final name = file.path.split(Platform.pathSeparator).last;
    final deposit = await _media.uploadBytes(
      bytes: await file.readAsBytes(),
      fileName: name,
      ownerType: MediaOwner.user,
      ownerId: myUserId,
      mediaType: MediaKind.attachment,
    );
    return UploadedAttachment(
      mediaId: deposit.mediaId,
      contentType: MediaApi.mediaTypeOf(name).mimeType,
    );
  }

  /// URL signée pour afficher une pièce jointe reçue.
  Future<String> attachmentUrl(MessageAttachment attachment) async =>
      attachment.legacyUrl ?? await _media.signedUrl(attachment.mediaId);

  /// Marque le fil comme lu. `PUT`, et non `POST`.
  Future<void> markRead(String conversationId) => guard(() async {
        await dio.put('$_p/$conversationId/read');
      });

  Future<void> archive(String conversationId) => guard(() async {
        await dio.post('$_p/$conversationId/archive');
      });

  /// Réagit à un message.
  ///
  /// `PUT .../reaction` AU SINGULIER — l'appel partait en
  /// `POST .../reactions` au pluriel, un chemin qui n'existe pas : la réaction
  /// disparaissait au rafraîchissement, sans message d'erreur.
  Future<void> react(String conversationId, String messageId, String emoji) => guard(() async {
        await dio.put('$_p/$conversationId/messages/$messageId/reaction', data: {'emoji': emoji});
      });

  /// Supprime pour tout le monde (ses propres messages — le serveur vérifie).
  Future<void> deleteForEveryone(String conversationId, String messageId) => guard(() async {
        await dio.delete('$_p/$conversationId/messages/$messageId');
      });

  /// Masque le message pour soi seulement.
  ///
  /// LE SUFFIXE EST `/mine`, PAS `/for-me`.
  Future<void> hideForMe(String conversationId, String messageId) => guard(() async {
        await dio.delete('$_p/$conversationId/messages/$messageId/mine');
      });
}

/// Pièce jointe prête à être attachée à un message.
class UploadedAttachment {
  const UploadedAttachment({required this.mediaId, required this.contentType});

  final String mediaId;
  final String contentType;
}

final messagingApiProvider = Provider<MessagingApi>(
    (ref) => MessagingApi(ref.watch(dioProvider), ref.watch(mediaApiProvider)));

/// L'identifiant du COMPTE connecté — pas celui du vendeur.
///
/// NE PAS CONFONDRE AVEC `sellerId`. Les messages sont signés par un
/// UTILISATEUR (`SenderId`), pas par une boutique. `SellerIdentity` porte les
/// deux, et `GET /api/merchants/me` est le seul endroit où les lire.
final currentUserIdProvider = FutureProvider<String>((ref) async {
  final seller = await ref.watch(sellerIdentityProvider.future);
  return seller?.userId ?? '';
});

final conversationsProvider = FutureProvider<List<Conversation>>((ref) async {
  final myUserId = await ref.watch(currentUserIdProvider.future);
  return ref.watch(messagingApiProvider).conversations(myUserId);
});

final messagesProvider = FutureProvider.family<List<Message>, String>((ref, id) async {
  final myUserId = await ref.watch(currentUserIdProvider.future);
  return ref.watch(messagingApiProvider).messages(id, myUserId);
});

/// Total des non-lus — alimente le badge de l'onglet Messages.
final unreadCountProvider = Provider<int>((ref) {
  final convs = ref.watch(conversationsProvider).valueOrNull;
  if (convs == null) return 0;
  return convs.fold<int>(0, (sum, c) => sum + c.unread);
});
