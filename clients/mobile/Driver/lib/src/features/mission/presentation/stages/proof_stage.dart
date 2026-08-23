import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/mock/mission_mock_data.dart';
import '../../../../core/mock/mission_state.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../shared/widgets/driver_widgets.dart';
import '../mission_flow_screen.dart';
import '_stage_header.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 14 — PREUVE DE LIVRAISON. « Mode OTP / photo / signature selon configuration. »
///
/// LES TROIS ONGLETS SONT UNE DÉMONSTRATION, PAS UN CHOIX DU LIVREUR.
///
/// Votre maquette l'écrit : « Mode imposé par Delivery Service selon la
/// commande. » Si le livreur pouvait choisir, il prendrait toujours la photo —
/// la moins contraignante des trois — et le code OTP ne protégerait plus rien :
/// c'est précisément la preuve qu'un tiers ne peut pas fabriquer.
///
/// Ils restent commutables ici pour que la démonstration montre les trois. En
/// service, `MissionMockData.proofMode` viendra de la commande et les onglets
/// disparaîtront.
///
/// PAVÉ NUMÉRIQUE MAISON, PAS LE CLAVIER SYSTÈME.
///
/// Le clavier natif occupe la moitié basse de l'écran et masquerait les quatre
/// cases pendant la saisie. Un pavé dessiné garde tout visible — et ses touches
/// font 56 px de haut, pour des doigts qui viennent de porter deux sacs.
/// ═════════════════════════════════════════════════════════════════════════════
class ProofStage extends ConsumerStatefulWidget {
  const ProofStage({super.key});

  @override
  ConsumerState<ProofStage> createState() => _ProofStageState();
}

class _ProofStageState extends ConsumerState<ProofStage> {
  String _code = '';
  bool _photoTaken = false;
  bool _signed = false;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final mode = ref.watch(proofModeProvider);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        child: Column(
          children: [
            StageHeader(
              title: 'Preuve de livraison',
              onBack: () => leaveMission(context, ref),
            ),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 20),
              child: _ModeTabs(
                current: mode,
                onChanged: (m) =>
                    ref.read(proofModeProvider.notifier).state = m,
              ),
            ),
            const SizedBox(height: 8),
            Text(
              'Mode imposé par Delivery Service selon la commande.',
              style: TextStyle(fontSize: 11.5, color: colors.subtle),
            ),
            const SizedBox(height: 12),

            Expanded(
              child: switch (mode) {
                ProofMode.otp => _OtpBody(
                    code: _code,
                    onDigit: (d) => setState(() {
                      if (_code.length < 4) _code += d;
                    }),
                    onErase: () => setState(() {
                      if (_code.isNotEmpty) {
                        _code = _code.substring(0, _code.length - 1);
                      }
                    }),
                  ),
                ProofMode.photo => _PhotoBody(taken: _photoTaken),
                ProofMode.signature => _SignatureBody(signed: _signed),
              },
            ),

            Padding(
              padding: const EdgeInsets.fromLTRB(20, 8, 20, 16),
              child: DriverPrimaryButton(
                label: switch (mode) {
                  ProofMode.otp => 'Valider',
                  ProofMode.photo => 'Prendre une photo',
                  ProofMode.signature => 'Valider la signature',
                },
                // SEUL L'OTP EXIGE UNE SAISIE COMPLÈTE.
                //
                // « Prendre une photo » et « Valider la signature » sont les
                // gestes eux-mêmes : les désactiver interdirait de commencer.
                onPressed: switch (mode) {
                  ProofMode.otp =>
                    _code.length == 4 ? _submit : null,
                  ProofMode.photo => () {
                      if (!_photoTaken) {
                        setState(() => _photoTaken = true);
                        return;
                      }
                      _submit();
                    },
                  ProofMode.signature => () {
                      if (!_signed) {
                        setState(() => _signed = true);
                        return;
                      }
                      _submit();
                    },
                },
              ),
            ),
          ],
        ),
      ),
    );
  }

  void _submit() => ref.read(missionFlowProvider.notifier).advance();
}

class _ModeTabs extends StatelessWidget {
  const _ModeTabs({required this.current, required this.onChanged});

  final ProofMode current;
  final ValueChanged<ProofMode> onChanged;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Row(
      children: [
        for (final m in ProofMode.values) ...[
          if (m != ProofMode.values.first) const SizedBox(width: 8),
          Expanded(
            child: InkWell(
              onTap: () => onChanged(m),
              borderRadius: BorderRadius.circular(10),
              child: Container(
                height: 42,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: m == current ? AppTheme.charcoal : colors.surface,
                  borderRadius: BorderRadius.circular(10),
                  border: Border.all(
                    color: m == current ? AppTheme.charcoal : colors.line,
                  ),
                ),
                child: Text(
                  m.label,
                  style: TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w700,
                    color: m == current ? Colors.white : colors.ink,
                  ),
                ),
              ),
            ),
          ),
        ],
      ],
    );
  }
}

