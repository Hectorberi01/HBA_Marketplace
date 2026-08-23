import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';

const _green = AppTheme.brandGreen;
const _navy = Color(0xFF0E2239);

class ChatScreen extends StatelessWidget {
  const ChatScreen({super.key, required this.conversationId});
  final String conversationId;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF4F6F5),
      body: SafeArea(
        child: Column(
          children: [
            _ChatHeader(onBack: () => context.canPop() ? context.pop() : context.go('/shop/hba-tech-store')),
            Expanded(
              child: ListView(
                padding: const EdgeInsets.fromLTRB(16, 22, 16, 18),
                children: const [
                  _DaySeparator(),
                  _SafeNotice(),
                  SizedBox(height: 20),
                  _MessageBubble(text: 'Bonjour et bienvenue chez HBA Tech\nStore 👋 Comment pouvons-nous vous\naider ?', time: '14:02'),
                  SizedBox(height: 22),
                  _ProductMessage(),
                  SizedBox(height: 14),
                  _MessageBubble(text: 'Bonjour, ce casque est-il encore\ndisponible en noir ?', mine: true, time: '14:04'),
                  SizedBox(height: 22),
                  _MessageBubble(text: 'Oui, il nous reste 12 pièces en noir.\nLivraison sous 24 h à Cotonou avec\nHBA Delivery.', time: '14:05'),
                ],
              ),
            ),
            const _Composer(),
          ],
        ),
      ),
    );
  }
}

class _ChatHeader extends StatelessWidget {
  const _ChatHeader({required this.onBack});
  final VoidCallback onBack;

  @override
  Widget build(BuildContext context) {
    return Container(
      height: 104,
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 14),
      color: Colors.white,
      child: Row(
        children: [
          _HeaderButton(icon: Icons.chevron_left_rounded, onTap: onBack),
          const SizedBox(width: 14),
          Container(width: 54, height: 54, decoration: BoxDecoration(color: const Color(0xFFE5EAF0), borderRadius: BorderRadius.circular(16)), alignment: Alignment.center, child: const Text('HT', style: TextStyle(color: _navy, fontSize: 16, fontWeight: FontWeight.w900))),
          const SizedBox(width: 14),
          const Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Text('HBA Tech Store', maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: _navy, fontSize: 17, fontWeight: FontWeight.w900)),
                SizedBox(height: 5),
                Row(
                  children: [
                    Icon(Icons.circle, color: _green, size: 8),
                    SizedBox(width: 7),
                    Expanded(child: Text('En ligne · répond en ~5 min', maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: _green, fontSize: 13, fontWeight: FontWeight.w700))),
                  ],
                ),
              ],
            ),
          ),
          const SizedBox(width: 10),
          const _HeaderButton(icon: Icons.phone_in_talk_outlined, green: true),
        ],
      ),
    );
  }
}

class _HeaderButton extends StatelessWidget {
  const _HeaderButton({required this.icon, this.onTap, this.green = false});
  final IconData icon;
  final VoidCallback? onTap;
  final bool green;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: Container(width: 50, height: 50, decoration: BoxDecoration(color: green ? const Color(0xFFE1F4EC) : const Color(0xFFF4F6F5), borderRadius: BorderRadius.circular(14)), child: Icon(icon, color: green ? _green : _navy)),
    );
  }
}

class _DaySeparator extends StatelessWidget {
  const _DaySeparator();

  @override
  Widget build(BuildContext context) {
    return const Center(child: Text("AUJOURD'HUI", style: TextStyle(color: Color(0xFF9AA8B6), fontSize: 12, letterSpacing: 1.4, fontWeight: FontWeight.w900)));
  }
}

class _SafeNotice extends StatelessWidget {
  const _SafeNotice();

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.only(top: 20),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(color: const Color(0xFFE1F4EC), borderRadius: BorderRadius.circular(18)),
      child: Row(
        children: [
          Container(width: 50, height: 50, decoration: BoxDecoration(color: _green, borderRadius: BorderRadius.circular(14)), child: const Icon(Icons.inventory_2_outlined, color: Colors.white)),
          const SizedBox(width: 14),
          const Expanded(child: Text("Discussion protégée par HBA. Ne payez jamais en dehors\nde l'application.", style: TextStyle(color: Color(0xFF3B806D), fontSize: 14, height: 1.35, fontWeight: FontWeight.w600))),
        ],
      ),
    );
  }
}

