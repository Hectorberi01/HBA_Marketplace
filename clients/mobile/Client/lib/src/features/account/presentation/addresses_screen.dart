import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui_kit.dart';

const _ink = Color(0xFF10233A);
const _muted = Color(0xFF728399);
const _pageBg = Color(0xFFF4F6F5);
const _line = Color(0xFFE5EAED);

class AddressesScreen extends StatefulWidget {
  const AddressesScreen({super.key});

  @override
  State<AddressesScreen> createState() => _AddressesScreenState();
}

class _AddressesScreenState extends State<AddressesScreen> {
  String? _expandedAddressId;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: _pageBg,
      body: SafeArea(
        top: false,
        bottom: false,
        child: Stack(
          children: [
            CustomScrollView(
              slivers: [
                const SliverToBoxAdapter(child: _AddressesHeader()),
                SliverPadding(
                  padding: EdgeInsets.fromLTRB(16, 22, 16, bottomSafePadding(context, extra: 118)),
                  sliver: SliverList.list(
                    children: [
                      _AddressCard(
                        icon: Icons.storefront_outlined,
                        title: 'Ma boutique',
                        city: 'Fidjrossè, Cotonou',
                        details: 'En face de la pharmacie Le Bon Samaritain, à côté de la boulangerie',
                        isDefault: true,
                        highlighted: true,
                        expanded: _expandedAddressId == 'shop',
                        onTap: () => _toggleAddress('shop'),
                      ),
                      const SizedBox(height: 16),
                      _AddressCard(
                        icon: Icons.home_outlined,
                        title: 'Mon domicile',
                        city: 'Akpakpa, Cotonou',
                        details: 'Après le carrefour rouge, deuxième maison à gauche, portail bleu',
                        expanded: _expandedAddressId == 'home',
                        onTap: () => _toggleAddress('home'),
                      ),
                      const SizedBox(height: 16),
                      _AddressCard(
                        icon: Icons.location_on_outlined,
                        title: 'Bureau Parakou',
                        city: 'Zongo, Parakou',
                        details: 'Immeuble Sonef, 2e étage, bureau 204',
                        expanded: _expandedAddressId == 'office',
                        onTap: () => _toggleAddress('office'),
                      ),
                    ],
                  ),
                ),
              ],
            ),
            Positioned(
              left: 0,
              right: 0,
              bottom: 0,
              child: _AddAddressBar(bottomPadding: bottomSafePadding(context, extra: 16)),
            ),
          ],
        ),
      ),
    );
  }

  void _toggleAddress(String id) {
    setState(() {
      _expandedAddressId = _expandedAddressId == id ? null : id;
    });
  }
}

class _AddressesHeader extends StatelessWidget {
  const _AddressesHeader();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: EdgeInsets.fromLTRB(16, MediaQuery.paddingOf(context).top + 16, 16, 18),
      color: Colors.white,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const _StatusLine(),
          const SizedBox(height: 18),
          Row(
            children: [
              _SquareButton(icon: Icons.chevron_left, onTap: () => context.pop()),
              const SizedBox(width: 14),
              const Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('Mes adresses', style: TextStyle(color: _ink, fontSize: 26, fontWeight: FontWeight.w900)),
                    SizedBox(height: 2),
                    Text(
                      '3 adresses enregistrées · utilisées par HBA Delivery',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(color: _muted, fontSize: 15, fontWeight: FontWeight.w600),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 24),
          const _SegmentedTabs(),
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
        const Text('9:41', style: TextStyle(color: _ink, fontSize: 16, fontWeight: FontWeight.w800)),
        const Spacer(),
        const Icon(Icons.signal_cellular_alt, color: _ink, size: 18),
        const SizedBox(width: 5),
        Container(
          width: 24,
          height: 13,
          decoration: BoxDecoration(
            border: Border.all(color: _ink, width: 1.4),
            borderRadius: BorderRadius.circular(4),
          ),
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
  const _SquareButton({required this.icon, this.onTap});

  final IconData icon;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(14),
      child: Container(
        width: 48,
        height: 48,
        alignment: Alignment.center,
        decoration: BoxDecoration(color: _pageBg, borderRadius: BorderRadius.circular(14)),
        child: Icon(icon, color: _ink, size: 26),
      ),
    );
  }
}

class _SegmentedTabs extends StatelessWidget {
  const _SegmentedTabs();

