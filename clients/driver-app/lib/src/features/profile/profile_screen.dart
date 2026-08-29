import 'package:flutter/material.dart';

import '../../core/models/driver_profile.dart';
import '../../core/widgets/app_card.dart';
import '../../core/widgets/status_badge.dart';
import 'documents_screen.dart';
import 'settings_screen.dart';
import 'vehicle_screen.dart';

class ProfileScreen extends StatelessWidget {
  const ProfileScreen({required this.profile, super.key});

  final DriverProfile profile;

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
              Row(
                children: [
                  const CircleAvatar(
                    radius: 28,
                    child: Icon(Icons.person, size: 30),
                  ),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          profile.fullName,
                          style: Theme.of(context).textTheme.titleLarge
                              ?.copyWith(fontWeight: FontWeight.w900),
                        ),
                        const SizedBox(height: 2),
                        Text('Livreur à ${profile.city}'),
                      ],
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 16),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  StatusBadge(
                    label: profile.verified ? 'Profil vérifié' : 'À vérifier',
                    icon: Icons.verified_outlined,
                    emphasized: profile.verified,
                  ),
                  StatusBadge(
                    label: '${profile.rating} / 5',
                    icon: Icons.star_outline,
                  ),
                  StatusBadge(
                    label: '${profile.completedDeliveries} courses',
                    icon: Icons.local_shipping_outlined,
                  ),
                ],
              ),
            ],
          ),
        ),
        const SizedBox(height: 18),
        _ProfileAction(
          icon: Icons.badge_outlined,
          title: 'Documents',
          subtitle: 'Permis, pièce d’identité, assurance',
          onTap: () => _push(context, const DocumentsScreen()),
        ),
        _ProfileAction(
          icon: Icons.two_wheeler,
          title: 'Véhicule',
          subtitle: '${profile.vehicleLabel} • ${profile.plateNumber}',
          onTap: () => _push(context, VehicleScreen(profile: profile)),
        ),
        _ProfileAction(
          icon: Icons.settings_outlined,
          title: 'Paramètres',
          subtitle: 'Langue, sécurité, notifications',
          onTap: () => _push(context, const SettingsScreen()),
        ),
        _ProfileAction(
          icon: Icons.logout,
          title: 'Déconnexion',
          subtitle: 'Fermer la session sur cet appareil',
          onTap: () {},
        ),
      ],
    );
  }

  void _push(BuildContext context, Widget page) {
    Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => page));
  }
}

class _ProfileAction extends StatelessWidget {
  const _ProfileAction({
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.onTap,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: AppCard(
        padding: EdgeInsets.zero,
        child: ListTile(
          leading: Icon(icon),
          title: Text(title),
          subtitle: Text(subtitle),
          trailing: const Icon(Icons.chevron_right),
          onTap: onTap,
        ),
      ),
    );
  }
}
