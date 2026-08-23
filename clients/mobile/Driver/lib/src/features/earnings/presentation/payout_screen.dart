import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/mock/earnings_mock_data.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/driver_widgets.dart';
import '../../mission/presentation/stages/_stage_header.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 20 — RETRAIT. « Montant, méthode, confirmation explicite. »
///
/// CET ÉCRAN NE DÉCLENCHE AUCUN MOUVEMENT D'ARGENT, ET C'EST DÉFINITIF ICI.
///
/// « Confirmer le retrait » n'appelle rien, n'écrit rien et ne modifie pas le
/// solde. Ce n'est pas une limite de la simulation à lever plus tard : brancher
/// un vrai virement Mobile Money demande une intégration FedaPay, une
/// vérification d'identité et une trace comptable — trois choses qui ne
/// s'improvisent pas dans une maquette.
///
/// « TOUT » EST CALCULÉ, JAMAIS ÉCRIT.
///
/// Figer 42 500 dans la puce la rendrait fausse dès la course suivante — et
/// c'est le montant que le livreur touchera le plus souvent.
///
/// LE DÉLAI EST ANNONCÉ SOUS LE BOUTON, PAS APRÈS.
///
/// « Traitement sous 24 h ouvrées » doit être lu AVANT de confirmer. Le
/// découvrir dans un écran de succès, c'est apprendre trop tard qu'on n'aura pas
/// l'argent ce soir.
/// ═════════════════════════════════════════════════════════════════════════════
class PayoutScreen extends ConsumerStatefulWidget {
  const PayoutScreen({super.key});

  @override
  ConsumerState<PayoutScreen> createState() => _PayoutScreenState();
}

class _PayoutScreenState extends ConsumerState<PayoutScreen> {
  int _amount = EarningsMockData.defaultAmount;
  String _method = EarningsMockData.payoutMethods.first.id;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        bottom: false,
        child: ListView(
          children: [
            StageHeader(
              title: 'Retirer mes gains',
              onBack: () => context.pop(),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 8, 20, 24),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  DriverCard(
                    padding: const EdgeInsets.fromLTRB(16, 16, 16, 16),
                    child: Column(
                      children: [
                        Text(
                          'MONTANT À RETIRER',
                          style: TextStyle(
                            fontSize: 10.5,
                            fontWeight: FontWeight.w800,
                            letterSpacing: 0.9,
                            color: colors.subtle,
                          ),
                        ),
                        const SizedBox(height: 8),
                        Text(
                          '${Format.amount(_amount)} F',
                          style: TextStyle(
                            fontSize: 34,
                            fontWeight: FontWeight.w800,
                            color: colors.ink,
                          ),
                        ),
                        const SizedBox(height: 4),
                        Text(
                          'Disponible : '
                          '${Format.cfaAmount(EarningsMockData.available)}',
                          style: TextStyle(fontSize: 12.5, color: colors.subtle),
                        ),
                        const SizedBox(height: 16),
                        Row(
                          children: [
                            for (final a in EarningsMockData.quickAmounts) ...[
                              Expanded(
                                child: _AmountChip(
                                  label: Format.amount(a),
                                  selected: _amount == a,
                                  onTap: () => setState(() => _amount = a),
                                ),
                              ),
                              const SizedBox(width: 10),
                            ],
                            Expanded(
                              child: _AmountChip(
                                label: 'Tout',
                                selected:
                                    _amount == EarningsMockData.available,
                                onTap: () => setState(
                                  () => _amount = EarningsMockData.available,
                                ),
                              ),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 18),

                  Text(
                    'MÉTHODE',
                    style: TextStyle(
                      fontSize: 10.5,
                      fontWeight: FontWeight.w800,
                      letterSpacing: 0.9,
                      color: colors.subtle,
                    ),
                  ),
                  const SizedBox(height: 10),

                  for (final m in EarningsMockData.payoutMethods) ...[
                    _MethodRow(
                      method: m,
                      selected: m.id == _method,
                      onTap: () => setState(() => _method = m.id),
                    ),
                    const SizedBox(height: 10),
                  ],
                  const SizedBox(height: 10),

                  DriverPrimaryButton(
                    label: 'Confirmer le retrait',
                    // DÉSACTIVÉ SI LE MONTANT DÉPASSE LE DISPONIBLE.
                    //
                    // Impossible avec les puces actuelles, mais la saisie libre
                    // viendra — et c'est le genre de garde qu'on n'ajoute jamais
                    // après coup.
                    onPressed: _amount > 0 &&
                            _amount <= EarningsMockData.available
                        ? () => _confirm(context)
                        : null,
                  ),
                  const SizedBox(height: 10),
                  Center(
                    child: Text(
                      EarningsMockData.payoutDelay,
                      style: TextStyle(fontSize: 12, color: colors.subtle),
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  void _confirm(BuildContext context) {
    final messenger = ScaffoldMessenger.of(context);
    context.pop();
    messenger.showSnackBar(
      SnackBar(
        content: Text(
          'Retrait de ${Format.amount(_amount)} F demandé (simulation).',
        ),
        behavior: SnackBarBehavior.floating,
      ),
    );
  }
}

class _AmountChip extends StatelessWidget {
  const _AmountChip({
    required this.label,
    required this.selected,
    required this.onTap,
  });

  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(10),
      child: Container(
        height: 44,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: selected ? AppTheme.charcoal : colors.bg,
          borderRadius: BorderRadius.circular(10),
        ),
        child: Text(
          label,
          style: TextStyle(
            fontSize: 14,
            fontWeight: FontWeight.w700,
            color: selected ? Colors.white : colors.ink,
          ),
        ),
      ),
    );
  }
}

class _MethodRow extends StatelessWidget {
  const _MethodRow({
    required this.method,
    required this.selected,
    required this.onTap,
  });

  final PayoutMethod method;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(AppTheme.radiusCard),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 13),
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(AppTheme.radiusCard),
          border: Border.all(
            color: selected ? AppTheme.brandGreen : colors.line,
            width: selected ? 1.6 : 1,
          ),
        ),
        child: Row(
          children: [
            Container(
              width: 40,
              height: 40,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                // COULEURS D'OPÉRATEUR APPROCHÉES, PAS LES LOGOS OFFICIELS.
                //
                // MTN est jaune, Moov bleu. Intégrer les vrais logos suppose un
                // droit d'usage de marque : à traiter avant publication.
                color: method.id == 'mtn'
                    ? const Color(0xFFFDF1E3)
                    : const Color(0xFFEAF1FE),
                borderRadius: BorderRadius.circular(10),
              ),
              child: Text(
                method.initials,
                style: TextStyle(
                  fontSize: 9.5,
                  fontWeight: FontWeight.w800,
                  color: method.id == 'mtn'
                      ? AppTheme.amber
                      : AppTheme.info,
                ),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    method.name,
                    style: TextStyle(
                      fontSize: 15,
                      fontWeight: FontWeight.w800,
                      color: colors.ink,
                    ),
                  ),
                  Text(
                    method.maskedNumber,
                    style: TextStyle(
                      fontSize: 12.5,
                      letterSpacing: 0.5,
                      color: colors.subtle,
                    ),
                  ),
                ],
              ),
            ),
            Container(
              width: 22,
              height: 22,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: selected ? AppTheme.brandGreen : Colors.transparent,
                shape: BoxShape.circle,
                border: Border.all(
                  color: selected ? AppTheme.brandGreen : colors.line,
                  width: 1.6,
                ),
              ),
              child: selected
                  ? const Icon(Icons.check, size: 13, color: Colors.white)
                  : null,
            ),
          ],
        ),
      ),
    );
  }
}
