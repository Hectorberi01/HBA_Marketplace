import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/app_notify.dart';
import '../consent_controller.dart';
import '../legal_content.dart';
import 'legal_document.dart';

/// Écran de consentement — BLOQUANT.
///
/// Tant que l'utilisateur n'a pas accepté la version courante, il ne voit rien
/// d'autre. Ce n'est pas de la rigidité : le Code du numérique fait dépendre la
/// validité du contrat conclu en ligne de l'acceptation des conditions, et un
/// accord que l'on peut contourner d'un revers de pouce n'est pas un accord.
///
/// Trois choix de conception, tous discutables et tous assumés :
///
///  • La case à cocher ne s'active QUE si le texte a été déroulé jusqu'au bout.
///    On ne peut évidemment pas forcer quelqu'un à lire — mais on peut refuser de
///    prétendre qu'il a lu alors qu'il n'a pas même fait défiler la page.
///
///  • Le refus est OFFERT, et il déconnecte. Un écran sans issue est un piège ;
///    et un consentement dont on ne peut pas sortir n'est pas libre, donc pas
///    valable.
///
///  • Le retour Android est neutralisé. Le contourner reviendrait à entrer dans
///    l'app sans avoir accepté — exactement ce que cet écran empêche.
class ConsentScreen extends ConsumerStatefulWidget {
  const ConsentScreen({super.key});

  @override
  ConsumerState<ConsentScreen> createState() => _ConsentScreenState();
}

class _ConsentScreenState extends ConsumerState<ConsentScreen> {
  final _scroll = ScrollController();

  bool _reachedEnd = false;
  bool _agreed = false;
  bool _busy = false;

  /// Onglet courant : 0 = conditions, 1 = confidentialité.
  int _tab = 0;

  /// Les DEUX documents doivent avoir été déroulés. Accepter les conditions sans
  /// avoir ouvert la politique de confidentialité serait un consentement partiel.
  final _seen = <int>{};

  @override
  void initState() {
    super.initState();
    _scroll.addListener(_onScroll);
    // Un document plus court que l'écran n'émettra jamais d'événement de défilement :
    // sans cette vérification, la case resterait grisée pour toujours.
    WidgetsBinding.instance.addPostFrameCallback((_) => _onScroll());
  }

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  void _onScroll() {
    if (!_scroll.hasClients) return;
    final atEnd = _scroll.position.maxScrollExtent <= 0 ||
        _scroll.position.pixels >= _scroll.position.maxScrollExtent - 24;
    if (atEnd && !_seen.contains(_tab)) {
      setState(() {
        _seen.add(_tab);
        _reachedEnd = _seen.length == 2;
      });
    }
  }

  void _switchTo(int tab) {
    setState(() => _tab = tab);
    // Nouveau document, nouveau défilement : on repart du haut et on revérifie.
    _scroll.jumpTo(0);
    WidgetsBinding.instance.addPostFrameCallback((_) => _onScroll());
  }

  Future<void> _accept() async {
    setState(() => _busy = true);
    try {
      await ref.read(consentControllerProvider.notifier).accept();
      // Le routeur bascule seul dès que l'état passe à « accordé ».
    } catch (e) {
      if (mounted) {
        AppNotify.error(context, "Votre accord n'a pas pu être enregistré : $e");
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _decline() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Refuser les conditions ?'),
        content: const Text(
          "Sans votre accord, l'application ne peut pas être utilisée : vous serez déconnecté. "
          "Votre compte et vos données ne sont pas supprimés.",
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('Revenir'),
          ),
          FilledButton(
            style: FilledButton.styleFrom(backgroundColor: AppTheme.danger),
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('Refuser et quitter'),
          ),
        ],
      ),
    );

