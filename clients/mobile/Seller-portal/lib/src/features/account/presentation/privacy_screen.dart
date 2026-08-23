import 'package:flutter/material.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../legal/legal_content.dart';
import '../../legal/presentation/legal_document.dart';

/// Politique de confidentialité — consultation.
///
/// Soumise au Livre V du Code du numérique et au contrôle de l'APDP. Même texte
/// que celui présenté à l'acceptation.
class PrivacyScreen extends StatelessWidget {
  const PrivacyScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(title: Text(l.privTitle)),
      body: LegalDocument(
        sections: Legal.privacy,
        header: LegalHeader(
          title: l.privHeaderTitle,
          icon: Icons.lock_outline,
        ),
      ),
    );
  }
}
