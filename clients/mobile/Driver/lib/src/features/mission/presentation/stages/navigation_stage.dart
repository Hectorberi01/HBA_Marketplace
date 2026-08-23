import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../../core/mock/mission_mock_data.dart';
import '../../../../core/mock/mission_state.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../shared/utils/formatters.dart';
import '../../../../shared/widgets/driver_widgets.dart';
import '../../../../shared/widgets/mission_widgets.dart';
import '../mission_flow_screen.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 08 · VERS LE RETRAIT — 12 · EN LIVRAISON.
///
/// UN SEUL ÉCRAN POUR LES DEUX, ET LA DIFFÉRENCE TIENT À TROIS CHOSES.
///
/// Carte dominante, feuille compacte, « Je suis arrivé » : la structure est
/// identique. Ce qui change :
///
///   • la couleur du bandeau — AMBRE vers le retrait, VERTE en livraison ;
///   • le destinataire de l'appel — l'établissement, puis le client ;
///   • le gain, affiché seulement en livraison, quand il est acquis.
///
/// Deux fichiers auraient dupliqué la carte, la feuille, le bandeau et le SOS
/// pour trois différences — et la première correction n'aurait été portée que
/// d'un côté.
///
/// LE SUIVI DE POSITION N'EST ANNONCÉ QUE VERS LE RETRAIT.
///
/// « Suivi actif · position partagée » figure sur l'écran 08 et pas sur le 12.
/// Je suis la maquette, mais je le signale : c'est en LIVRAISON que le client
/// suit le livreur. L'inverse serait plus attendu.
/// ═════════════════════════════════════════════════════════════════════════════
class NavigationStage extends ConsumerWidget {
  const NavigationStage({super.key, required this.toPickup});

  final bool toPickup;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);

    return Scaffold(
      backgroundColor: colors.bg,
      body: Stack(
        children: [
          Positioned.fill(
            child: MissionMapSketch(
              markerLabel: toPickup ? 'P' : 'D',
              markerColor: toPickup ? AppTheme.amber : AppTheme.brandGreen,
            ),
          ),

          SafeArea(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(12, 8, 12, 0),
              child: MissionMapHeader(
                label: toPickup ? 'EN ROUTE VERS LE RETRAIT' : 'EN LIVRAISON',
                color: toPickup ? AppTheme.amber : AppTheme.brandGreen,
                subtitle: toPickup ? 'Suivi actif · position partagée' : null,
                onBack: () => leaveMission(context, ref),
                onSos: () => context.push('/support'),
              ),
            ),
          ),

          Align(
            alignment: Alignment.bottomCenter,
            child: _Sheet(toPickup: toPickup),
          ),
        ],
      ),
    );
  }
}

class _Sheet extends ConsumerWidget {
  const _Sheet({required this.toPickup});

