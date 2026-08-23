import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/mock/driver_mock_data.dart';
import '../../../core/mock/driver_state.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/driver_widgets.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 01 — CONNEXION. « Deux champs, un seul CTA, aide accessible. »
///
/// UN SEUL BOUTON VERT SUR L'ÉCRAN.
///
/// « Devenir livreur HBA » est en bas, à contour fin, séparé par un grand vide.
/// Deux boutons pleins auraient mis sur le même plan « j'ai un compte » et « je
/// n'en ai pas » — deux intentions qui n'ont ni la même fréquence ni la même
/// urgence. Celui qui s'inscrit le fait une fois ; celui qui se connecte le fait
/// tous les matins.
///
/// AUCUN MOT DE PASSE N'EST VÉRIFIÉ.
///
/// Il n'y a ni service, ni jeton, ni règle de complexité. N'importe quelle
/// saisie ouvre la session simulée. Le jour où l'authentification arrive, c'est
/// `_submit` qui change, et rien d'autre.
/// ═════════════════════════════════════════════════════════════════════════════
class DriverLoginScreen extends ConsumerStatefulWidget {
  const DriverLoginScreen({super.key});

  @override
  ConsumerState<DriverLoginScreen> createState() => _DriverLoginScreenState();
}

class _DriverLoginScreenState extends ConsumerState<DriverLoginScreen> {
  String _identifier = DriverMockData.phone;
  String _password = '';
  bool _reveal = false;

  /// LE BOUTON RESTE ACTIF MÊME SANS SAISIE.
  ///
  /// Le griser tant que les deux champs ne sont pas remplis paraît propre, mais
  /// laisse sans explication : l'utilisateur touche un bouton mort et ne sait
  /// pas ce qui manque. Il vaut mieux laisser tenter et dire ce qui cloche —
  /// message qui reste à écrire, faute de règles de validation.
  void _submit() {
    ref.read(driverSignedInProvider.notifier).state = true;
    context.go('/home');
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Scaffold(
      backgroundColor: colors.surface,
      body: SafeArea(
        child: Column(
          children: [
            Expanded(
              child: ListView(
                padding: const EdgeInsets.fromLTRB(22, 24, 22, 8),
                children: [
                  const _BrandMark(),
                  const SizedBox(height: 26),

                  Text(
                    'Livrez avec HBA',
                    style: TextStyle(
                      fontSize: 28,
                      fontWeight: FontWeight.w800,
                      color: colors.ink,
                    ),
                  ),
                  const SizedBox(height: 6),
                  Text(
                    'Recevez des missions, livrez et suivez vos revenus.',
                    style: TextStyle(fontSize: 14.5, height: 1.4, color: colors.subtle),
                  ),
                  const SizedBox(height: 26),

                  DriverField(
                    // Un seul champ pour les deux : au Bénin, un livreur a
                    // toujours un numéro et pas toujours une adresse. Imposer un
                    // choix entre deux onglets ajouterait un geste avant même la
                    // première frappe.
                    label: 'Téléphone ou e-mail',
                    hint: DriverMockData.phone,
                    initial: _identifier,
                    onChanged: (v) => _identifier = v,
                  ),
                  const SizedBox(height: 16),

                  DriverField(
                    label: 'Mot de passe',
                    hint: '••••••••',
                    obscure: !_reveal,
                    onChanged: (v) => _password = v,
                    trailing: TextButton(
                      onPressed: () => setState(() => _reveal = !_reveal),
                      style: TextButton.styleFrom(
                        foregroundColor: colors.subtle,
                        textStyle: const TextStyle(
                          fontSize: 13.5,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      // « AFFICHER » / « MASQUER », PAS UNE ICÔNE D'ŒIL.
                      //
                      // La maquette écrit le mot. Un œil barré est ambigu — il
                      // peut se lire « c'est masqué » comme « touchez pour
                      // masquer » — et le doute coûte un essai à l'aveugle sur un
                      // écran où l'on est déjà en train de se tromper.
                      child: Text(_reveal ? 'Masquer' : 'Afficher'),
                    ),
                  ),
                  const SizedBox(height: 22),

                  DriverPrimaryButton(label: 'Se connecter', onPressed: _submit),
                  const SizedBox(height: 14),

                  Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      _LinkButton(label: 'Mot de passe oublié ?', onTap: () {}),
                      const SizedBox(width: 14),
                      // « Besoin d'aide ? » en vert : c'est le seul autre élément
                      // coloré de l'écran, parce qu'un livreur bloqué à la
                      // connexion ne peut plus rien faire du tout.
                      _LinkButton(
                        label: 'Besoin d\'aide ?',
                        color: AppTheme.brandGreen,
                        onTap: () {},
                      ),
                    ],
                  ),
                ],
              ),
            ),

            Padding(
              padding: const EdgeInsets.fromLTRB(22, 8, 22, 18),
              child: DriverSecondaryButton(
                label: 'Devenir livreur HBA',
                onPressed: () => context.push('/register'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _BrandMark extends StatelessWidget {
  const _BrandMark();

  @override
  Widget build(BuildContext context) => Align(
        alignment: Alignment.centerLeft,
        child: Container(
          width: 58,
          height: 58,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: AppTheme.brandGreen,
            borderRadius: BorderRadius.circular(18),
          ),
          child: const Text(
            'H',
            style: TextStyle(
              fontSize: 26,
              fontWeight: FontWeight.w800,
              color: Colors.white,
            ),
          ),
        ),
      );
}

class _LinkButton extends StatelessWidget {
  const _LinkButton({required this.label, required this.onTap, this.color});

  final String label;
  final VoidCallback onTap;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return TextButton(
      onPressed: onTap,
      style: TextButton.styleFrom(
        // 48 px de haut : ces liens sont petits à l'œil, jamais au doigt.
        minimumSize: const Size(0, AppTheme.minTapTarget),
        foregroundColor: color ?? colors.subtle,
        textStyle: const TextStyle(fontSize: 13.5, fontWeight: FontWeight.w600),
      ),
      child: Text(label),
    );
  }
}
