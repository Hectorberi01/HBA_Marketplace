import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui_kit.dart';

const _ink = Color(0xFF10233A);
const _muted = Color(0xFF728399);
const _pageBg = Color(0xFFF4F6F5);
const _line = Color(0xFFE8EDF0);

class NotificationPreferencesScreen extends StatelessWidget {
  const NotificationPreferencesScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: _pageBg,
      body: SafeArea(
        top: false,
        bottom: false,
        child: ListView(
          padding: EdgeInsets.fromLTRB(16, MediaQuery.paddingOf(context).top + 16, 16, bottomSafePadding(context, extra: 24)),
          children: [
            const _StatusLine(),
            const SizedBox(height: 18),
            Row(
              children: [
                _SquareButton(icon: Icons.chevron_left_rounded, onTap: () => context.pop()),
                const SizedBox(width: 14),
                const Text('Paramètres', style: TextStyle(color: _ink, fontSize: 28, fontWeight: FontWeight.w900)),
              ],
            ),
            const SizedBox(height: 36),
            const _SectionTitle('NOTIFICATIONS'),
            const _SettingsCard(
              rows: [
                _SettingsRow(title: 'Notifications push', subtitle: 'Statut de commande et livraison', enabled: true),
                _SettingsRow(title: 'Offres et promotions', enabled: true),
                _SettingsRow(title: 'Actualités HBA Food', enabled: true),
                _SettingsRow(title: 'Actualités HBAExpress', enabled: false),
              ],
            ),
            const SizedBox(height: 22),
            const _SectionTitle('PRÉFÉRENCES'),
            const _SettingsCard(
              rows: [
                _SettingsRow(title: 'Langue', value: 'Français'),
                _SettingsRow(title: 'Devise', value: 'F CFA (XOF)'),
                _SettingsRow(title: 'Localisation', subtitle: 'Pour proposer les restaurants proches', enabled: true),
              ],
            ),
            const SizedBox(height: 22),
            const _SectionTitle('SÉCURITÉ'),
            const _SettingsCard(
              rows: [
                _SettingsRow(title: 'Déverrouillage biométrique', subtitle: 'Empreinte ou Face ID au paiement', enabled: true),
                _SettingsRow(title: 'Thème sombre', enabled: false),
              ],
            ),
            const SizedBox(height: 22),
            Container(
              height: 56,
              alignment: Alignment.center,
              decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(18)),
              child: const Text('HBA · Version 2.4.1 · Cotonou', style: TextStyle(color: Color(0xFF9BA8B5), fontSize: 15, fontWeight: FontWeight.w700)),
            ),
          ],
        ),
      ),
    );
  }
}

class _SectionTitle extends StatelessWidget {
  const _SectionTitle(this.text);
  final String text;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(4, 0, 0, 10),
      child: Text(text, style: const TextStyle(color: Color(0xFFA5B2BE), fontSize: 14, fontWeight: FontWeight.w900, letterSpacing: 2.5)),
    );
  }
}

class _SettingsCard extends StatelessWidget {
  const _SettingsCard({required this.rows});
  final List<_SettingsRow> rows;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(24)),
      child: Column(
        children: [
          for (var i = 0; i < rows.length; i++) ...[
            rows[i],
            if (i != rows.length - 1) const Divider(height: 1, color: _line),
          ],
        ],
      ),
    );
  }
}

class _SettingsRow extends StatelessWidget {
  const _SettingsRow({required this.title, this.subtitle, this.value, this.enabled});

  final String title;
  final String? subtitle;
  final String? value;
  final bool? enabled;

  @override
  Widget build(BuildContext context) {
    return ConstrainedBox(
      constraints: BoxConstraints(minHeight: subtitle == null ? 76 : 88),
      child: Row(
        children: [
          Expanded(
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: _ink, fontSize: 18, fontWeight: FontWeight.w800)),
                if (subtitle != null) ...[
                  const SizedBox(height: 6),
                  Text(subtitle!, maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(color: _muted, fontSize: 15, fontWeight: FontWeight.w600)),
                ],
              ],
            ),
          ),
          if (value != null) ...[
            const SizedBox(width: 10),
            Text(value!, style: const TextStyle(color: AppTheme.brandGreen, fontSize: 16, fontWeight: FontWeight.w900)),
            const SizedBox(width: 8),
            const Icon(Icons.chevron_right_rounded, color: Color(0xFFB7C3CC), size: 24),
          ] else if (enabled != null)
            _MockSwitch(on: enabled!),
        ],
      ),
    );
  }
}

class _MockSwitch extends StatelessWidget {
  const _MockSwitch({required this.on});
  final bool on;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 62,
      height: 38,
      padding: const EdgeInsets.all(4),
      decoration: BoxDecoration(color: on ? AppTheme.brandGreen : const Color(0xFFDCE4EA), borderRadius: BorderRadius.circular(22)),
      child: Align(
        alignment: on ? Alignment.centerRight : Alignment.centerLeft,
        child: Container(
          width: 30,
          height: 30,
          decoration: BoxDecoration(
            color: Colors.white,
            borderRadius: BorderRadius.circular(18),
            boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.08), blurRadius: 4, offset: const Offset(0, 2))],
          ),
        ),
      ),
    );
  }
}

class _StatusLine extends StatelessWidget {
  const _StatusLine();

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        const Text('9:41', style: TextStyle(color: _ink, fontSize: 16, fontWeight: FontWeight.w900)),
        const Spacer(),
        const Icon(Icons.signal_cellular_alt, color: _ink, size: 18),
        const SizedBox(width: 5),
        Container(
          width: 25,
          height: 13,
          decoration: BoxDecoration(border: Border.all(color: _ink, width: 1.4), borderRadius: BorderRadius.circular(4)),
          child: Align(
            alignment: Alignment.centerLeft,
            child: Container(
              width: 16,
              margin: const EdgeInsets.all(2),
              decoration: BoxDecoration(color: _ink, borderRadius: BorderRadius.circular(2)),
            ),
          ),
        ),
      ],
    );
  }
}

class _SquareButton extends StatelessWidget {
  const _SquareButton({required this.icon, required this.onTap});
  final IconData icon;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(15),
      child: Container(
        width: 52,
        height: 52,
        decoration: BoxDecoration(color: const Color(0xFFF2F4F5), borderRadius: BorderRadius.circular(15)),
        child: Icon(icon, color: _ink, size: 27),
      ),
    );
  }
}
