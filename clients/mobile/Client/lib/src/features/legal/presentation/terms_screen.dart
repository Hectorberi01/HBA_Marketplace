import 'package:flutter/material.dart';

import '../legal_content.dart';
import 'legal_document.dart';

/// Conditions générales — consultation.
///
/// Le MÊME texte que celui présenté à l'acceptation : ce que l'acheteur a accepté
/// est exactement ce qu'il relit ici. Deux rédactions divergentes, et l'accord ne
/// vaut plus rien.
class TermsScreen extends StatelessWidget {
  const TermsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Conditions générales')),
      body: const LegalDocument(
        sections: Legal.terms,
        header: LegalHeader(title: 'Conditions générales', icon: Icons.gavel_outlined),
      ),
    );
  }
}
