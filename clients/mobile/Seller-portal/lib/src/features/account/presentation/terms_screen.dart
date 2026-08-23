import 'package:flutter/material.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../legal/legal_content.dart';
import '../../legal/presentation/legal_document.dart';

/// Conditions générales — consultation.
///
/// Le MÊME texte que celui de l'écran de consentement : ce que le vendeur a
/// accepté est exactement ce qu'il relit ici. Deux rédactions divergentes, et
/// l'accord ne vaut plus rien.
class TermsScreen extends StatelessWidget {
  const TermsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(title: Text(l.termsTitle)),
      body: LegalDocument(
        sections: Legal.terms,
        header: LegalHeader(
          title: l.termsTitle,
          icon: Icons.gavel_outlined,
        ),
      ),
    );
  }
}
