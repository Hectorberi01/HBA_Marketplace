import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/mock/driver_mock_data.dart';
import '../../../core/mock/driver_state.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/driver_widgets.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 04 / 05 — ACCUEIL. Un seul écran, deux visages.
///
/// UN ÉCRAN, PAS DEUX, MALGRÉ DEUX ENTRÉES DANS VOTRE PROTOTYPE.
///
/// « Dashboard hors ligne » et « Dashboard disponible » partagent l'en-tête, les
/// statistiques de la journée et la barre d'onglets. Ce qui change tient à la
/// carte de bascule et à ce qui la suit. Deux écrans auraient dupliqué le reste,
/// et la bascule — le geste le plus fréquent de la journée — serait devenue une
/// NAVIGATION, avec sa transition et son risque de retour arrière.
///
/// LES DIFFÉRENCES ENTRE LES DEUX ÉTATS NE SONT PAS COSMÉTIQUES.
///
/// Hors ligne : carte NOIRE, quatre tuiles en grille, et rien d'autre. Rien de
/// ce qui suit n'a de sens quand on ne reçoit pas de missions — ni la zone, ni
/// le bonus, ni la simulation.
///
/// Disponible : carte VERTE, trois tuiles en ligne, la zone d'activité, le bonus
/// du jour et le déclencheur de mission. La maquette le dit : « Jamais un écran
/// vide. »
/// ═════════════════════════════════════════════════════════════════════════════
class DriverHomeScreen extends ConsumerWidget {
  const DriverHomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final online = ref.watch(driverOnlineProvider);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        bottom: false,
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
          children: [
            _Greeting(online: online),
            const SizedBox(height: 14),

            _StatusCard(
              online: online,
              onToggle: () =>
                  ref.read(driverOnlineProvider.notifier).state = !online,
            ),
            const SizedBox(height: 14),

            if (online) const _OnlineStats() else const _OfflineStats(),
            const SizedBox(height: 14),

            if (online) ...[
              const _ZoneCard(),
              const SizedBox(height: 14),
              const _BonusCard(),
              const SizedBox(height: 14),
              _SimulateButton(
                label: 'Simuler l\'arrivée d\'une mission',
                onTap: () => context.push('/mission/new'),
              ),
              const SizedBox(height: 10),
              // AJOUTÉ : L'ÉCRAN HORS LIGNE N'AVAIT AUCUNE PORTE D'ENTRÉE.
              //
              // Il est déclaré dans votre prototype (22) mais aucun geste n'y
              // menait — en service, il s'affiche tout seul à la perte du
              // réseau. Ce déclencheur existe pour le montrer, et disparaîtra
              // avec les autres boutons de simulation.
              _SimulateButton(
                label: 'Simuler la perte de connexion',
                onTap: () => context.push('/offline'),
              ),
            ] else
              const DriverEmptyBox(
                title: 'Aucune mission active',
                message: 'Passez disponible pour recevoir une livraison.',
              ),
          ],
        ),
      ),
    );
  }
}

class _Greeting extends StatelessWidget {
  const _Greeting({required this.online});

  final bool online;


  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Row(
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'Bonjour ${DriverMockData.driverName}',
                style: TextStyle(
                  fontSize: 24,
                  fontWeight: FontWeight.w800,
                  color: colors.ink,
                ),
              ),
              const SizedBox(height: 2),
              Row(
                children: [
                  // Pastille verte seulement quand on est disponible. Hors ligne,
                  // la maquette n'en met aucune : un point gris se lirait comme
                  // un état « indéterminé » alors qu'il est parfaitement défini.
                  if (online) ...[
                    Container(
                      width: 7,
                      height: 7,
                      decoration: const BoxDecoration(
                        color: AppTheme.brandGreen,
                        shape: BoxShape.circle,
                      ),
                    ),
                    const SizedBox(width: 6),
                  ],
                  Text(
                    online ? 'Disponible' : 'Vous êtes hors ligne',
                    style: TextStyle(
                      fontSize: 13.5,
                      fontWeight: online ? FontWeight.w700 : FontWeight.w500,
                      color: online ? AppTheme.brandGreen : colors.subtle,
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
        DriverBell(onTap: () => context.push('/notifications')),
      ],
    );
  }
}

/// La carte de bascule — le seul élément que le livreur touche vraiment souvent.
class _StatusCard extends StatelessWidget {
  const _StatusCard({required this.online, required this.onToggle});