  final bool toPickup;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);

    final minutes = toPickup
        ? MissionMockData.pickupEtaMin
        : MissionMockData.dropoffEtaMin;
    final distance = toPickup
        ? MissionMockData.pickupDistanceKm
        : MissionMockData.dropoffDistanceKm;
    final clock = toPickup
        ? MissionMockData.pickupEtaClock
        : MissionMockData.dropoffEtaClock;

    return Container(
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: const BorderRadius.vertical(top: Radius.circular(22)),
      ),
      child: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(18, 10, 18, 14),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Center(
                child: Container(
                  width: 38,
                  height: 4,
                  decoration: BoxDecoration(
                    color: colors.line,
                    borderRadius: BorderRadius.circular(2),
                  ),
                ),
              ),
              const SizedBox(height: 14),

              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          // La durée en gros : c'est la seule information qu'on
                          // lit en roulant, d'un coup d'œil.
                          '$minutes min',
                          style: TextStyle(
                            fontSize: 30,
                            fontWeight: FontWeight.w800,
                            color: colors.ink,
                          ),
                        ),
                        Text(
                          '${Format.km(distance, upper: false)} · arrivée $clock',
                          style: TextStyle(fontSize: 13, color: colors.subtle),
                        ),
                      ],
                    ),
                  ),
                  if (toPickup)
                    const UniverseBadge(universe: MissionMockData.universe)
                  else
                    Column(
                      crossAxisAlignment: CrossAxisAlignment.end,
                      children: [
                        Text(
                          'GAIN',
                          style: TextStyle(
                            fontSize: 10,
                            fontWeight: FontWeight.w800,
                            letterSpacing: 0.8,
                            color: colors.subtle,
                          ),
                        ),
                        Text(
                          '${Format.amount(MissionMockData.earning)} F',
                          style: TextStyle(
                            fontSize: 19,
                            fontWeight: FontWeight.w800,
                            color: colors.ink,
                          ),
                        ),
                      ],
                    ),
                ],
              ),
              const SizedBox(height: 12),
              Divider(height: 1, color: colors.line),
              const SizedBox(height: 12),

              if (toPickup) const _PickupRow() else const _DropoffBlock(),

              const SizedBox(height: 14),
              DriverPrimaryButton(
                label: 'Je suis arrivé',
                onPressed: () => ref.read(missionFlowProvider.notifier).advance(),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _PickupRow extends StatelessWidget {
  const _PickupRow();

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Row(
      children: [
        Container(
          width: 38,
          height: 38,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: AppTheme.amberSoft,
            borderRadius: BorderRadius.circular(10),
          ),
          child: const Text(
            'P',
            style: TextStyle(
              fontSize: 14,
              fontWeight: FontWeight.w800,
              color: AppTheme.amber,
            ),
          ),
        ),
        const SizedBox(width: 11),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                MissionMockData.pickupName,
                style: TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.w800,
                  color: colors.ink,
                ),
              ),
              Text(
                'Rue 12.045, Fidjrossè Plage',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(fontSize: 12.5, color: colors.subtle),
              ),
            ],
          ),
        ),
        const SizedBox(width: 8),
        _IconButton(
          icon: Icons.phone_outlined,
          onTap: () {},
        ),
      ],
    );
  }
}

class _DropoffBlock extends StatelessWidget {
  const _DropoffBlock();

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Container(
          width: double.infinity,
          padding: const EdgeInsets.all(13),
          decoration: BoxDecoration(
            color: colors.bg,
            borderRadius: BorderRadius.circular(AppTheme.radiusField),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'DESTINATION',
                style: TextStyle(
                  fontSize: 10,
                  fontWeight: FontWeight.w800,
                  letterSpacing: 0.8,
                  color: colors.subtle,
                ),
              ),
              const SizedBox(height: 4),
              Text(
                MissionMockData.dropoffAddress,
                style: TextStyle(
                  fontSize: 14.5,
                  fontWeight: FontWeight.w700,
                  color: colors.ink,
                ),
              ),
              const SizedBox(height: 9),
              Divider(height: 1, color: colors.line),
              const SizedBox(height: 9),
              Text(
                // ENTRE GUILLEMETS ET EN ITALIQUE : C'EST LA PAROLE DU CLIENT.
                //
                // Sans cette mise en forme, « Maison portail noir » se lirait
                // comme une donnée de la plateforme — et un livreur qui ne trouve
                // pas le portail s'en prendrait à HBA plutôt qu'à l'indication.
                '« ${MissionMockData.dropoffInstruction} »',
                style: TextStyle(
                  fontSize: 13,
                  fontStyle: FontStyle.italic,
                  height: 1.35,
                  color: colors.subtle,
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 12),
        Row(
          children: [
            Expanded(
              child: DriverSecondaryButton(label: 'Appeler', onPressed: () {}),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: DriverSecondaryButton(label: 'Message', onPressed: () {}),
            ),
            const SizedBox(width: 10),
            _IconButton(
              icon: Icons.priority_high,
              tint: AppTheme.danger,
              wash: AppTheme.dangerSoft,
              onTap: () {},
            ),
          ],
        ),
      ],
    );
  }
}

class _IconButton extends StatelessWidget {
  const _IconButton({
    required this.icon,
    required this.onTap,
    this.tint,
    this.wash,
  });

  final IconData icon;
  final VoidCallback onTap;
  final Color? tint;
  final Color? wash;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(AppTheme.radiusField),
      child: Container(
        width: AppTheme.primaryButtonHeight,
        height: AppTheme.primaryButtonHeight,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: wash ?? colors.surface,
          borderRadius: BorderRadius.circular(AppTheme.radiusField),
          border: Border.all(color: wash == null ? colors.line : Colors.transparent),
        ),
        child: Icon(icon, size: 20, color: tint ?? colors.ink),
      ),
    );
  }
}