class _OtpBody extends StatelessWidget {
  const _OtpBody({
    required this.code,
    required this.onDigit,
    required this.onErase,
  });

  final String code;
  final ValueChanged<String> onDigit;
  final VoidCallback onErase;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return ListView(
      padding: const EdgeInsets.fromLTRB(20, 8, 20, 8),
      children: [
        Text(
          'Entrez le code reçu par le client',
          textAlign: TextAlign.center,
          style: TextStyle(
            fontSize: 19,
            fontWeight: FontWeight.w800,
            color: colors.ink,
          ),
        ),
        const SizedBox(height: 4),
        Text(
          'Code à 4 chiffres envoyé par SMS.',
          textAlign: TextAlign.center,
          style: TextStyle(fontSize: 12.5, color: colors.subtle),
        ),
        const SizedBox(height: 18),

        Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            for (var i = 0; i < 4; i++)
              Container(
                width: 62,
                height: 62,
                margin: const EdgeInsets.symmetric(horizontal: 5),
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: colors.surface,
                  borderRadius: BorderRadius.circular(14),
                  border: Border.all(
                    // La case ACTIVE est cerclée de vert : elle dit où le
                    // prochain chiffre ira, sans curseur clignotant.
                    color: i == code.length ? AppTheme.brandGreen : colors.line,
                    width: i == code.length ? 1.8 : 1,
                  ),
                ),
                child: Text(
                  i < code.length ? code[i] : '',
                  style: TextStyle(
                    fontSize: 24,
                    fontWeight: FontWeight.w800,
                    color: colors.ink,
                  ),
                ),
              ),
          ],
        ),
        const SizedBox(height: 20),

        for (final row in const [
          ['1', '2', '3'],
          ['4', '5', '6'],
          ['7', '8', '9'],
          ['', '0', '⌫'],
        ])
          Padding(
            padding: const EdgeInsets.only(bottom: 10),
            child: Row(
              children: [
                for (final key in row) ...[
                  if (key != row.first) const SizedBox(width: 10),
                  Expanded(
                    child: _Key(
                      label: key,
                      onTap: key.isEmpty
                          ? null
                          : key == '⌫'
                              ? onErase
                              : () => onDigit(key),
                    ),
                  ),
                ],
              ],
            ),
          ),
      ],
    );
  }
}

class _Key extends StatelessWidget {
  const _Key({required this.label, required this.onTap});

  final String label;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    if (label.isEmpty) return const SizedBox(height: 56);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(12),
      child: Container(
        height: 56,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: colors.bg,
          borderRadius: BorderRadius.circular(12),
        ),
        child: Text(
          label == '⌫' ? '←' : label,
          style: TextStyle(
            fontSize: 20,
            fontWeight: FontWeight.w700,
            color: colors.ink,
          ),
        ),
      ),
    );
  }
}

class _PhotoBody extends StatelessWidget {
  const _PhotoBody({required this.taken});

  final bool taken;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(horizontal: 20),
        child: Container(
          width: double.infinity,
          decoration: BoxDecoration(
            color: AppTheme.charcoal,
            borderRadius: BorderRadius.circular(AppTheme.radiusCard),
          ),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              // PAS D'APERÇU CAMÉRA : IL DEMANDERAIT UNE PERMISSION SYSTÈME.
              //
              // Une boîte de dialogue d'autorisation en pleine démonstration,
              // pour une photo qui ne serait envoyée nulle part, coûte plus
              // qu'elle ne montre.
              Container(
                width: 56,
                height: 56,
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(10),
                  border: Border.all(
                    color: Colors.white.withValues(alpha: taken ? 0.6 : 0.28),
                  ),
                ),
                child: taken
                    ? const Icon(Icons.check, color: Colors.white, size: 26)
                    : null,
              ),
              const SizedBox(height: 14),
              Text(
                taken ? 'Photo prise' : 'Cadrez le colis devant la porte',
                style: TextStyle(
                  fontSize: 13,
                  color: Colors.white.withValues(alpha: 0.75),
                ),
              ),
            ],
          ),
        ),
      );
}

class _SignatureBody extends StatelessWidget {
  const _SignatureBody({required this.signed});

  final bool signed;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 20),
      child: Container(
        width: double.infinity,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(AppTheme.radiusCard),
          border: Border.all(color: colors.line),
        ),
        child: Text(
          // ZONE INERTE : LA CAPTURE DU TRACÉ N'EST PAS IMPLÉMENTÉE.
          //
          // Un `CustomPainter` sur `GestureDetector` la ferait, mais une
          // signature qu'on ne stocke ni ne transmet n'apporte rien à la
          // démonstration — et laisserait croire que la preuve est en place.
          signed ? 'Signature enregistrée' : 'Faites signer le client ici',
          style: TextStyle(fontSize: 13.5, color: colors.subtle),
        ),
      ),
    );
  }
}
