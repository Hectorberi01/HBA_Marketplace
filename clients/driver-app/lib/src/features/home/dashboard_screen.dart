import 'package:flutter/material.dart';

import '../../core/formatters.dart';
import '../../core/models/delivery_task.dart';
import '../../core/widgets/app_card.dart';
import '../../core/widgets/section_header.dart';
import '../../core/widgets/status_badge.dart';
import '../deliveries/delivery_detail_screen.dart';

class DashboardScreen extends StatelessWidget {
  const DashboardScreen({
    required this.available,
    required this.activeDelivery,
    required this.proposedDeliveries,
    required this.onAvailabilityChanged,
    super.key,
  });

  final bool available;
  final DeliveryTask activeDelivery;
  final List<DeliveryTask> proposedDeliveries;
  final ValueChanged<bool> onAvailabilityChanged;

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
      children: [
        AppCard(
          child: Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      available ? 'Disponible' : 'Hors ligne',
                      style: Theme.of(context).textTheme.titleLarge
                          ?.copyWith(fontWeight: FontWeight.w800),
                    ),
                    const SizedBox(height: 4),
                    Text(
                      available
                          ? 'Les nouvelles courses peuvent arriver.'
                          : 'Activez votre statut pour recevoir des courses.',
                    ),
                  ],
                ),
              ),
              Switch(value: available, onChanged: onAvailabilityChanged),
            ],
          ),
        ),
        const SizedBox(height: 16),
        _ActiveDeliveryCard(task: activeDelivery),
        const SizedBox(height: 18),
        const SectionHeader(title: 'Propositions disponibles'),
        const SizedBox(height: 12),
        for (final task in proposedDeliveries) ...[
          _DeliveryProposalCard(task: task),
          const SizedBox(height: 12),
        ],
      ],
    );
  }
}

class _ActiveDeliveryCard extends StatelessWidget {
  const _ActiveDeliveryCard({required this.task});

  final DeliveryTask task;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;

    return AppCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              StatusBadge(
                label: task.statusLabel,
                icon: Icons.route,
                emphasized: true,
              ),
              const Spacer(),
              Text(
                task.reference,
                style: Theme.of(context).textTheme.labelLarge?.copyWith(
                  fontWeight: FontWeight.w800,
                  color: colors.primary,
                ),
              ),
            ],
          ),
          const SizedBox(height: 16),
          Text(
            'Course en cours',
            style: Theme.of(context).textTheme.titleLarge
                ?.copyWith(fontWeight: FontWeight.w900),
          ),
          const SizedBox(height: 12),
          _StopLine(
            icon: Icons.storefront,
            title: task.pickupName,
            subtitle: task.pickupAddress,
          ),
          const SizedBox(height: 12),
          _StopLine(
            icon: Icons.location_on,
            title: task.dropoffName,
            subtitle: task.dropoffAddress,
          ),
          const SizedBox(height: 16),
          Row(
            children: [
              Expanded(
                child: FilledButton.icon(
                  onPressed: () => _openDetail(context, task),
                  icon: const Icon(Icons.visibility_outlined),
                  label: const Text('Voir la course'),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _DeliveryProposalCard extends StatelessWidget {
  const _DeliveryProposalCard({required this.task});

  final DeliveryTask task;

  @override
  Widget build(BuildContext context) {
    return AppCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              StatusBadge(
                label: task.typeLabel,
                icon: task.type == DeliveryType.food
                    ? Icons.restaurant
                    : Icons.inventory_2_outlined,
              ),
              const Spacer(),
              Text(
                formatXof(task.payoutXof),
                style: Theme.of(context).textTheme.titleMedium
                    ?.copyWith(fontWeight: FontWeight.w900),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Text(
            task.pickupName,
            style: Theme.of(context).textTheme.titleMedium
                ?.copyWith(fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: 4),
          Text(
            '${formatDistance(task.distanceKm)} • ${task.estimatedMinutes} min',
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              Expanded(
                child: OutlinedButton(
                  onPressed: () {},
                  child: const Text('Refuser'),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: FilledButton(
                  onPressed: () => _openDetail(context, task),
                  child: const Text('Accepter'),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _StopLine extends StatelessWidget {
  const _StopLine({
    required this.icon,
    required this.title,
    required this.subtitle,
  });

  final IconData icon;
  final String title;
  final String subtitle;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(icon, color: Theme.of(context).colorScheme.primary),
        const SizedBox(width: 12),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(title, style: const TextStyle(fontWeight: FontWeight.w800)),
              const SizedBox(height: 2),
              Text(subtitle),
            ],
          ),
        ),
      ],
    );
  }
}

void _openDetail(BuildContext context, DeliveryTask task) {
  Navigator.of(context).push(
    MaterialPageRoute<void>(builder: (_) => DeliveryDetailScreen(task: task)),
  );
}
