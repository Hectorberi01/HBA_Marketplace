import 'package:flutter/material.dart';

import '../../core/widgets/app_card.dart';
import '../../core/widgets/section_header.dart';

class DocumentsScreen extends StatelessWidget {
  const DocumentsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Documents')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
          children: const [
            SectionHeader(title: 'Documents requis'),
            SizedBox(height: 12),
            _DocumentTile(
              title: 'Pièce d’identité',
              status: 'Validée',
              icon: Icons.badge_outlined,
            ),
            SizedBox(height: 10),
            _DocumentTile(
              title: 'Permis de conduire',
              status: 'Validé',
              icon: Icons.credit_card_outlined,
            ),
            SizedBox(height: 10),
            _DocumentTile(
              title: 'Assurance véhicule',
              status: 'Expire dans 22 jours',
              icon: Icons.shield_outlined,
            ),
          ],
        ),
      ),
    );
  }
}

class _DocumentTile extends StatelessWidget {
  const _DocumentTile({
    required this.title,
    required this.status,
    required this.icon,
  });

  final String title;
  final String status;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    return AppCard(
      padding: EdgeInsets.zero,
      child: ListTile(
        leading: Icon(icon),
        title: Text(title),
        subtitle: Text(status),
        trailing: OutlinedButton(
          onPressed: () {},
          child: const Text('Mettre à jour'),
        ),
      ),
    );
  }
}