    if (ok == true) {
      await ref.read(consentControllerProvider.notifier).decline();
    }
  }

  @override
  Widget build(BuildContext context) {
    final sections = _tab == 0 ? Legal.terms : Legal.privacy;

    return PopScope(
      // Le retour Android ne doit pas ouvrir l'app sans acceptation.
      canPop: false,
      child: Scaffold(
        backgroundColor: AppTheme.bg,
        appBar: AppBar(
          automaticallyImplyLeading: false,
          title: const Text('Avant de continuer'),
        ),
        body: Column(
          children: [
            Container(
              color: AppTheme.surface,
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    "Nous avons besoin de votre accord sur nos conditions générales et sur notre "
                    "politique de confidentialité. Prenez le temps de les lire : elles disent ce à "
                    "quoi vous vous engagez, et ce à quoi nous nous engageons.",
                    style: TextStyle(fontSize: 13, color: AppTheme.subtle, height: 1.5),
                  ),
                  const SizedBox(height: 12),
                  Row(
                    children: [
                      _Tab(
                        label: 'Conditions',
                        selected: _tab == 0,
                        done: _seen.contains(0),
                        onTap: () => _switchTo(0),
                      ),
                      const SizedBox(width: 8),
                      _Tab(
                        label: 'Confidentialité',
                        selected: _tab == 1,
                        done: _seen.contains(1),
                        onTap: () => _switchTo(1),
                      ),
                    ],
                  ),
                  const SizedBox(height: 12),
                ],
              ),
            ),

            Expanded(
              child: LegalDocument(
                key: ValueKey(_tab),
                controller: _scroll,
                sections: sections,
                padding: const EdgeInsets.only(bottom: 24),
              ),
            ),

            _Footer(
              canAgree: _reachedEnd,
              agreed: _agreed,
              busy: _busy,
              onAgreedChanged: (v) => setState(() => _agreed = v),
              onAccept: _accept,
              onDecline: _decline,
            ),
          ],
        ),
      ),
    );
  }
}

class _Tab extends StatelessWidget {
  const _Tab({
    required this.label,
    required this.selected,
    required this.done,
    required this.onTap,
  });

  final String label;
  final bool selected;
  final bool done;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Expanded(
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(10),
        child: Container(
          padding: const EdgeInsets.symmetric(vertical: 10),
          decoration: BoxDecoration(
            color: selected ? AppTheme.softGreen : Colors.transparent,
            borderRadius: BorderRadius.circular(10),
            border: Border.all(color: selected ? AppTheme.brandGreen : AppTheme.line),
          ),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              // La coche dit ce qui a déjà été parcouru : sans elle, l'utilisateur
              // ne comprend pas pourquoi la case reste grisée.
              if (done) ...[
                const Icon(Icons.check_circle, size: 15, color: AppTheme.brandGreen),
                const SizedBox(width: 6),
              ],
              Text(
                label,
                style: TextStyle(
                  fontSize: 13,
                  fontWeight: FontWeight.w700,
                  color: selected ? AppTheme.brandGreen : AppTheme.subtle,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Footer extends StatelessWidget {
  const _Footer({
    required this.canAgree,
    required this.agreed,
    required this.busy,
    required this.onAgreedChanged,
    required this.onAccept,
    required this.onDecline,
  });

  final bool canAgree;
  final bool agreed;
  final bool busy;
  final ValueChanged<bool> onAgreedChanged;
  final VoidCallback onAccept;
  final VoidCallback onDecline;

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        color: AppTheme.surface,
        border: Border(top: BorderSide(color: AppTheme.line)),
      ),
      child: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (!canAgree)
                const Padding(
                  padding: EdgeInsets.only(bottom: 8),
                  child: Text(
                    'Faites défiler les deux documents jusqu’au bout pour pouvoir les accepter.',
                    textAlign: TextAlign.center,
                    style: TextStyle(fontSize: 12, color: AppTheme.promoOrange),
                  ),
                ),

              InkWell(
                onTap: canAgree ? () => onAgreedChanged(!agreed) : null,
                borderRadius: BorderRadius.circular(8),
                child: Row(
                  children: [
                    Checkbox(
                      value: agreed,
                      onChanged: canAgree ? (v) => onAgreedChanged(v ?? false) : null,
                    ),
                    Expanded(
                      child: Text(
                        "J’ai lu et j’accepte les conditions générales et la politique de confidentialité.",
                        style: TextStyle(fontSize: 13, color: AppTheme.ink, height: 1.4),
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 8),

              FilledButton(
                onPressed: (agreed && !busy) ? onAccept : null,
                child: busy
                    ? const SizedBox(
                        width: 22,
                        height: 22,
                        child: CircularProgressIndicator(strokeWidth: 2.4, color: Colors.white),
                      )
                    : const Text('Accepter et continuer'),
              ),
              TextButton(
                onPressed: busy ? null : onDecline,
                style: TextButton.styleFrom(foregroundColor: AppTheme.subtle),
                child: const Text('Refuser'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
