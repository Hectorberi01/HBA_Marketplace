import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/async_views.dart';
import '../returns_data.dart';

class ReturnsScreen extends ConsumerWidget {
  const ReturnsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final returns = ref.watch(returnsProvider);
    return Scaffold(
      backgroundColor: AppTheme.bg,
      appBar: AppBar(title: const Text('Mes retours')),
      body: returns.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(message: e.toString(), onRetry: () => ref.invalidate(returnsProvider)),
        data: (list) {
          if (list.isEmpty) {
            return const EmptyView(message: 'Aucune demande de retour.', icon: Icons.assignment_return_outlined);
          }
          return RefreshIndicator(
            onRefresh: () async => ref.refresh(returnsProvider.future),
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
              children: [for (final r in list) _ReturnCard(request: r)],
            ),
          );
        },
      ),
    );
  }
}

class _ReturnCard extends StatelessWidget {
  const _ReturnCard({required this.request});
  final ReturnRequest request;

  String get _orderRef {
    final id = request.orderId.replaceAll('-', '');
    return 'CMD-${id.substring(0, id.length >= 8 ? 8 : id.length).toUpperCase()}';
  }

  @override
  Widget build(BuildContext context) {
    final (label, color) = _status(request.status);
    return Container(
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
        Text('Motif : ${_reason(request.reason)}', style: const TextStyle(fontWeight: FontWeight.w600)),
        if (request.createdAt != null)
          Padding(
            padding: const EdgeInsets.only(top: 2),
            child: Text('Demandé le ${Format.date(request.createdAt)}', style: TextStyle(color: AppTheme.subtle, fontSize: 12)),
          ),
        if (request.refundAmount != null) ...[
          const SizedBox(height: 6),
          Text('Remboursement : ${Format.money(request.refundAmount, request.currency)}',
              style: const TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w800)),
        ],
        if (request.trackingNumber != null) ...[
          const SizedBox(height: 6),
          Text('Suivi : ${request.carrier ?? ''} ${request.trackingNumber}', style: TextStyle(color: AppTheme.subtle, fontSize: 12)),
        ],
        const SizedBox(height: 8),
        GestureDetector(
          onTap: () => context.push('/order/${request.orderId}'),
          child: const Text('Voir la commande', style: TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w700, fontSize: 13)),
        ),
      ]),
    );
  }

  (String, Color) _status(String s) {
    switch (s.toLowerCase()) {
      case 'approved':
        return ('Approuvé', const Color(0xFF3B6FE0));
      case 'received':
        return ('Reçu', const Color(0xFF8A5CD1));
      case 'refunded':
        return ('Remboursé', AppTheme.brandGreen);
      case 'rejected':
        return ('Refusé', AppTheme.danger);
      default:
        return ('En attente', AppTheme.promoOrange);
    }
  }

  String _reason(String r) {
    switch (r.toLowerCase()) {
      case 'defective':
        return 'Produit défectueux';
      case 'notasdescribed':
        return 'Non conforme';
      case 'changedmind':
        return 'Changement d\'avis';
      case 'wrongitem':
        return 'Mauvais article';
      default:
        return r.isEmpty ? 'Autre' : r;
    }
  }
}
