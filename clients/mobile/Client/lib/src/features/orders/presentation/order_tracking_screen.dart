import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui_kit.dart';

const _green = AppTheme.brandGreen;
const _navy = Color(0xFF0E2239);
const _orange = Color(0xFFE56400);

class OrderTrackingScreen extends StatelessWidget {
  const OrderTrackingScreen({super.key, required this.orderId});
  final String orderId;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF4F6F5),
      body: SafeArea(
        bottom: false,
        child: CustomScrollView(
          slivers: [
            SliverToBoxAdapter(child: _MapPanel(onBack: () => context.canPop() ? context.pop() : context.go('/orders'))),
            const SliverToBoxAdapter(child: _TrackingCard()),
            SliverToBoxAdapter(
              child: Padding(
                padding: EdgeInsets.fromLTRB(16, 24, 16, bottomSafePadding(context, extra: 92)),
                child: Row(
                  children: [
                    Expanded(
                      child: FilledButton(
                        onPressed: () {},
                        style: FilledButton.styleFrom(
                          backgroundColor: _green,
                          minimumSize: const Size.fromHeight(54),
                          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
                        ),
                        child: const Text('Contacter le livreur', style: TextStyle(fontSize: 15, fontWeight: FontWeight.w900)),
                      ),
                    ),
                    const SizedBox(width: 12),
                    OutlinedButton(
                      onPressed: () {},
                      style: OutlinedButton.styleFrom(
                        foregroundColor: _navy,
                        minimumSize: const Size(78, 54),
                        side: const BorderSide(color: Color(0xFFD8E0E7)),
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
                      ),
                      child: const Text('Aide', style: TextStyle(fontSize: 15, fontWeight: FontWeight.w900)),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _MapPanel extends StatelessWidget {
  const _MapPanel({required this.onBack});
  final VoidCallback onBack;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 430,
      child: Stack(
        children: [
          Container(
            color: const Color(0xFFDDE8E3),
            child: CustomPaint(painter: _MapPainter(), child: const SizedBox.expand()),
          ),
          Positioned(
            left: 18,
            top: 18,
            child: _FloatingIcon(icon: Icons.chevron_left_rounded, onTap: onBack),
          ),
          Positioned(
            right: 18,
            top: 18,
            child: Container(
              height: 54,
              padding: const EdgeInsets.symmetric(horizontal: 22),
              alignment: Alignment.center,
              decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(16), boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.10), blurRadius: 18, offset: const Offset(0, 8))]),
              child: const Text('HBA DELIVERY', style: TextStyle(color: _green, fontSize: 13, fontWeight: FontWeight.w900)),
            ),
          ),
          const Positioned(left: 42, top: 54, child: Text('9:41', style: TextStyle(color: _navy, fontSize: 14, fontWeight: FontWeight.w900))),
          Positioned(
            left: 96,
            top: 250,
            child: Container(width: 30, height: 30, decoration: BoxDecoration(color: const Color(0xFFEAF5F0), shape: BoxShape.circle, border: Border.all(color: _green, width: 3))),
          ),
          Positioned(
            right: 150,
            top: 180,
            child: Container(
              width: 48,
              height: 48,
              decoration: BoxDecoration(color: _green, shape: BoxShape.circle, border: Border.all(color: Colors.white, width: 3), boxShadow: [BoxShadow(color: _green.withValues(alpha: 0.22), blurRadius: 24)]),
              child: const Icon(Icons.delivery_dining_rounded, color: Colors.white, size: 24),
            ),
          ),
        ],
      ),
    );
  }
}

class _FloatingIcon extends StatelessWidget {
  const _FloatingIcon({required this.icon, required this.onTap});
  final IconData icon;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: Container(
        width: 54,
        height: 54,
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(16), boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.10), blurRadius: 18, offset: const Offset(0, 8))]),
        child: Icon(icon, color: _navy),
      ),
    );
  }
}

class _TrackingCard extends StatelessWidget {
  const _TrackingCard();

