import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../loyalty_data.dart';

class LoyaltyScreen extends ConsumerWidget {
  const LoyaltyScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final loyalty = ref.watch(loyaltyProvider);
    return Scaffold(
      backgroundColor: AppTheme.bg,
      appBar: AppBar(title: const Text('Fidélité')),
      body: loyalty.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(message: e.toString(), onRetry: () => ref.invalidate(loyaltyProvider)),
        data: (acc) => ListView(
          padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
          children: [
            _BalanceCard(account: acc, onRedeem: () => _redeem(context, ref, acc)),
            const SizedBox(height: 16),
            const Text('Historique', style: TextStyle(fontWeight: FontWeight.w800, fontSize: 16)),
            const SizedBox(height: 8),
            if (acc.transactions.isEmpty)
              const Padding(
                padding: EdgeInsets.only(top: 24),
                child: EmptyView(message: 'Aucune transaction de points.', icon: Icons.history),
              )
            else
              for (final t in acc.transactions) _TxRow(tx: t),
          ],
        ),
      ),
    );
  }

  Future<void> _redeem(BuildContext context, WidgetRef ref, LoyaltyAccount acc) async {
    if (acc.pointsBalance <= 0) {
      AppNotify.info(context, 'Aucun point à utiliser.');
      return;
    }
    final controller = TextEditingController(text: '${acc.pointsBalance}');
    final amount = await showDialog<int>(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('Utiliser mes points'),
        content: Column(mainAxisSize: MainAxisSize.min, children: [
          Text('Solde disponible : ${acc.pointsBalance} pts', style: TextStyle(color: AppTheme.subtle)),
          const SizedBox(height: 12),
          TextField(
            controller: controller,
            keyboardType: TextInputType.number,
            decoration: const InputDecoration(labelText: 'Points à utiliser'),
          ),
        ]),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Annuler')),
          FilledButton(
            onPressed: () => Navigator.pop(context, int.tryParse(controller.text.trim()) ?? 0),
            child: const Text('Utiliser'),
          ),
        ],
      ),
    );
    if (amount == null || amount <= 0) return;
    if (amount > acc.pointsBalance) {
      if (context.mounted) AppNotify.info(context, 'Solde insuffisant.');
      return;
    }
    try {
      await ref.read(loyaltyApiProvider).redeem(amount);
      ref.invalidate(loyaltyProvider);
      if (context.mounted) {
        AppNotify.success(context, '$amount points utilisés.');
      }
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
    }
  }
}

class _BalanceCard extends StatelessWidget {
  const _BalanceCard({required this.account, required this.onRedeem});
  final LoyaltyAccount account;
  final VoidCallback onRedeem;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(20),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(18),
        gradient: const LinearGradient(
          colors: [AppTheme.brandGreenDark, AppTheme.brandGreen],
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
        ),
      ),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(children: [
          const Icon(Icons.workspace_premium, color: Colors.white, size: 22),
          const SizedBox(width: 8),
          Text('Palier ${_tier(account.tier)}',
              style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w800, fontSize: 15)),
        ]),
        const SizedBox(height: 18),
        Text('${account.pointsBalance}',
            style: const TextStyle(color: Colors.white, fontSize: 40, fontWeight: FontWeight.w800, height: 1)),
        Text('points disponibles', style: TextStyle(color: Colors.white.withValues(alpha: 0.9))),
        const SizedBox(height: 4),
        Text('${account.lifetimePoints} points cumulés au total',
            style: TextStyle(color: Colors.white.withValues(alpha: 0.75), fontSize: 12)),
        const SizedBox(height: 16),
        SizedBox(
          width: double.infinity,
          child: FilledButton(
            onPressed: onRedeem,
            style: FilledButton.styleFrom(
              backgroundColor: Colors.white,
              foregroundColor: AppTheme.brandGreenDark,
              minimumSize: const Size.fromHeight(46),
            ),
            child: const Text('Utiliser mes points', style: TextStyle(fontWeight: FontWeight.w800)),
          ),
        ),
      ]),
    );
  }

  String _tier(String t) {
    switch (t.toLowerCase()) {
      case 'gold':
        return 'Or';
      case 'silver':
        return 'Argent';
      default:
        return 'Bronze';
    }
  }
}

class _TxRow extends StatelessWidget {
  const _TxRow({required this.tx});
  final LoyaltyTransaction tx;

  @override
  Widget build(BuildContext context) {
    final positive = tx.amount >= 0;
    return Container(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12), border: Border.all(color: AppTheme.line)),
      child: Row(children: [
        Container(
          width: 36, height: 36,
          decoration: BoxDecoration(
            color: (positive ? AppTheme.brandGreen : AppTheme.danger).withValues(alpha: 0.12),
            borderRadius: BorderRadius.circular(10),
          ),
          child: Icon(positive ? Icons.add : Icons.remove, color: positive ? AppTheme.brandGreen : AppTheme.danger, size: 20),
        ),
        const SizedBox(width: 12),
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(_reason(tx.reason), style: const TextStyle(fontWeight: FontWeight.w700)),
            if (tx.createdAt != null)
              Text(Format.date(tx.createdAt), style: TextStyle(color: AppTheme.subtle, fontSize: 12)),
          ]),
        ),
        Text('${positive ? '+' : ''}${tx.amount}',
            style: TextStyle(fontWeight: FontWeight.w800, color: positive ? AppTheme.brandGreen : AppTheme.danger)),
      ]),
    );
  }

  String _reason(String r) {
    switch (r.toLowerCase()) {
      case 'purchase':
        return 'Achat';
      case 'cashback':
        return 'Cashback';
      case 'referral':
        return 'Parrainage';
      case 'redemption':
        return 'Utilisation de points';
      case 'expiry':
        return 'Expiration';
      default:
        return r;
    }
  }
}
