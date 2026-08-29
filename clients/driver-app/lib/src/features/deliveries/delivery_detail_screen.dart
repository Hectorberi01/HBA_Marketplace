import 'package:flutter/material.dart';

import '../../core/formatters.dart';
import '../../core/models/delivery_task.dart';
import '../../core/widgets/app_card.dart';
import '../../core/widgets/section_header.dart';
import '../../core/widgets/status_badge.dart';
import 'proof_delivery_screen.dart';

class DeliveryDetailScreen extends StatelessWidget {
  const DeliveryDetailScreen({required this.task, super.key});

  final DeliveryTask task;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Détail de course')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
          children: [
            AppCard(
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
                        emphasized: true,
                      ),
                      const Spacer(),
                      Text(
                        task.reference,
                        style: Theme.of(context).textTheme.labelLarge
                            ?.copyWith(fontWeight: FontWeight.w900),
                      ),
                    ],
                  ),
                  const SizedBox(height: 16),
                  Text(
                    formatXof(task.payoutXof),
                    style: Theme.of(context).textTheme.headlineSmall
                        ?.copyWith(fontWeight: FontWeight.w900),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    '${formatDistance(task.distanceKm)} • ${task.estimatedMinutes} minutes estimées',
                  ),
                ],
              ),
            ),
            const SizedBox(height: 18),
            const SectionHeader(title: 'Itinéraire'),
            const SizedBox(height: 12),
            AppCard(
              child: Column(
                children: [
                  _AddressBlock(
                    icon: Icons.storefront,
                    title: 'Retrait',
                    name: task.pickupName,
                    address: task.pickupAddress,
                    phone: task.pickupPhone,
                  ),
                  const Divider(height: 28),
                  _AddressBlock(
                    icon: Icons.location_on,
                    title: 'Livraison',
                    name: task.dropoffName,
                    address: task.dropoffAddress,
                    phone: task.customerPhone,
                  ),
                ],
              ),
            ),
            const SizedBox(height: 18),
            const SectionHeader(title: 'Instructions'),
            const SizedBox(height: 12),
            AppCard(child: Text(task.instructions)),
            const SizedBox(height: 18),
            const SectionHeader(title: 'Actions'),
            const SizedBox(height: 12),
            FilledButton.icon(
              onPressed: () {},
              icon: const Icon(Icons.navigation),
              label: const Text('Ouvrir la navigation'),
            ),
            const SizedBox(height: 12),
            OutlinedButton.icon(
              onPressed: () {},
              icon: const Icon(Icons.inventory_2_outlined),
              label: const Text('Confirmer la récupération'),
            ),
            const SizedBox(height: 12),
            FilledButton.icon(
              onPressed: () {
                Navigator.of(context).push(
                  MaterialPageRoute<void>(
                    builder: (_) => ProofDeliveryScreen(task: task),
                  ),
                );
              },
              icon: const Icon(Icons.verified_outlined),
              label: const Text('Finaliser la livraison'),
            ),
          ],
        ),
      ),
    );
  }
}

class _AddressBlock extends StatelessWidget {
  const _AddressBlock({
    required this.icon,
    required this.title,
    required this.name,
    required this.address,
    required this.phone,
  });

  final IconData icon;
  final String title;
  final String name;
  final String address;
  final String phone;

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
              Text(title, style: Theme.of(context).textTheme.labelLarge),
              const SizedBox(height: 4),
              Text(
                name,
                style: Theme.of(context).textTheme.titleMedium
                    ?.copyWith(fontWeight: FontWeight.w800),
              ),
              const SizedBox(height: 2),
              Text(address),
              const SizedBox(height: 8),
              OutlinedButton.icon(
                onPressed: () {},
                icon: const Icon(Icons.call_outlined),
                label: Text(phone),
              ),
            ],
          ),
        ),
      ],
    );
  }
}
