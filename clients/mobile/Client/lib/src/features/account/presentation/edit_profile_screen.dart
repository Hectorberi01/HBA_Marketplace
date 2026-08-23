import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui_kit.dart';

const _ink = Color(0xFF10233A);
const _muted = Color(0xFF728399);
const _pageBg = Color(0xFFF4F6F5);
const _line = Color(0xFFE8EDF0);

class EditProfileScreen extends StatelessWidget {
  const EditProfileScreen({super.key});

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
                const Text('Profil', style: TextStyle(color: _ink, fontSize: 28, fontWeight: FontWeight.w900)),
              ],
            ),
            const SizedBox(height: 34),
            const _ProfileHeader(),
            const SizedBox(height: 34),
            const _InfoCard(),
            const SizedBox(height: 20),
            SizedBox(
              height: 64,
              child: FilledButton(
                onPressed: () {},
                style: FilledButton.styleFrom(
                  backgroundColor: AppTheme.brandGreen,
                  foregroundColor: Colors.white,
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(18)),
                ),
                child: const Text('Enregistrer les modifications', style: TextStyle(fontSize: 18, fontWeight: FontWeight.w900)),
              ),
            ),
            const SizedBox(height: 18),
            InkWell(
              onTap: () => context.push('/account/delete'),
              borderRadius: BorderRadius.circular(18),
              child: Container(
                height: 62,
                alignment: Alignment.center,
                decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(18)),
                child: const Text(
                  'Supprimer mon compte',
                  style: TextStyle(color: Color(0xFFE9513E), fontSize: 18, fontWeight: FontWeight.w900),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _ProfileHeader extends StatelessWidget {
  const _ProfileHeader();

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Stack(
          clipBehavior: Clip.none,
          children: [
            Container(
              width: 118,
              height: 118,
              alignment: Alignment.center,
              decoration: BoxDecoration(color: AppTheme.softGreen, borderRadius: BorderRadius.circular(34)),
              child: const Text('AK', style: TextStyle(color: AppTheme.brandGreen, fontSize: 36, fontWeight: FontWeight.w900)),
            ),
            Positioned(
              right: -4,
              bottom: -4,
              child: Container(
                width: 42,
                height: 42,
                decoration: BoxDecoration(
                  color: AppTheme.brandGreen,
                  borderRadius: BorderRadius.circular(13),
                  border: Border.all(color: Colors.white, width: 3),
                ),
                child: const Icon(Icons.image_outlined, color: Colors.white, size: 20),
              ),
            ),
          ],
        ),
        const SizedBox(height: 22),
        const Text('Aïcha Koudjo', style: TextStyle(color: _ink, fontSize: 24, fontWeight: FontWeight.w900)),
        const SizedBox(height: 12),
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 9),
          decoration: BoxDecoration(color: AppTheme.softGreen, borderRadius: BorderRadius.circular(13)),
          child: const Text(
            '✓  Compte vérifié · Membre depuis 2024',
            style: TextStyle(color: AppTheme.brandGreen, fontSize: 14, fontWeight: FontWeight.w900),
          ),
        ),
      ],
    );
  }
}

class _InfoCard extends StatelessWidget {
  const _InfoCard();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 22, vertical: 18),
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(24)),
      child: const Column(
        children: [
          _ProfileRow(label: 'Nom complet', value: 'Aïcha Koudjo'),
          Divider(height: 1, color: _line),
          _ProfileRow(label: 'Téléphone', value: '+229 97 00 00 00', badge: 'Vérifié'),
          Divider(height: 1, color: _line),
          _ProfileRow(label: 'E-mail', value: 'aicha.koudjo@gmail.com', badge: 'Vérifié'),
          Divider(height: 1, color: _line),
          _ProfileRow(label: 'Date de naissance', value: '14 mars 1994'),
          Divider(height: 1, color: _line),
          _ProfileRow(label: 'Ville', value: 'Cotonou, Bénin'),
        ],
      ),
    );
  }
}

class _ProfileRow extends StatelessWidget {
  const _ProfileRow({required this.label, required this.value, this.badge});

  final String label;
  final String value;
  final String? badge;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 82,
      child: Row(
        children: [
          Expanded(
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(label, style: const TextStyle(color: _muted, fontSize: 15, fontWeight: FontWeight.w600)),
                const SizedBox(height: 8),
                Text(value, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: _ink, fontSize: 17, fontWeight: FontWeight.w900)),
              ],
            ),
          ),
          if (badge != null) ...[
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 13, vertical: 8),
              decoration: BoxDecoration(color: AppTheme.softGreen, borderRadius: BorderRadius.circular(13)),
              child: Text(badge!, style: const TextStyle(color: AppTheme.brandGreen, fontSize: 13, fontWeight: FontWeight.w900)),
            ),
            const SizedBox(width: 10),
          ],
          const Icon(Icons.chevron_right_rounded, color: Color(0xFFB7C3CC), size: 24),
        ],
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
