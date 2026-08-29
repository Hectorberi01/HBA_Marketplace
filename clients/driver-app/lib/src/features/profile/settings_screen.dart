import 'package:flutter/material.dart';

class SettingsScreen extends StatefulWidget {
  const SettingsScreen({super.key});

  @override
  State<SettingsScreen> createState() => _SettingsScreenState();
}

class _SettingsScreenState extends State<SettingsScreen> {
  bool _pushEnabled = true;
  bool _soundEnabled = true;
  bool _backgroundLocation = true;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Paramètres')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
          children: [
            SwitchListTile(
              value: _pushEnabled,
              onChanged: (value) => setState(() => _pushEnabled = value),
              title: const Text('Notifications push'),
              subtitle: const Text('Nouvelles courses et alertes importantes'),
            ),
            SwitchListTile(
              value: _soundEnabled,
              onChanged: (value) => setState(() => _soundEnabled = value),
              title: const Text('Son des alertes'),
              subtitle: const Text('Signal sonore pour les propositions'),
            ),
            SwitchListTile(
              value: _backgroundLocation,
              onChanged: (value) => setState(() => _backgroundLocation = value),
              title: const Text('Position pendant les courses'),
              subtitle: const Text('Nécessaire pour le suivi en temps réel'),
            ),
            const Divider(),
            ListTile(
              leading: const Icon(Icons.lock_outline),
              title: const Text('Changer le mot de passe'),
              trailing: const Icon(Icons.chevron_right),
              onTap: () {},
            ),
            ListTile(
              leading: const Icon(Icons.language_outlined),
              title: const Text('Langue'),
              subtitle: const Text('Français'),
              trailing: const Icon(Icons.chevron_right),
              onTap: () {},
            ),
          ],
        ),
      ),
    );
  }
}
