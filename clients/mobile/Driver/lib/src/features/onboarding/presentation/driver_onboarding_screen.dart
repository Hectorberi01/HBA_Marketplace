import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/mock/driver_mock_data.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/driver_widgets.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// 02 — INSCRIPTION. « Parcours en 7 étapes, une seule décision par écran. »
///
/// LA LISTE « ÉTAPES RESTANTES » EST LE CŒUR DE L'ÉCRAN, PAS SA DÉCORATION.
///
/// Sept étapes, c'est long. La seule question de celui qui les traverse est
/// « combien encore », et une barre de progression seule n'y répond pas : elle
/// dit une proportion, pas ce qui reste à faire. La liste nomme les sept, coche
/// les faites, désigne celle en cours et grise les suivantes.
///
/// Sans elle, l'abandon se décide à la troisième — parce que rien ne dit s'il en
/// reste deux ou douze.
///
/// SEULE L'ÉTAPE 3 EST DESSINÉE PAR LA MAQUETTE.
///
/// Les six autres sont ici des formulaires nommés, sans champs. Inventer la
/// saisie du permis, du moyen de transport ou de la photo produirait six écrans
/// crédibles et faux, qu'il faudrait défaire à réception des vraies maquettes.
/// La structure, la progression et la navigation, elles, sont complètes.
/// ═════════════════════════════════════════════════════════════════════════════
class DriverOnboardingScreen extends ConsumerStatefulWidget {
  const DriverOnboardingScreen({super.key});

  @override
  ConsumerState<DriverOnboardingScreen> createState() =>
      _DriverOnboardingScreenState();
}

class _DriverOnboardingScreenState extends ConsumerState<DriverOnboardingScreen> {
  /// L'écran s'ouvre sur l'adresse — l'étape que la maquette dessine.
  ///
  /// VALEUR DE DÉMONSTRATION, À REMETTRE À 0.
  ///
  /// Un vrai parcours commence à la première étape. Celle-ci permet de comparer
  /// l'écran à la capture sans avoir à toucher deux fois « Continuer ».
  int _step = 2;

  String _district = 'Fidjrossè';
  String _street = 'Carrefour Aïbatin';
  String _city = '';

  List<OnboardingStep> get _steps => DriverMockData.onboardingSteps;

  void _back() {
    if (_step == 0) {
      context.pop();
      return;
    }
    setState(() => _step--);
  }

  void _next() {
    if (_step < _steps.length - 1) {
      setState(() => _step++);
      return;
    }
    // La dernière étape est « Vérification » : elle mène à l'écran d'attente.
    context.go('/verification');
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Scaffold(
      backgroundColor: colors.surface,
      body: SafeArea(
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(14, 6, 22, 0),
              child: Row(
                children: [
                  _BackChip(onTap: _back),
                  const Spacer(),
                  Text(
                    'Étape ${_step + 1} sur ${_steps.length}',
                    style: TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.w600,
                      color: colors.subtle,
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 12),

            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 22),
              child: ClipRRect(
                borderRadius: BorderRadius.circular(3),
                child: LinearProgressIndicator(
                  value: (_step + 1) / _steps.length,
                  minHeight: 4,
                  backgroundColor: colors.line,
                  valueColor: const AlwaysStoppedAnimation(AppTheme.brandGreen),
                ),
              ),
            ),
            const SizedBox(height: 20),

            Expanded(
              child: ListView(
                padding: const EdgeInsets.fromLTRB(22, 0, 22, 16),
                children: [
                  Text(
                    _titleFor(_step),
                    style: TextStyle(
                      fontSize: 25,
                      fontWeight: FontWeight.w800,
                      color: colors.ink,
                    ),
                  ),
                  const SizedBox(height: 5),
                  Text(
                    _subtitleFor(_step),
                    style: TextStyle(fontSize: 14, height: 1.4, color: colors.subtle),
                  ),
                  const SizedBox(height: 18),

                  if (_step == 2) _AddressForm(
                    district: _district,
                    street: _street,
                    city: _city,
                    onDistrict: (v) => _district = v,
                    onStreet: (v) => _street = v,
                    onCity: (v) => _city = v,
                  ) else _NotDrawnYet(label: _steps[_step].label),

                  const SizedBox(height: 18),
                  _StepChecklist(steps: _steps, current: _step),
                ],
              ),
            ),

            Padding(
              padding: const EdgeInsets.fromLTRB(22, 4, 22, 16),
              child: DriverPrimaryButton(
                label: _step == _steps.length - 1 ? 'Envoyer le dossier' : 'Continuer',
                onPressed: _next,
              ),
            ),
          ],
        ),
      ),
    );
  }

  /// TITRE ET LIBELLÉ D'ÉTAPE SONT DEUX CHOSES DIFFÉRENTES.
  ///
  /// La liste dit « Adresse » ; le titre dit « Votre adresse ». Le premier
  /// nomme une case à cocher, le second s'adresse à quelqu'un. Réutiliser le
  /// libellé de la liste comme titre aurait donné un écran qui parle comme un
  /// formulaire administratif.
  String _titleFor(int step) => switch (step) {
        0 => 'Vos informations',
        1 => 'Votre téléphone',
        2 => 'Votre adresse',
        3 => 'Votre moyen de transport',
        4 => 'Vos documents',
        5 => 'Votre photo',
        _ => 'Vérification',
      };

  String _subtitleFor(int step) => switch (step) {
        2 => 'Nous l\'utilisons pour vous proposer les missions les plus proches.',
        // SOUS-TITRES DÉDUITS, SAUF CELUI DE L'ADRESSE.
        //
        // Seul le troisième est écrit par la maquette. Les autres disent à quoi
        // sert la donnée demandée — c'est la règle que l'étape dessinée établit,
        // et la seule qu'il soit raisonnable de généraliser.
        0 => 'Votre nom tel qu\'il figure sur vos pièces d\'identité.',
        1 => 'Il servira à vous joindre pendant les livraisons.',
        3 => 'Il détermine les missions que vous pouvez accepter.',
        4 => 'Permis, pièce d\'identité, assurance et carte grise.',
        5 => 'Elle est montrée au client à la remise du colis.',
        _ => 'Nous vérifions votre dossier avant votre première mission.',
      };
}