  @override
  Widget build(BuildContext context) {
    return Transform.translate(
      offset: const Offset(0, -46),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16),
        child: Container(
          padding: const EdgeInsets.fromLTRB(22, 26, 22, 24),
          decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(24)),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('Arrivée estimée', style: TextStyle(color: Color(0xFF65768B), fontSize: 14, fontWeight: FontWeight.w700)),
                        SizedBox(height: 2),
                        Text('12 min', style: TextStyle(color: _navy, fontSize: 30, fontWeight: FontWeight.w900)),
                      ],
                    ),
                  ),
                  _Capsule(label: 'FOOD', color: _orange),
                ],
              ),
              const SizedBox(height: 22),
              Container(
                padding: const EdgeInsets.all(14),
                decoration: BoxDecoration(color: const Color(0xFFF3F5F6), borderRadius: BorderRadius.circular(18)),
                child: Row(
                  children: [
                    Container(width: 56, height: 56, decoration: BoxDecoration(color: const Color(0xFFDCE6EC), borderRadius: BorderRadius.circular(14))),
                    const SizedBox(width: 14),
                    const Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text('Ibrahim · Livreur HBA', maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: _navy, fontSize: 15, fontWeight: FontWeight.w900)),
                          SizedBox(height: 3),
                          Text('Moto · AB 4821 RB · ★ 4,9', maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: Color(0xFF65768B), fontSize: 13, fontWeight: FontWeight.w600)),
                        ],
                      ),
                    ),
                    Container(width: 52, height: 52, decoration: BoxDecoration(color: _green, borderRadius: BorderRadius.circular(15)), child: const Icon(Icons.phone_in_talk_outlined, color: Colors.white)),
                  ],
                ),
              ),
              const SizedBox(height: 24),
              const _Timeline(),
            ],
          ),
        ),
      ),
    );
  }
}

class _Timeline extends StatelessWidget {
  const _Timeline();

  static const steps = [
    _Step('Commande confirmée', '12:24', true, false),
    _Step('Préparation', '12:29', true, false),
    _Step('Livreur récupère la commande', '12:38', true, false),
    _Step('En route vers vous', 'Maintenant', false, true),
    _Step('Livrée', 'Estimée 12:56', false, false),
  ];

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        for (var i = 0; i < steps.length; i++) _TimelineRow(step: steps[i], last: i == steps.length - 1),
      ],
    );
  }
}

class _TimelineRow extends StatelessWidget {
  const _TimelineRow({required this.step, required this.last});
  final _Step step;
  final bool last;

  @override
  Widget build(BuildContext context) {
    final active = step.done || step.current;
    return IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Column(
            children: [
              Container(
                width: 20,
                height: 20,
                decoration: BoxDecoration(
                  color: step.done ? _green : Colors.white,
                  shape: BoxShape.circle,
                  border: Border.all(color: active ? _green : const Color(0xFFD3DDE5), width: 3),
                ),
              ),
              if (!last) Expanded(child: Container(width: 2, color: active ? _green : const Color(0xFFD3DDE5))),
            ],
          ),
          const SizedBox(width: 20),
          Expanded(
            child: Padding(
              padding: EdgeInsets.only(bottom: last ? 0 : 22),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(step.title, style: TextStyle(color: active ? _navy : const Color(0xFF9AA8B6), fontSize: 15, fontWeight: FontWeight.w900)),
                  const SizedBox(height: 3),
                  Text(step.time, style: const TextStyle(color: Color(0xFF96A6B6), fontSize: 13, fontWeight: FontWeight.w600)),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _Capsule extends StatelessWidget {
  const _Capsule({required this.label, required this.color});
  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 7),
      decoration: BoxDecoration(color: color.withValues(alpha: 0.10), borderRadius: BorderRadius.circular(12)),
      child: Text(label, style: TextStyle(color: color, fontSize: 12, letterSpacing: 1.2, fontWeight: FontWeight.w900)),
    );
  }
}

class _Step {
  const _Step(this.title, this.time, this.done, this.current);
  final String title;
  final String time;
  final bool done;
  final bool current;
}

class _MapPainter extends CustomPainter {
  @override
  void paint(Canvas canvas, Size size) {
    final gridPaint = Paint()
      ..color = const Color(0xFFD2DED8)
      ..strokeWidth = 1;
    for (var x = 0.0; x < size.width; x += 58) {
      canvas.drawLine(Offset(x, 0), Offset(x, size.height), gridPaint);
    }
    for (var y = 0.0; y < size.height; y += 58) {
      canvas.drawLine(Offset(0, y), Offset(size.width, y), gridPaint);
    }

    final road = Paint()
      ..color = Colors.white.withValues(alpha: 0.72)
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round
      ..strokeWidth = 30;
    final roadPath = Path()
      ..moveTo(-20, size.height * 0.55)
      ..lineTo(size.width * 0.28, size.height * 0.46)
      ..lineTo(size.width + 40, size.height * 0.28);
    canvas.drawPath(roadPath, road);
    canvas.drawLine(Offset(size.width * 0.10, -20), Offset(size.width * 0.02, size.height), road);

    final route = Paint()
      ..color = _green
      ..style = PaintingStyle.stroke
      ..strokeWidth = 5
      ..strokeCap = StrokeCap.round;
    final path = Path()
      ..moveTo(size.width * 0.16, size.height * 0.67)
      ..cubicTo(size.width * 0.30, size.height * 0.68, size.width * 0.30, size.height * 0.48, size.width * 0.45, size.height * 0.48)
      ..cubicTo(size.width * 0.62, size.height * 0.48, size.width * 0.55, size.height * 0.38, size.width * 0.72, size.height * 0.38);
    canvas.drawPath(path, route);
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}
