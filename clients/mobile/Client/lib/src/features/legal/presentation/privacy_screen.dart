import 'package:flutter/material.dart';

import '../legal_content.dart';
import 'legal_document.dart';

/// Politique de confidentialité — consultation.
/// Soumise au Livre V du Code du numérique et au contrôle de l'APDP.
class PrivacyScreen extends StatelessWidget {
  const PrivacyScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Confidentialité')),
      body: const LegalDocument(
        sections: Legal.privacy,
        header: LegalHeader(title: 'Vos données', icon: Icons.lock_outline),
      ),
    );
  }
}