  @override
  Widget build(BuildContext context) {
    return Container(
      height: 60,
      padding: const EdgeInsets.all(6),
      decoration: BoxDecoration(color: _pageBg, borderRadius: BorderRadius.circular(18)),
      child: Row(
        children: [
          Expanded(
            child: Container(
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: AppTheme.brandGreen,
                borderRadius: BorderRadius.circular(15),
                boxShadow: [
                  BoxShadow(color: AppTheme.brandGreen.withValues(alpha: 0.16), blurRadius: 14, offset: const Offset(0, 8)),
                ],
              ),
              child: const Text(
                'Mes lieux',
                style: TextStyle(color: Colors.white, fontSize: 16, fontWeight: FontWeight.w900),
              ),
            ),
          ),
          Expanded(
            child: Center(
              child: Text(
                'Mes contacts',
                style: TextStyle(color: _muted.withValues(alpha: 0.95), fontSize: 16, fontWeight: FontWeight.w900),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _AddressCard extends StatelessWidget {
  const _AddressCard({
    required this.icon,
    required this.title,
    required this.city,
    required this.details,
    this.isDefault = false,
    this.highlighted = false,
    this.expanded = false,
    this.onTap,
  });

  final IconData icon;
  final String title;
  final String city;
  final String details;
  final bool isDefault;
  final bool highlighted;
  final bool expanded;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final isActive = highlighted || expanded;

    return AnimatedContainer(
      duration: const Duration(milliseconds: 180),
      curve: Curves.easeOutCubic,
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(22),
        border: Border.all(
          color: isActive ? const Color(0xFFB9E3D4) : Colors.transparent,
          width: isActive ? 1.4 : 0,
        ),
        boxShadow: [
          BoxShadow(color: Colors.black.withValues(alpha: 0.025), blurRadius: 20, offset: const Offset(0, 10)),
        ],
      ),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(22),
        child: Padding(
          padding: const EdgeInsets.all(20),
          child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                width: 52,
                height: 52,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: isActive ? AppTheme.softGreen : const Color(0xFFF1F3F4),
                  borderRadius: BorderRadius.circular(15),
                ),
                child: Icon(icon, color: isActive ? AppTheme.brandGreen : _ink, size: 25),
              ),
              const SizedBox(width: 16),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            title,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: const TextStyle(color: _ink, fontSize: 20, fontWeight: FontWeight.w900),
                          ),
                        ),
                        if (isDefault) ...[
                          const SizedBox(width: 8),
                          _Chip(label: 'Par défaut', color: AppTheme.brandGreen, bg: AppTheme.softGreen),
                        ],
                      ],
                    ),
                    const SizedBox(height: 24),
                    Text(city, style: const TextStyle(color: _ink, fontSize: 18, fontWeight: FontWeight.w800)),
                    const SizedBox(height: 8),
                    Text(details, style: const TextStyle(color: _muted, fontSize: 16, height: 1.35, fontWeight: FontWeight.w600)),
                  ],
                ),
              ),
              const SizedBox(width: 6),
              const Icon(Icons.more_vert, color: Color(0xFF9AA9B5), size: 23),
            ],
          ),
          if (expanded) ...[
            const SizedBox(height: 22),
            const Divider(height: 1, color: _line),
            const SizedBox(height: 18),
            Row(
              children: [
                Expanded(child: _ActionPill(label: 'Par défaut', color: AppTheme.brandGreen, bg: AppTheme.softGreen)),
                const SizedBox(width: 10),
                const Expanded(child: _ActionPill(label: 'Modifier', color: _ink, bg: Color(0xFFF4F5F6))),
                const SizedBox(width: 10),
                const Expanded(child: _ActionPill(label: 'Supprimer', color: Color(0xFFE9513E), bg: Color(0xFFFFE8DF))),
              ],
            ),
          ],
        ],
          ),
        ),
      ),
    );
  }
}

class _Chip extends StatelessWidget {
  const _Chip({required this.label, required this.color, required this.bg});

  final String label;
  final Color color;
  final Color bg;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 9),
      decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(13)),
      child: Text(label, style: TextStyle(color: color, fontSize: 13, fontWeight: FontWeight.w900)),
    );
  }
}

class _ActionPill extends StatelessWidget {
  const _ActionPill({required this.label, required this.color, required this.bg});

  final String label;
  final Color color;
  final Color bg;

  @override
  Widget build(BuildContext context) {
    return Container(
      height: 46,
      alignment: Alignment.center,
      decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(15)),
      child: FittedBox(
        fit: BoxFit.scaleDown,
        child: Text(label, style: TextStyle(color: color, fontSize: 15, fontWeight: FontWeight.w900)),
      ),
    );
  }
}

class _AddAddressBar extends StatelessWidget {
  const _AddAddressBar({required this.bottomPadding});

  final double bottomPadding;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: EdgeInsets.fromLTRB(16, 18, 16, bottomPadding),
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: [Colors.white.withValues(alpha: 0), Colors.white, Colors.white],
        ),
      ),
      child: Container(
        height: 64,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: AppTheme.brandGreen,
          borderRadius: BorderRadius.circular(18),
          boxShadow: [
            BoxShadow(color: AppTheme.brandGreen.withValues(alpha: 0.18), blurRadius: 24, offset: const Offset(0, 14)),
          ],
        ),
        child: const Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(Icons.add, color: Colors.white, size: 24),
            SizedBox(width: 14),
            Text('Ajouter une adresse', style: TextStyle(color: Colors.white, fontSize: 18, fontWeight: FontWeight.w900)),
          ],
        ),
      ),
    );
  }
}