  final bool online;
  final VoidCallback onToggle;

  @override
  Widget build(BuildContext context) => InkWell(
        // TOUTE LA CARTE EST CLIQUABLE, PAS SEULEMENT L'INTERRUPTEUR.
        //
        // « Touchez pour passer disponible », écrit la maquette. Un interrupteur
        // seul fait une cible de 50 px pour quelqu'un qui a un casque sur la tête
        // et une main sur le guidon.
        onTap: onToggle,
        borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        child: Container(
          padding: const EdgeInsets.fromLTRB(18, 16, 16, 16),
          decoration: BoxDecoration(
            // Noir hors ligne, vert disponible. Le contraste entre les deux états
            // se voit à un mètre, ce qui est la bonne distance : on vérifie son
            // statut d'un coup d'œil au téléphone posé sur le guidon.
            color: online ? AppTheme.brandGreen : AppTheme.charcoal,
            borderRadius: BorderRadius.circular(AppTheme.radiusCard),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'VOUS ÊTES',
                          style: TextStyle(
                            fontSize: 10.5,
                            fontWeight: FontWeight.w800,
                            letterSpacing: 1,
                            color: Colors.white.withValues(alpha: 0.65),
                          ),
                        ),
                        const SizedBox(height: 3),
                        Text(
                          online ? 'DISPONIBLE' : 'HORS LIGNE',
                          style: const TextStyle(
                            fontSize: 25,
                            fontWeight: FontWeight.w800,
                            color: Colors.white,
                          ),
                        ),
                      ],
                    ),
                  ),
                  Switch(
                    value: online,
                    onChanged: (_) => onToggle(),
                    activeThumbColor: Colors.white,
                    activeTrackColor: Colors.white.withValues(alpha: 0.32),
                    inactiveThumbColor: Colors.white,
                    inactiveTrackColor: Colors.white.withValues(alpha: 0.18),
                    trackOutlineColor:
                        const WidgetStatePropertyAll(Colors.transparent),
                  ),
                ],
              ),
              const SizedBox(height: 14),
              Divider(height: 1, color: Colors.white.withValues(alpha: 0.18)),
              const SizedBox(height: 12),
              Text(
                online
                    ? 'Vous pouvez maintenant recevoir des missions.'
                    : 'Vous ne recevrez pas de nouvelles missions.\n'
                        'Touchez pour passer disponible.',
                style: TextStyle(
                  fontSize: 13.5,
                  height: 1.4,
                  color: Colors.white.withValues(alpha: 0.9),
                ),
              ),
            ],
          ),
        ),
      );
}

/// Hors ligne : quatre tuiles en grille 2 × 2.
class _OfflineStats extends StatelessWidget {
  const _OfflineStats();

  @override
  Widget build(BuildContext context) => const Column(
        children: [
          Row(
            children: [
              Expanded(
                child: DriverStatTile(
                  label: 'Gains aujourd\'hui',
                  value: '15 500',
                  unit: Format.cfa,
                ),
              ),
              SizedBox(width: 12),
              Expanded(
                child: DriverStatTile(label: 'Livraisons', value: '8'),
              ),
            ],
          ),
          SizedBox(height: 12),
          Row(
            children: [
              Expanded(
                child: DriverStatTile(label: 'Temps en ligne', value: '4 h 20'),
              ),
              SizedBox(width: 12),
              Expanded(
                child: DriverStatTile(label: 'Note', value: '4,9 ★'),
              ),
            ],
          ),
        ],
      );
}

/// Disponible : trois tuiles en ligne — la note se replie sous le temps.
///
/// LA NOTE PERD SA TUILE, ELLE NE DISPARAÎT PAS.
///
/// La maquette la glisse en légende sous « En ligne ». C'est cohérent : une note
/// ne bouge pas d'une course à l'autre, elle n'a pas à occuper le quart de
/// l'écran quand la zone et le bonus, eux, changent dans la journée.
class _OnlineStats extends StatelessWidget {
  const _OnlineStats();

