import 'package:flutter/material.dart';

import '../../core/formatters.dart';
import '../../core/models/wallet_entry.dart';
import '../../core/widgets/app_card.dart';
import '../../core/widgets/section_header.dart';
import 'withdraw_screen.dart';

class WalletScreen extends StatelessWidget {
  const WalletScreen({required this.entries, super.key});

  final List<WalletEntry> entries;

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
      children: [
        AppCard(
          padding: const EdgeInsets.all(20),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text('Solde disponible'),
              const SizedBox(height: 8),
              Text(
                formatXof(18450),
                style: Theme.of(context).textTheme.headlineMedium
                    ?.copyWith(fontWeight: FontWeight.w900),
              ),
              const SizedBox(height: 16),
              FilledButton.icon(
                onPressed: () {
                  Navigator.of(context).push(
                    MaterialPageRoute<void>(
                      builder: (_) => const WithdrawScreen(),
                    ),
                  );
                },
                icon: const Icon(Icons.payments_outlined),
                label: const Text('Demander un retrait'),
              ),
            ],
          ),
        ),
        const SizedBox(height: 18),
        const SectionHeader(title: 'Résumé de la semaine'),
        const SizedBox(height: 12),
        Row(
          children: const [
            Expanded(
              child: _MetricCard(label: 'Courses', value: '26'),
            ),
            SizedBox(width: 12),
            Expanded(
              child: _MetricCard(label: 'Gains', value: '41 200'),
            ),
          ],
        ),
        const SizedBox(height: 18),
        const SectionHeader(title: 'Mouvements'),
        const SizedBox(height: 12),
        for (final entry in entries) ...[
          _WalletEntryTile(entry: entry),
          const SizedBox(height: 10),
        ],
      ],
    );
  }
}

class _MetricCard extends StatelessWidget {
  const _MetricCard({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return AppCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label),
          const SizedBox(height: 4),
          Text(
            value,
            style: Theme.of(context).textTheme.titleLarge
                ?.copyWith(fontWeight: FontWeight.w900),
          ),
        ],
      ),
    );
  }
}

class _WalletEntryTile extends StatelessWidget {
  const _WalletEntryTile({required this.entry});

  final WalletEntry entry;

  @override
  Widget build(BuildContext context) {
    final positive = entry.amountXof >= 0;

    return AppCard(
      padding: EdgeInsets.zero,
      child: ListTile(
        leading: CircleAvatar(
          child: Icon(positive ? Icons.south_west : Icons.north_east, size: 18),
        ),
        title: Text(entry.label),
        subtitle: Text(entry.date),
        trailing: Text(
          formatXof(entry.amountXof),
          style: TextStyle(
            color: positive ? Colors.green.shade700 : Colors.red.shade700,
            fontWeight: FontWeight.w800,
          ),
        ),
      ),
    );
  }
}
