import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/async_views.dart';
import '../disputes_data.dart';

class DisputesScreen extends ConsumerWidget {
  const DisputesScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final disputes = ref.watch(disputesProvider);
    return Scaffold(
      backgroundColor: AppTheme.bg,
      appBar: AppBar(title: const Text('Mes litiges')),
      body: disputes.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(message: e.toString(), onRetry: () => ref.invalidate(disputesProvider)),
        data: (list) {
          if (list.isEmpty) {
            return const EmptyView(
              message: 'Aucun litige.\nVous pouvez en ouvrir un depuis le détail d\'une commande.',
              icon: Icons.gavel_outlined,
            );
          }
          return RefreshIndicator(
            onRefresh: () async => ref.refresh(disputesProvider.future),
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
              children: [for (final d in list) _DisputeCard(dispute: d)],
            ),
          );
        },
      ),
    );
  }
}

class _DisputeCard extends StatelessWidget {
  const _DisputeCard({required this.dispute});
  final Dispute dispute;

  String get _orderRef {
    final id = dispute.orderId.replaceAll('-', '');
    return 'CMD-${id.substring(0, id.length >= 8 ? 8 : id.length).toUpperCase()}';
  }

  @override
  Widget build(BuildContext context) {
    final (label, color) = _status(dispute.status);
    final last = dispute.messages.isNotEmpty ? dispute.messages.last : null;
    return GestureDetector(
      onTap: () => context.push('/dispute/${dispute.id}'),
      child: Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(14), border: Border.all(color: AppTheme.line)),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(children: [
          Text(_orderRef, style: TextStyle(fontSize: 11, fontWeight: FontWeight.w700, color: AppTheme.subtle)),
          const Spacer(),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
            decoration: BoxDecoration(color: color.withValues(alpha: 0.12), borderRadius: BorderRadius.circular(20)),
            child: Text(label, style: TextStyle(color: color, fontSize: 12, fontWeight: FontWeight.w700)),
          ),
        ]),
        const SizedBox(height: 8),
        Text(_type(dispute.type), style: const TextStyle(fontWeight: FontWeight.w700)),
        if (last != null)
          Padding(
            padding: const EdgeInsets.only(top: 4),
            child: Text(last.body, maxLines: 2, overflow: TextOverflow.ellipsis, style: TextStyle(color: AppTheme.subtle, height: 1.3)),
          ),
        if (dispute.createdAt != null)
          Padding(
            padding: const EdgeInsets.only(top: 4),
            child: Text('Ouvert le ${Format.date(dispute.createdAt)}', style: TextStyle(color: AppTheme.subtle, fontSize: 12)),
          ),
        const SizedBox(height: 8),
        Row(children: [
          GestureDetector(
            onTap: () => context.push('/order/${dispute.orderId}'),
            child: const Text('Voir la commande', style: TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w700, fontSize: 13)),
          ),
          const Spacer(),
          Text('Voir les échanges', style: TextStyle(color: AppTheme.subtle, fontWeight: FontWeight.w700, fontSize: 13)),
          Icon(Icons.chevron_right, size: 18, color: AppTheme.subtle),
        ]),
      ]),
      ),
    );
  }

  (String, Color) _status(String s) {
    switch (s.toLowerCase()) {
      case 'underreview':
        return ('En cours d\'examen', const Color(0xFF3B6FE0));
      case 'resolved':
        return ('Résolu', AppTheme.brandGreen);
      case 'escalated':
        return ('Escaladé', AppTheme.danger);
      default:
        return ('Ouvert', AppTheme.promoOrange);
    }
  }

  String _type(String t) {
    switch (t.toLowerCase()) {
      case 'notreceived':
        return 'Colis non reçu';
      case 'notconforming':
        return 'Produit non conforme';
      case 'damageditem':
        return 'Article endommagé';
      default:
        return 'Autre problème';
    }
  }
}
