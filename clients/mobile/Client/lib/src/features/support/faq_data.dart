import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/network/api_exception.dart';
import '../../core/providers/core_providers.dart';
import '../../shared/utils/formatters.dart';

/// Une entrée de FAQ (question + réponse), servie par le CMS via le BFF.
class FaqItem {
  FaqItem({required this.question, required this.answer});
  final String question;
  final String answer;

  factory FaqItem.fromJson(Map d) => FaqItem(
        question: Json.str(d['question'], 'Question'),
        answer: Json.str(d['answer']),
      );
}

class FaqApi {
  FaqApi(this._dio);
  final Dio _dio;

  Future<List<FaqItem>> list() async {
    try {
      final resp = await _dio.get('${AppConfig.apiPrefix}/content/help_faq');
      return Json.list(resp.data).map(FaqItem.fromJson).toList();
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final faqApiProvider = Provider<FaqApi>((ref) => FaqApi(ref.watch(dioProvider)));

final faqProvider = FutureProvider<List<FaqItem>>((ref) => ref.watch(faqApiProvider).list());
