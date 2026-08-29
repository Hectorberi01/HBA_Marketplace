import 'package:flutter/material.dart';

import '../../core/widgets/app_card.dart';

class NotificationsScreen extends StatelessWidget {
  const NotificationsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final notifications = [
      ('Nouvelle proposition', 'Course FOOD-92752 disponible à Cadjehoun.'),
      ('Paiement ajouté', '1 800 XOF ont été ajoutés à votre solde.'),
      ('Document à renouveler', 'Votre assurance expire dans 22 jours.'),
    ];

    return Scaffold(
      appBar: AppBar(title: const Text('Notifications')),
      body: SafeArea(
        child: ListView.separated(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
          itemBuilder: (context, index) {
            final item = notifications[index];
            return AppCard(
              padding: EdgeInsets.zero,
              child: ListTile(
                leading: const Icon(Icons.notifications_none),
                title: Text(item.$1),
                subtitle: Text(item.$2),
              ),
            );
          },
          separatorBuilder: (_, _) => const SizedBox(height: 10),
          itemCount: notifications.length,
        ),
      ),
    );
  }
}
