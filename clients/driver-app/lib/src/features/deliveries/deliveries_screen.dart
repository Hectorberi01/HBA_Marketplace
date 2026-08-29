import 'package:flutter/material.dart';

import '../../core/formatters.dart';
import '../../core/models/delivery_task.dart';
import '../../core/widgets/app_card.dart';
import '../../core/widgets/section_header.dart';
import '../../core/widgets/status_badge.dart';
import 'delivery_detail_screen.dart';

class DeliveriesScreen extends StatelessWidget {
  const DeliveriesScreen({
    required this.activeDelivery,
    required this.proposedDeliveries,
    required this.completedDeliveries,
    super.key,
  });

  final DeliveryTask activeDelivery;
  final List<DeliveryTask> proposedDeliveries;
  final List<DeliveryTask> completedDeliveries;

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
      children: [
        const SectionHeader(title: 'Livraison active'),
        const SizedBox(height: 12),
        _DeliveryListCard(task: activeDelivery),
        const SizedBox(height: 20),
        const SectionHeader(title: 'À traiter'),
        const SizedBox(height: 12),
        for (final task in proposedDeliveries) ...[
          _DeliveryListCard(task: task),
          const SizedBox(height: 12),
        ],
        const SizedBox(height: 8),
        const SectionHeader(title: 'Historique'),
        const SizedBox(height: 12),
        for (final task in completedDeliveries) ...[
          _DeliveryListCard(task: task),
          const SizedBox(height: 12),
        ],
      ],
    );
  }
}

class _DeliveryListCard extends StatelessWidget {
  const _DeliveryListCard({required this.task});

  final DeliveryTask task;

  @override
  Widget build(BuildContext context) {
    return AppCard(
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: () {
          Navigator.of(context).push(
            MaterialPageRoute<void>(
              builder: (_) => DeliveryDetailScreen(task: task),
            ),
          );
        },
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                StatusBadge(label: task.statusLabel),
                const Spacer(),
                Text(
                  formatXof(task.payoutXof),
                  style: Theme.of(context).textTheme.titleSmall
                      ?.copyWith(fontWeight: FontWeight.w900),
                ),
              ],
            ),
            const SizedBox(height: 12),
            Text(
              task.reference,
              style: Theme.of(context).textTheme.titleMedium
                  ?.copyWith(fontWeight: FontWeight.w800),
            ),
            const SizedBox(height: 6),
            Text('${task.pickupName} → ${task.dropoffName}'),
            const SizedBox(height: 8),
            Text(
              '${formatDistance(task.distanceKm)} • ${task.estimatedMinutes} min',
            ),
          ],
        ),
      ),
    );
  }
}
