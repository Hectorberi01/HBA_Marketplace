import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

class Dispute {
  Dispute({
    required this.id,
    required this.orderId,
    required this.type,
    required this.status,
    required this.resolution,
    required this.createdAt,
    required this.raisedBy,
    required this.messages,
  });

  final String id;
  final String orderId;
  final String type;
  final String status;
  final String? resolution;
  final DateTime? createdAt;

  /// Identifiant de l'acheteur qui a ouvert le litige. Sert à distinguer MES
  /// messages (auteur == raisedBy) des réponses du support.
  final String raisedBy;
  final List<DisputeMessage> messages;

  factory Dispute.fromJson(Map d) => Dispute(
        id: Json.str(d['id']),
        orderId: Json.str(d['orderId']),
        type: Json.str(d['type']),
        status: Json.str(d['status'], 'Open'),
        resolution: (d['resolution'])?.toString(),
        createdAt: Json.asDate(d['createdAtUtc'] ?? d['createdAt']),
        raisedBy: Json.str(d['raisedBy']),
        messages: Json.list(d['messages']).map(DisputeMessage.fromJson).toList(),
      );
}

class DisputeMessage {
  DisputeMessage({required this.body, required this.createdAt, required this.authorId, this.photoUrl});
  final String body;
  final DateTime? createdAt;
  final String authorId;
  final String? photoUrl;

  factory DisputeMessage.fromJson(Map d) {
    final photo = Json.str(d['photoUrl']);
    return DisputeMessage(
      body: Json.str(d['body']),
      createdAt: Json.asDate(d['createdAtUtc'] ?? d['createdAt']),
      authorId: Json.str(d['authorId']),
      photoUrl: photo.isEmpty ? null : photo,
    );
  }
}

class DisputesApi {
  DisputesApi(this._dio);
  final Dio _dio;
  static const _p = '${AppConfig.apiPrefix}/disputes';

  Future<List<Dispute>> list() async {
    try {
      final resp = await _dio.get(_p);
      return Json.list(resp.data).map(Dispute.fromJson).toList();
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  Future<void> open({required String orderId, required String type, required String message}) async {
    try {
      await _dio.post(_p, data: {'orderId': orderId, 'type': type, 'message': message, 'photoUrl': null});
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Détail d'un litige (fil complet des messages).
  Future<Dispute> detail(String id) async {
    try {
      final resp = await _dio.get('$_p/$id');
      return Dispute.fromJson(resp.data as Map);
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Répond à un litige (message de l'acheteur dans le fil), avec pièce jointe
  /// optionnelle (URL d'image déjà téléversée).
  Future<void> reply(String id, String message, {String? photoUrl}) async {
    try {
      await _dio.post('$_p/$id/messages', data: {'message': message, 'photoUrl': photoUrl});
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final disputesApiProvider = Provider<DisputesApi>((ref) => DisputesApi(ref.watch(dioProvider)));
final disputesProvider = FutureProvider<List<Dispute>>((ref) => ref.watch(disputesApiProvider).list());

/// Détail d'un litige (fil de messages), rechargé après chaque réponse.
final disputeDetailProvider =
    FutureProvider.family<Dispute, String>((ref, id) => ref.watch(disputesApiProvider).detail(id));