class _MessageBubble extends StatelessWidget {
  const _MessageBubble({required this.text, required this.time, this.mine = false});
  final String text;
  final String time;
  final bool mine;

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: mine ? Alignment.centerRight : Alignment.centerLeft,
      child: Column(
        crossAxisAlignment: mine ? CrossAxisAlignment.end : CrossAxisAlignment.start,
        children: [
          Container(
            constraints: BoxConstraints(maxWidth: MediaQuery.of(context).size.width * 0.72),
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: mine ? _green : Colors.white,
              borderRadius: BorderRadius.only(
                topLeft: const Radius.circular(18),
                topRight: const Radius.circular(18),
                bottomLeft: Radius.circular(mine ? 18 : 4),
                bottomRight: Radius.circular(mine ? 4 : 18),
              ),
            ),
            child: Text(text, style: TextStyle(color: mine ? Colors.white : _navy, fontSize: 15, height: 1.38, fontWeight: FontWeight.w600)),
          ),
          const SizedBox(height: 6),
          Text(time, style: const TextStyle(color: Color(0xFF9AA8B6), fontSize: 12, fontWeight: FontWeight.w600)),
        ],
      ),
    );
  }
}

class _ProductMessage extends StatelessWidget {
  const _ProductMessage();

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: Alignment.centerRight,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.end,
        children: [
          Container(
            width: 270,
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(14)),
            child: Row(
              children: [
                Container(width: 70, height: 70, decoration: BoxDecoration(color: const Color(0xFFE5EAF0), borderRadius: BorderRadius.circular(14))),
                const SizedBox(width: 14),
                const Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text('Sony WH-1000XM5', maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: _navy, fontSize: 14, fontWeight: FontWeight.w900)),
                      SizedBox(height: 6),
                      Text('279 000 F CFA', style: TextStyle(color: _green, fontSize: 14, fontWeight: FontWeight.w900)),
                    ],
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 6),
          const Text('14:04', style: TextStyle(color: Color(0xFF9AA8B6), fontSize: 12, fontWeight: FontWeight.w600)),
        ],
      ),
    );
  }
}

class _Composer extends StatelessWidget {
  const _Composer();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 16),
      decoration: const BoxDecoration(color: Colors.white, border: Border(top: BorderSide(color: Color(0xFFE1E7EC)))),
      child: Column(
        children: [
          SizedBox(
            height: 38,
            child: ListView(
              scrollDirection: Axis.horizontal,
              children: const [
                _SuggestionChip('Est-ce disponible ?'),
                SizedBox(width: 8),
                _SuggestionChip('Délai de livraison ?'),
                SizedBox(width: 8),
                _SuggestionChip('Possible de négocier ?'),
              ],
            ),
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              Container(width: 48, height: 48, decoration: BoxDecoration(color: const Color(0xFFF4F6F5), borderRadius: BorderRadius.circular(14)), child: const Icon(Icons.image_outlined, color: Color(0xFF66778C))),
              const SizedBox(width: 10),
              Expanded(
                child: Container(
                  height: 48,
                  padding: const EdgeInsets.symmetric(horizontal: 18),
                  decoration: BoxDecoration(color: const Color(0xFFF4F6F5), borderRadius: BorderRadius.circular(16)),
                  alignment: Alignment.centerLeft,
                  child: const Text('Écrire un message...', style: TextStyle(color: Color(0xFF9AA8B6), fontSize: 15, fontWeight: FontWeight.w600)),
                ),
              ),
              const SizedBox(width: 10),
              Container(width: 56, height: 56, decoration: BoxDecoration(color: _green, borderRadius: BorderRadius.circular(18)), child: const Icon(Icons.send_rounded, color: Colors.white, size: 25)),
            ],
          ),
        ],
      ),
    );
  }
}

class _SuggestionChip extends StatelessWidget {
  const _SuggestionChip(this.label);
  final String label;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 16),
      alignment: Alignment.center,
      decoration: BoxDecoration(color: const Color(0xFFF4F6F5), borderRadius: BorderRadius.circular(12)),
      child: Text(label, style: const TextStyle(color: _navy, fontSize: 13, fontWeight: FontWeight.w800)),
    );
  }
}