  @override
  Widget build(BuildContext context) => const IntrinsicHeight(
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Expanded(
              child: DriverStatTile(
                label: 'Gains',
                value: '15 500',
                caption: Format.cfa,
              ),
            ),
            SizedBox(width: 12),
            Expanded(
              child: DriverStatTile(
                label: 'Courses',
                value: '8',
                caption: 'aujourd\'hui',
              ),
            ),
            SizedBox(width: 12),
            Expanded(
              child: DriverStatTile(
                label: 'En ligne',
                value: '4 h 20',
                caption: '4,9 ★',
              ),
            ),
          ],
        ),
      );
}

/// Zone d'activité : un aperçu de carte, un libellé, et l'attente de mission.
///
/// CE N'EST PAS UNE VRAIE CARTE, ET JE NE FAIS PAS SEMBLANT.
///
/// Poser Google Maps ici exigerait une clé d'API, une permission de localisation
/// et un coût par affichage — pour un écran dont les données sont figées. Le
/// motif quadrillé dit « ici il y aura une carte » sans prétendre en être une.
class _ZoneCard extends StatelessWidget {
  const _ZoneCard();

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Container(
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        border: Border.all(color: colors.line),
      ),
      clipBehavior: Clip.antiAlias,
      child: Column(
        children: [
          SizedBox(
            height: 150,
            child: Stack(
              children: [
                Positioned.fill(
                  child: CustomPaint(painter: _MapSketchPainter(colors.line)),
                ),
                Center(
                  child: Container(
                    width: 132,
                    height: 132,
                    decoration: BoxDecoration(
                      color: AppTheme.brandGreen.withValues(alpha: 0.10),
                      shape: BoxShape.circle,
                      border: Border.all(
                        color: AppTheme.brandGreen.withValues(alpha: 0.22),
                      ),
                    ),
                    child: const Center(
                      child: _Dot(size: 11, color: AppTheme.brandGreen),
                    ),
                  ),
                ),
                Positioned(
                  left: 12,
                  bottom: 12,
                  child: Container(
                    padding:
                        const EdgeInsets.symmetric(horizontal: 11, vertical: 7),
                    decoration: BoxDecoration(
                      color: colors.surface,
                      borderRadius: BorderRadius.circular(20),
                      border: Border.all(color: colors.line),
                    ),
                    child: Text(
                      'Zone ${DriverMockData.zoneName} · '
                      '${DriverMockData.zoneDemand}',
                      style: TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w600,
                        color: colors.ink,
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),

          Padding(
            padding: const EdgeInsets.fromLTRB(14, 12, 14, 14),
            child: Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'Aucune mission active',
                        style: TextStyle(
                          fontSize: 15.5,
                          fontWeight: FontWeight.w800,
                          color: colors.ink,
                        ),
                      ),
                      const SizedBox(height: 2),
                      Text(
                        'Nous vous préviendrons dès qu\'une mission arrive.',
                        style: TextStyle(fontSize: 12.5, color: colors.subtle),
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 12),
                // Anneau en rotation : il dit que l'application ÉCOUTE. Sans
                // lui, « aucune mission active » se lit comme une panne.
                SizedBox(
                  width: 26,
                  height: 26,
                  child: CircularProgressIndicator(
                    strokeWidth: 2.4,
                    color: AppTheme.brandGreen,
                    backgroundColor: colors.line,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _Dot extends StatelessWidget {
  const _Dot({required this.size, required this.color});

  final double size;
  final Color color;

  @override
  Widget build(BuildContext context) => Container(
        width: size,
        height: size,
        decoration: BoxDecoration(color: color, shape: BoxShape.circle),
      );
}

/// Quadrillage discret évoquant un plan de ville, plus une diagonale d'eau.
class _MapSketchPainter extends CustomPainter {
  const _MapSketchPainter(this.line);

  final Color line;

  @override
  void paint(Canvas canvas, Size size) {
    final grid = Paint()
      ..color = line
      ..strokeWidth = 1;

    const step = 34.0;
    for (var x = 0.0; x < size.width; x += step) {
      canvas.drawLine(Offset(x, 0), Offset(x, size.height), grid);
    }
    for (var y = 0.0; y < size.height; y += step) {
      canvas.drawLine(Offset(0, y), Offset(size.width, y), grid);
    }

    // Cotonou est bordée par la lagune : la diagonale bleutée l'évoque, sans
    // prétendre correspondre à une géographie réelle.
    final water = Paint()
      ..color = const Color(0xFFD8E6F2)
      ..style = PaintingStyle.fill;
    final path = Path()
      ..moveTo(size.width * 0.62, size.height)
      ..lineTo(size.width, size.height * 0.42)
      ..lineTo(size.width, size.height * 0.62)
      ..lineTo(size.width * 0.78, size.height)
      ..close();
    canvas.drawPath(path, water);
  }

  @override
  bool shouldRepaint(covariant _MapSketchPainter oldDelegate) =>
      oldDelegate.line != line;
}

/// Bonus du jour — carte sombre, accent ambre.
class _BonusCard extends StatelessWidget {
  const _BonusCard();

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.fromLTRB(16, 14, 16, 15),
        decoration: BoxDecoration(
          color: AppTheme.charcoal,
          borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'BONUS DU JOUR',
              style: TextStyle(
                fontSize: 10.5,
                fontWeight: FontWeight.w800,
                letterSpacing: 1,
                color: AppTheme.amber,
              ),
            ),
            const SizedBox(height: 7),
            Text(
              '${DriverMockData.bonusTarget} livraisons aujourd\'hui · '
              '+${Format.amount(DriverMockData.bonusAmount)} F',
              style: const TextStyle(
                fontSize: 15,
                fontWeight: FontWeight.w800,
                color: Colors.white,
              ),
            ),
            const SizedBox(height: 12),

            ClipRRect(
              borderRadius: BorderRadius.circular(3),
              child: LinearProgressIndicator(
                value: DriverMockData.bonusProgress,
                minHeight: 5,
                backgroundColor: Colors.white.withValues(alpha: 0.16),
                valueColor: const AlwaysStoppedAnimation(AppTheme.amber),
              ),
            ),
            const SizedBox(height: 8),

            Text(
              // « PLUS QUE N COURSES » EST CALCULÉ, JAMAIS ÉCRIT.
              //
              // La maquette affiche « 8 sur 10 · plus que 2 courses » avec huit
              // livraisons au compteur. Figer le « 2 » aurait promis deux courses
              // restantes après la neuvième — l'erreur qu'un livreur repère
              // immédiatement, parce que c'est son argent.
              '${DriverMockData.deliveriesToday} sur ${DriverMockData.bonusTarget}'
              '${DriverMockData.bonusRemaining > 0 ? ' · plus que ${DriverMockData.bonusRemaining} '
                  '${DriverMockData.bonusRemaining > 1 ? 'courses' : 'course'}' : ' · bonus atteint'}',
              style: TextStyle(
                fontSize: 12,
                color: Colors.white.withValues(alpha: 0.7),
              ),
            ),
          ],
        ),
      );
}

/// Déclencheur de démonstration.
///
/// IL EST SUR LA MAQUETTE, ET IL NE DOIT PAS SURVIVRE À LA DÉMONSTRATION.
///
/// « Simuler l'arrivée d'une mission » n'a de sens que dans un prototype : en
/// production, une mission arrive par notification, pas par un bouton. D'où le
/// contour en pointillés et le fond pâle — il ne ressemble à aucun autre bouton
/// de l'application, exprès.
class _SimulateButton extends StatelessWidget {
  const _SimulateButton({required this.label, required this.onTap});

  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(AppTheme.radiusField),
        child: Container(
          height: 52,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: AppTheme.brandGreenSoft.withValues(alpha: 0.55),
            borderRadius: BorderRadius.circular(AppTheme.radiusField),
            border: Border.all(
              color: AppTheme.brandGreen.withValues(alpha: 0.35),
            ),
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.play_arrow_rounded, size: 18, color: AppTheme.brandGreen),
              const SizedBox(width: 7),
              Text(
                label,
                style: const TextStyle(
                  fontSize: 14.5,
                  fontWeight: FontWeight.w700,
                  color: AppTheme.brandGreen,
                ),
              ),
            ],
          ),
        ),
      );
}
