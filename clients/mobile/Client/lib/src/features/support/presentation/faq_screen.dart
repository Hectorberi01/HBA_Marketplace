import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui_kit.dart';

const _ink = Color(0xFF10233A);
const _muted = Color(0xFF728399);
const _pageBg = Color(0xFFF4F6F5);

class FaqScreen extends StatefulWidget {
  const FaqScreen({super.key});

  @override
  State<FaqScreen> createState() => _FaqScreenState();
}

class _FaqScreenState extends State<FaqScreen> {
  int? _open = 0;

  static const _questions = [
    ('Comment suivre ma commande ?', 'Ouvrez l\'onglet Commandes, sélectionnez la commande en cours puis appuyez sur Suivre pour voir la position du livreur HBA Delivery en direct.'),
    ('Puis-je commander Food et Express ensemble ?', 'Oui. Les paniers sont séparés pour garder des délais fiables, mais vous pouvez passer d\'un univers à l\'autre sans perdre vos sélections.'),
    ('Quels moyens de paiement acceptez-vous ?', 'Vous pouvez payer par carte, mobile money compatible et paiement à la livraison selon le restaurant ou la boutique.'),
    ('Comment annuler une commande ?', 'Ouvrez le détail de la commande puis choisissez Annuler si elle n\'est pas encore en préparation ou expédiée.'),
  ];

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: _pageBg,
      body: SafeArea(
        top: false,
        bottom: false,
        child: CustomScrollView(
          slivers: [
            SliverToBoxAdapter(child: _HelpHero(onBack: () => context.pop())),
            SliverPadding(
              padding: EdgeInsets.fromLTRB(16, 22, 16, bottomSafePadding(context, extra: 24)),
              sliver: SliverList.list(
                children: [
                  const Text('Sujets fréquents', style: TextStyle(color: _ink, fontSize: 24, fontWeight: FontWeight.w900)),
                  const SizedBox(height: 14),
                  const _TopicGrid(),
                  const SizedBox(height: 28),
                  const Text('Questions fréquentes', style: TextStyle(color: _ink, fontSize: 24, fontWeight: FontWeight.w900)),
                  const SizedBox(height: 14),
                  for (var i = 0; i < _questions.length; i++) ...[
                    _FaqCard(
                      title: _questions[i].$1,
                      answer: _questions[i].$2,
                      open: _open == i,
                      onTap: () => setState(() => _open = _open == i ? null : i),
                    ),
                    const SizedBox(height: 12),
                  ],
                  const SizedBox(height: 12),
                  const _DeliveryHelpCard(),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _HelpHero extends StatelessWidget {
  const _HelpHero({required this.onBack});

  final VoidCallback onBack;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: EdgeInsets.fromLTRB(16, MediaQuery.paddingOf(context).top + 16, 16, 34),
      decoration: const BoxDecoration(
        color: AppTheme.brandGreen,
        borderRadius: BorderRadius.vertical(bottom: Radius.circular(30)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const _StatusLine(color: _ink),
          const SizedBox(height: 18),
          Row(
            children: [
              _HeroButton(icon: Icons.chevron_left_rounded, onTap: onBack),
              const SizedBox(width: 14),
              const Text('Aide', style: TextStyle(color: Colors.white, fontSize: 28, fontWeight: FontWeight.w900)),
            ],
          ),
          const SizedBox(height: 24),
          Text(
            'Une question sur une commande, un paiement ou une livraison ? Nous répondons en moins de 5 minutes.',
            style: TextStyle(color: Colors.white.withValues(alpha: 0.78), fontSize: 18, fontWeight: FontWeight.w700, height: 1.35),
          ),
          const SizedBox(height: 26),
          Row(
            children: [
              Expanded(
                child: _HeroCta(
                  icon: Icons.chat_bubble_outline_rounded,
                  label: 'Chat HBA',
                  color: AppTheme.brandGreen,
                  bg: Colors.white,
                  onTap: () => context.push('/conversations'),
                ),
              ),
              const SizedBox(width: 12),
              const Expanded(
                child: _HeroCta(
                  icon: Icons.phone_in_talk_outlined,
                  label: 'Appeler',
                  color: Colors.white,
                  bg: Color(0xFF529B7A),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _TopicGrid extends StatelessWidget {
  const _TopicGrid();

  @override
  Widget build(BuildContext context) {
    final topics = [
      ('CO', 'Commandes et livraison', '8 articles', AppTheme.softGreen, AppTheme.brandGreen),
      ('PA', 'Paiements et remboursements', '6 articles', const Color(0xFFEFF2F4), _ink),
      ('RE', 'Retours HBAExpress', '5 articles', AppTheme.softGreen, AppTheme.brandGreen),
      ('FO', 'Problème avec un repas', '4 articles', const Color(0xFFFFF1E2), const Color(0xFFE56E13)),
    ];
    return GridView.builder(
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      itemCount: topics.length,
      gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
        crossAxisCount: 2,
        crossAxisSpacing: 12,
        mainAxisSpacing: 12,
        childAspectRatio: 1.36,
      ),
      itemBuilder: (_, i) => Container(
        padding: const EdgeInsets.all(18),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(20)),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Container(
              width: 44,
              height: 44,
              alignment: Alignment.center,
              decoration: BoxDecoration(color: topics[i].$4, borderRadius: BorderRadius.circular(13)),
              child: Text(topics[i].$1, style: TextStyle(color: topics[i].$5, fontSize: 13, fontWeight: FontWeight.w900)),
            ),
            const Spacer(),
            Text(topics[i].$2, maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(color: _ink, fontSize: 16, fontWeight: FontWeight.w900, height: 1.15)),
            const SizedBox(height: 8),
            Text(topics[i].$3, style: const TextStyle(color: _muted, fontSize: 14, fontWeight: FontWeight.w600)),
          ],
        ),
      ),
    );
  }
}

class _FaqCard extends StatelessWidget {
  const _FaqCard({required this.title, required this.answer, required this.open, required this.onTap});

  final String title;
  final String answer;
  final bool open;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(20),
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 180),
        padding: const EdgeInsets.fromLTRB(20, 18, 18, 18),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(20)),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(title, style: const TextStyle(color: _ink, fontSize: 17, fontWeight: FontWeight.w900, height: 1.25)),
                ),
                Container(
                  width: 34,
                  height: 34,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(color: const Color(0xFFF3F5F6), borderRadius: BorderRadius.circular(12)),
                  child: Icon(open ? Icons.remove_rounded : Icons.add_rounded, color: AppTheme.brandGreen, size: 22),
                ),
              ],
            ),
            if (open) ...[
              const SizedBox(height: 18),
              Text(answer, style: const TextStyle(color: _muted, fontSize: 16, fontWeight: FontWeight.w600, height: 1.45)),
            ],
          ],
        ),
      ),
    );
  }
}

