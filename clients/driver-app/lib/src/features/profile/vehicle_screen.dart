import 'package:flutter/material.dart';

import '../../core/models/driver_profile.dart';
import '../../core/widgets/app_card.dart';

class VehicleScreen extends StatelessWidget {
  const VehicleScreen({required this.profile, super.key});

  final DriverProfile profile;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Véhicule')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
          children: [
            AppCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Icon(
                    Icons.two_wheeler,
                    size: 44,
                    color: Theme.of(context).colorScheme.primary,
                  ),
                  const SizedBox(height: 14),
                  Text(
                    profile.vehicleLabel,
                    style: Theme.of(context).textTheme.titleLarge
                        ?.copyWith(fontWeight: FontWeight.w900),
                  ),
                  const SizedBox(height: 6),
                  Text('Immatriculation ${profile.plateNumber}'),
                ],
              ),
            ),
            const SizedBox(height: 16),
            FilledButton.icon(
              onPressed: () {},
              icon: const Icon(Icons.edit_outlined),
              label: const Text('Modifier le véhicule'),
            ),
          ],
        ),
      ),
    );
  }
}