class _AddressForm extends StatelessWidget {
  const _AddressForm({
    required this.district,
    required this.street,
    required this.city,
    required this.onDistrict,
    required this.onStreet,
    required this.onCity,
  });

  final String district;
  final String street;
  final String city;
  final ValueChanged<String> onDistrict;
  final ValueChanged<String> onStreet;
  final ValueChanged<String> onCity;

  @override
  Widget build(BuildContext context) => Column(
        children: [
          // LE LIBELLÉ EST DANS LE CHAMP, PAS AU-DESSUS.
          //
          // « Quartier · Fidjrossè » : la maquette fond l'intitulé et l'exemple
          // en une seule ligne. Trois libellés flottants au-dessus de trois
          // champs auraient doublé la hauteur du bloc et repoussé la liste des
          // étapes hors de l'écran — or c'est elle qui retient les gens.
          DriverField(
            hint: 'Quartier · Fidjrossè',
            initial: district,
            onChanged: onDistrict,
          ),
          const SizedBox(height: 12),
          DriverField(
            hint: 'Rue / repère · Carrefour Aïbatin',
            initial: street,
            onChanged: onStreet,
          ),
          const SizedBox(height: 12),
          DriverField(
            hint: 'Ville · Cotonou',
            initial: city,
            onChanged: onCity,
          ),
        ],
      );
}

class _NotDrawnYet extends StatelessWidget {
  const _NotDrawnYet({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return DriverCard(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 22),
      child: Column(
        children: [
          Icon(Icons.draw_outlined, size: 26, color: colors.line),
          const SizedBox(height: 10),
          Text(
            '« $label » n\'est pas encore maquetté.',
            textAlign: TextAlign.center,
            style: TextStyle(
              fontSize: 13.5,
              fontWeight: FontWeight.w700,
              color: colors.ink,
            ),
          ),
          const SizedBox(height: 4),
          Text(
            'La progression et la navigation fonctionnent ; les champs viendront '
            'avec la maquette.',
            textAlign: TextAlign.center,
            style: TextStyle(fontSize: 12.5, height: 1.4, color: colors.subtle),
          ),
        ],
      ),
    );
  }
}

/// La liste des sept étapes : faites, en cours, à venir.
class _StepChecklist extends StatelessWidget {
  const _StepChecklist({required this.steps, required this.current});

  final List<OnboardingStep> steps;
  final int current;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return DriverCard(
      padding: const EdgeInsets.fromLTRB(14, 13, 14, 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'ÉTAPES RESTANTES',
            style: TextStyle(
              fontSize: 10.5,
              fontWeight: FontWeight.w800,
              letterSpacing: 0.9,
              color: colors.subtle,
            ),
          ),
          const SizedBox(height: 12),
          for (var i = 0; i < steps.length; i++) ...[
            if (i > 0) const SizedBox(height: 12),
            Row(
              children: [
                _StepDot(done: i < current, active: i == current),
                const SizedBox(width: 10),
                Text(
                  steps[i].label,
                  style: TextStyle(
                    fontSize: 14,
                    // L'étape en cours est la seule en gras : c'est là qu'on est.
                    fontWeight: i == current ? FontWeight.w700 : FontWeight.w500,
                    // Les étapes à venir s'effacent sans disparaître — on doit
                    // pouvoir les lire pour savoir ce qui attend.
                    color: i <= current ? colors.ink : colors.subtle,
                  ),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}

class _StepDot extends StatelessWidget {
  const _StepDot({required this.done, required this.active});

  final bool done;
  final bool active;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    if (done) {
      return Container(
        width: 20,
        height: 20,
        alignment: Alignment.center,
        decoration: const BoxDecoration(
          color: AppTheme.brandGreen,
          shape: BoxShape.circle,
        ),
        child: const Icon(Icons.check, size: 13, color: Colors.white),
      );
    }

    return Container(
      width: 20,
      height: 20,
      decoration: BoxDecoration(
        // Ambre pour l'étape en cours : la couleur de l'attention, la même que
        // celle du document qui expire. C'est ici que quelque chose est attendu.
        color: active ? AppTheme.amber : colors.line,
        shape: BoxShape.circle,
      ),
    );
  }
}

class _BackChip extends StatelessWidget {
  const _BackChip({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(12),
      child: Container(
        width: 40,
        height: 40,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: colors.line),
        ),
        child: Icon(Icons.chevron_left, size: 22, color: colors.ink),
      ),
    );
  }
}
