import 'package:flutter/material.dart';

import '../../core/mock/mission_mock_data.dart';
import '../../core/theme/app_theme.dart';

/// Fond de carte esquissé.
///
/// CE N'EST PAS UNE CARTE, ET JE NE FAIS PAS SEMBLANT.
///
/// Google Maps exigerait une clé d'API, une permission de localisation et un
/// coût par affichage — pour un trajet figé entre deux points fictifs. Le
/// quadrillage dit « ici il y aura une carte » sans prétendre en être une.
///
/// Le tracé est un L : deux segments perpendiculaires. Une polyligne courbe
/// laisserait croire à un vrai calcul d'itinéraire.
class MissionMapSketch extends StatelessWidget {
  const MissionMapSketch({
    super.key,
    required this.markerLabel,
    required this.markerColor,
    this.dark = false,
  });

  final String markerLabel;
  final Color markerColor;

  /// Fond sombre : la proposition de mission (écran 06) s'affiche par-dessus une
  /// carte assombrie, pour que la feuille blanche happe le regard.
  final bool dark;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Stack(
      fit: StackFit.expand,
      children: [
        ColoredBox(
          color: dark ? const Color(0xFF10181F) : const Color(0xFFEDF1EE),
          child: CustomPaint(
            painter: _MapPainter(
              grid: dark
                  ? Colors.white.withValues(alpha: 0.06)
                  : colors.line.withValues(alpha: 0.9),
              water: dark
                  ? const Color(0xFF1B2A38)
                  : const Color(0xFFD8E6F2),
              route: dark ? Colors.transparent : const Color(0xFF3B6FD4),
            ),
          ),
        ),
        if (!dark)
          Align(
            alignment: const Alignment(0, -0.32),
            child: _Marker(label: markerLabel, color: markerColor),
          ),
      ],
    );
  }
}

class _Marker extends StatelessWidget {
  const _Marker({required this.label, required this.color});

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) => Container(
        width: 26,
        height: 26,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: color,
          borderRadius: BorderRadius.circular(8),
          border: Border.all(color: Colors.white, width: 2.5),
        ),
        child: Text(
          label,
          style: const TextStyle(
            fontSize: 11,
            fontWeight: FontWeight.w800,
            color: Colors.white,
          ),
        ),
      );
}

class _MapPainter extends CustomPainter {
  const _MapPainter({
    required this.grid,
    required this.water,
    required this.route,
  });

  final Color grid;
  final Color water;
  final Color route;

  @override
  void paint(Canvas canvas, Size size) {
    final g = Paint()
      ..color = grid
      ..strokeWidth = 1;
    const step = 56.0;
    for (var x = 0.0; x < size.width; x += step) {
      canvas.drawLine(Offset(x, 0), Offset(x, size.height), g);
    }
    for (var y = 0.0; y < size.height; y += step) {
      canvas.drawLine(Offset(0, y), Offset(size.width, y), g);
    }

    // La lagune de Cotonou, en diagonale.
    canvas.drawPath(
      Path()
        ..moveTo(0, size.height * 0.56)
        ..lineTo(size.width, size.height * 0.40)
        ..lineTo(size.width, size.height * 0.48)
        ..lineTo(0, size.height * 0.64)
        ..close(),
      Paint()..color = water,
    );

    if (route.alpha == 0) return;

    final r = Paint()
      ..color = route
      ..strokeWidth = 5
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round
      ..strokeJoin = StrokeJoin.round;
    canvas.drawPath(
      Path()
        ..moveTo(size.width * 0.30, size.height * 0.92)
        ..lineTo(size.width * 0.30, size.height * 0.62)
        ..lineTo(size.width * 0.56, size.height * 0.62)
        ..lineTo(size.width * 0.56, size.height * 0.28),
      r,
    );
  }

  @override
  bool shouldRepaint(covariant _MapPainter old) =>
      old.grid != grid || old.water != water || old.route != route;
}

/// Badge d'univers : « HBA FOOD », « HBAEXPRESS », « HBA DELIVERY ».
class UniverseBadge extends StatelessWidget {
  const UniverseBadge({super.key, required this.universe, this.compact = false});

  final MissionUniverse universe;
  final bool compact;

  @override
  Widget build(BuildContext context) => Container(
        padding: EdgeInsets.symmetric(
          horizontal: compact ? 7 : 9,
          vertical: compact ? 3 : 5,
        ),
        decoration: BoxDecoration(
          color: universe.soft,
          borderRadius: BorderRadius.circular(6),
        ),
        child: Text(
          universe.badge,
          style: TextStyle(
            fontSize: compact ? 9.5 : 10.5,
            fontWeight: FontWeight.w800,
            letterSpacing: 0.5,
            color: universe.accent,
          ),
        ),
      );
}

