import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui_kit.dart';

const _ink = Color(0xFF10233A);
const _pageBg = Color(0xFFF4F6F5);
const _tileBg = Color(0xFFF5F6F7);

class AccountScreen extends StatelessWidget {
  const AccountScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: _pageBg,
      body: SafeArea(
        top: false,
        bottom: false,
        child: CustomScrollView(
          slivers: [
            const SliverToBoxAdapter(child: _ProfileHero()),
            SliverPadding(
              padding: const EdgeInsets.fromLTRB(16, 20, 16, 0),
              sliver: SliverList.list(
                children: [
                  _MenuCard(
                    items: [
                      _AccountMenuItem(code: 'PR', label: 'Profil', onTap: () => context.push('/account/edit')),
                      _AccountMenuItem(
                        code: 'AD',
                        label: 'Mes adresses',
                        badge: '3',
                        onTap: () => context.push('/account/addresses'),
                      ),
                      const _AccountMenuItem(code: 'PA', label: 'Moyens de paiement'),
                    ],
                  ),
                  const SizedBox(height: 16),
                  _MenuCard(
                    items: [
                      _AccountMenuItem(code: 'CO', label: 'Commandes', onTap: () => context.go('/orders')),
                      _AccountMenuItem(code: 'FA', label: 'Favoris', onTap: () => context.go('/wishlist')),
                      const _AccountMenuItem(code: 'CP', label: 'Coupons', badge: '3 actifs'),
                    ],
                  ),
                  const SizedBox(height: 16),
                  _MenuCard(
                    items: [
                      _AccountMenuItem(code: 'NO', label: 'Notifications', onTap: () => context.push('/notifications')),
                      _AccountMenuItem(code: 'AI', label: 'Aide', onTap: () => context.push('/account/faq')),
                      _AccountMenuItem(code: 'PM', label: 'Paramètres', onTap: () => context.push('/account/notifications')),
                    ],
                  ),
                  const SizedBox(height: 16),
                  const _LogoutButton(),
                  SizedBox(height: bottomSafePadding(context, extra: 24)),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _ProfileHero extends StatelessWidget {
  const _ProfileHero();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: EdgeInsets.fromLTRB(22, MediaQuery.paddingOf(context).top + 18, 22, 26),
      decoration: const BoxDecoration(
        color: AppTheme.brandGreen,
        borderRadius: BorderRadius.vertical(bottom: Radius.circular(30)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const _StatusLine(),
          const SizedBox(height: 18),
          Row(
            children: [
              Container(
                width: 72,
                height: 72,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: const Color(0xFF0EB77C),
                  borderRadius: BorderRadius.circular(24),
                  border: Border.all(color: Colors.white.withValues(alpha: 0.65), width: 3),
                ),
                child: const Text(
                  'AK',
                  style: TextStyle(color: Colors.white, fontSize: 24, fontWeight: FontWeight.w900),
                ),
              ),
              const SizedBox(width: 18),
              const Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'Aïcha Koudjo',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(color: Colors.white, fontSize: 22, fontWeight: FontWeight.w900),
                    ),
                    SizedBox(height: 4),
                    Text(
                      '+229 97 00 00 00 · Cotonou',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(color: Color(0xFFC8E8DC), fontSize: 15, fontWeight: FontWeight.w600),
                    ),
                  ],
                ),
              ),
              const SizedBox(width: 12),
              Container(
                width: 48,
                height: 48,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: Colors.white.withValues(alpha: 0.18),
                  borderRadius: BorderRadius.circular(15),
                ),
                child: const Icon(Icons.more_horiz, color: Colors.white, size: 24),
              ),
            ],
          ),
          const SizedBox(height: 28),
          const Row(
            children: [
              Expanded(child: _StatTile(value: '24', label: 'Commandes')),
              SizedBox(width: 10),
              Expanded(child: _StatTile(value: '3', label: 'Coupons')),
              SizedBox(width: 10),
              Expanded(child: _StatTile(value: '12', label: 'Favoris')),
            ],
          ),
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
        const Text('9:41', style: TextStyle(color: Colors.white, fontSize: 16, fontWeight: FontWeight.w800)),
        const Spacer(),
        Icon(Icons.signal_cellular_alt, color: Colors.white.withValues(alpha: 0.95), size: 18),
        const SizedBox(width: 5),
        Container(
          width: 24,
          height: 13,
          decoration: BoxDecoration(
            border: Border.all(color: Colors.white, width: 1.4),
            borderRadius: BorderRadius.circular(4),
          ),
          child: Align(
            alignment: Alignment.centerLeft,
            child: Container(
              width: 16,
              margin: const EdgeInsets.all(2),
              decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(2)),
            ),
          ),
        ),
      ],
    );
  }
}

class _StatTile extends StatelessWidget {
  const _StatTile({required this.value, required this.label});

  final String value;
  final String label;

  @override
  Widget build(BuildContext context) {
    return Container(
      height: 78,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.22),
        borderRadius: BorderRadius.circular(16),
      ),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Text(value, style: const TextStyle(color: Colors.white, fontSize: 23, fontWeight: FontWeight.w900)),
          const SizedBox(height: 2),
          Text(label, style: const TextStyle(color: Color(0xFFC8E8DC), fontSize: 13, fontWeight: FontWeight.w600)),
        ],
      ),
    );
  }
}

class _MenuCard extends StatelessWidget {
  const _MenuCard({required this.items});

  final List<_AccountMenuItem> items;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 14),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(22),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.025),
            blurRadius: 20,
            offset: const Offset(0, 10),
          ),
        ],
      ),
      child: Column(
        children: [
          for (var i = 0; i < items.length; i++) ...[
            items[i],
            if (i != items.length - 1) const Divider(height: 1, indent: 64, color: Color(0xFFE8EDF0)),
          ],
        ],
      ),
    );
  }
}

class _AccountMenuItem extends StatelessWidget {
  const _AccountMenuItem({required this.code, required this.label, this.badge, this.onTap});

  final String code;
  final String label;
  final String? badge;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(14),
      child: SizedBox(
        height: 72,
        child: Row(
          children: [
            Container(
              width: 46,
              height: 46,
              alignment: Alignment.center,
              decoration: BoxDecoration(color: _tileBg, borderRadius: BorderRadius.circular(14)),
              child: Text(
                code,
                style: const TextStyle(color: AppTheme.brandGreen, fontSize: 14, fontWeight: FontWeight.w900),
              ),
            ),
            const SizedBox(width: 18),
            Expanded(
              child: Text(
                label,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(color: _ink, fontSize: 18, fontWeight: FontWeight.w800),
              ),
            ),
            if (badge != null) ...[
              const SizedBox(width: 8),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
                decoration: BoxDecoration(color: AppTheme.softGreen, borderRadius: BorderRadius.circular(12)),
                child: Text(
                  badge!,
                  style: const TextStyle(color: AppTheme.brandGreen, fontSize: 13, fontWeight: FontWeight.w900),
                ),
              ),
            ],
            const SizedBox(width: 8),
            const Icon(Icons.chevron_right, color: Color(0xFFB7C3CC), size: 24),
          ],
        ),
      ),
    );
  }
}

class _LogoutButton extends StatelessWidget {
  const _LogoutButton();

  @override
  Widget build(BuildContext context) {
    return Container(
      height: 60,
      alignment: Alignment.center,
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(18)),
      child: const Text(
        'Se déconnecter',
        style: TextStyle(color: Color(0xFFE9513E), fontSize: 17, fontWeight: FontWeight.w900),
      ),
    );
  }
}