class _DeliveryHelpCard extends StatelessWidget {
  const _DeliveryHelpCard();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(color: AppTheme.softGreen, borderRadius: BorderRadius.circular(20)),
      child: Row(
        children: [
          Container(
            width: 52,
            height: 52,
            decoration: BoxDecoration(color: AppTheme.brandGreen, borderRadius: BorderRadius.circular(15)),
            child: const Icon(Icons.local_shipping_outlined, color: Colors.white, size: 25),
          ),
          const SizedBox(width: 16),
          const Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Problème avec une livraison ?', maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: AppTheme.brandGreen, fontSize: 17, fontWeight: FontWeight.w900)),
                SizedBox(height: 5),
                Text('Suivez votre commande HBA Delivery en direct.', maxLines: 2, overflow: TextOverflow.ellipsis, style: TextStyle(color: Color(0xFF4E8A71), fontSize: 15, fontWeight: FontWeight.w600)),
              ],
            ),
          ),
          const SizedBox(width: 12),
          SizedBox(
            width: 70,
            height: 48,
            child: FilledButton(
              onPressed: () => context.push('/order/mock/tracking'),
              style: FilledButton.styleFrom(
                backgroundColor: AppTheme.brandGreen,
                foregroundColor: Colors.white,
                padding: EdgeInsets.zero,
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
              ),
              child: const Text('Voir', style: TextStyle(fontWeight: FontWeight.w900)),
            ),
          ),
        ],
      ),
    );
  }
}

class _HeroCta extends StatelessWidget {
  const _HeroCta({required this.icon, required this.label, required this.color, required this.bg, this.onTap});

  final IconData icon;
  final String label;
  final Color color;
  final Color bg;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(18),
      child: Container(
        height: 58,
        alignment: Alignment.center,
        decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(18)),
        child: Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(icon, color: color, size: 21),
            const SizedBox(width: 10),
            Flexible(
              child: Text(label, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: color, fontSize: 17, fontWeight: FontWeight.w900)),
            ),
          ],
        ),
      ),
    );
  }
}

class _HeroButton extends StatelessWidget {
  const _HeroButton({required this.icon, required this.onTap});

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
        decoration: BoxDecoration(color: Colors.white.withValues(alpha: 0.2), borderRadius: BorderRadius.circular(15)),
        child: Icon(icon, color: Colors.white, size: 27),
      ),
    );
  }
}

class _StatusLine extends StatelessWidget {
  const _StatusLine({required this.color});

  final Color color;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Text('9:41', style: TextStyle(color: color, fontSize: 16, fontWeight: FontWeight.w900)),
        const Spacer(),
        Icon(Icons.signal_cellular_alt, color: color, size: 18),
        const SizedBox(width: 5),
        Container(
          width: 25,
          height: 13,
          decoration: BoxDecoration(border: Border.all(color: color, width: 1.4), borderRadius: BorderRadius.circular(4)),
          child: Align(
            alignment: Alignment.centerLeft,
            child: Container(
              width: 16,
              margin: const EdgeInsets.all(2),
              decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(2)),
            ),
          ),
        ),
      ],
    );
  }
}