/// Pastille de statut d'une mission dans les listes.
class MissionStatusPill extends StatelessWidget {
  const MissionStatusPill({super.key, required this.status});

  final MissionListStatus status;

  /// « ANNULÉE » EST ROUGE, « LIVRÉE » EST VERTE, « EN COURS » EST AMBRE.
  ///
  /// L'ambre dit « quelque chose est en train de se passer et vous concerne ».
  /// Le vert dit « c'est réglé ». Le rouge dit « ça n'a pas abouti ». Trois
  /// messages différents, trois couleurs — les confondre obligerait à lire le
  /// mot à chaque ligne.
  static (Color, Color) _tone(MissionListStatus s) => switch (s) {
        MissionListStatus.inProgress => (AppTheme.amber, AppTheme.amberSoft),
        MissionListStatus.available => (AppTheme.info, AppTheme.infoSoft),
        MissionListStatus.delivered => (AppTheme.brandGreen, AppTheme.brandGreenSoft),
        MissionListStatus.cancelled => (AppTheme.danger, AppTheme.dangerSoft),
      };

  @override
  Widget build(BuildContext context) {
    final (tint, wash) = _tone(status);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 4),
      decoration: BoxDecoration(
        color: wash,
        borderRadius: BorderRadius.circular(7),
      ),
      child: Text(
        status.label,
        style: TextStyle(
          fontSize: 11.5,
          fontWeight: FontWeight.w700,
          color: tint,
        ),
      ),
    );
  }
}

/// En-tête flottant des écrans de navigation : retour, bandeau d'état, SOS.
class MissionMapHeader extends StatelessWidget {
  const MissionMapHeader({
    super.key,
    required this.label,
    required this.color,
    required this.onBack,
    required this.onSos,
    this.subtitle,
  });

  final String label;
  final Color color;
  final VoidCallback onBack;
  final VoidCallback onSos;
  final String? subtitle;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            _RoundButton(
              onTap: onBack,
              child: Icon(Icons.chevron_left, size: 22, color: colors.ink),
            ),
            const SizedBox(width: 8),
            Expanded(
              child: Container(
                height: AppTheme.minTapTarget,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: color,
                  borderRadius: BorderRadius.circular(24),
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const _WhiteDot(),
                    const SizedBox(width: 8),
                    Text(
                      label,
                      style: const TextStyle(
                        fontSize: 12.5,
                        fontWeight: FontWeight.w800,
                        letterSpacing: 0.6,
                        color: Colors.white,
                      ),
                    ),
                  ],
                ),
              ),
            ),
            const SizedBox(width: 8),
            // SOS TOUJOURS VISIBLE PENDANT LA CONDUITE, JAMAIS DANS UN MENU.
            //
            // C'est le seul bouton dont on peut avoir besoin en une seconde, et
            // le seul qu'on cherchera sans regarder l'écran. L'enfouir sous trois
            // taps le rendrait inutile au moment précis où il sert.
            _RoundButton(
              onTap: onSos,
              child: const Text(
                'SOS',
                style: TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.w800,
                  color: AppTheme.danger,
                ),
              ),
            ),
          ],
        ),
        if (subtitle != null) ...[
          const SizedBox(height: 8),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
            decoration: BoxDecoration(
              color: AppTheme.charcoal,
              borderRadius: BorderRadius.circular(20),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Container(
                  width: 7,
                  height: 7,
                  decoration: const BoxDecoration(
                    color: AppTheme.brandGreen,
                    shape: BoxShape.circle,
                  ),
                ),
                const SizedBox(width: 7),
                Text(
                  subtitle!,
                  style: const TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.w600,
                    color: Colors.white,
                  ),
                ),
              ],
            ),
          ),
        ],
      ],
    );
  }
}

class _WhiteDot extends StatelessWidget {
  const _WhiteDot();

  @override
  Widget build(BuildContext context) => Container(
        width: 7,
        height: 7,
        decoration: const BoxDecoration(color: Colors.white, shape: BoxShape.circle),
      );
}

class _RoundButton extends StatelessWidget {
  const _RoundButton({required this.onTap, required this.child});

  final VoidCallback onTap;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(24),
      child: Container(
        width: AppTheme.minTapTarget,
        height: AppTheme.minTapTarget,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: colors.surface,
          shape: BoxShape.circle,
        ),
        child: child,
      ),
    );
  }
}

/// Lien rouge discret : « Signaler un problème », « Client absent ».
class MissionTroubleLink extends StatelessWidget {
  const MissionTroubleLink({super.key, required this.label, this.onTap});

  final String label;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) => TextButton(
        onPressed: onTap,
        style: TextButton.styleFrom(
          minimumSize: const Size(0, AppTheme.minTapTarget),
          foregroundColor: AppTheme.danger,
          textStyle: const TextStyle(fontSize: 13.5, fontWeight: FontWeight.w600),
        ),
        child: Text(label),
      );
}
