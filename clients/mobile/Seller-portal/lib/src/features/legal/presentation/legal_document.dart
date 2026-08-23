import 'package:flutter/material.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../legal_content.dart';

/// Rendu commun d'un document légal : sections titrées, texte aéré.
///
/// Un seul composant pour les conditions et pour la confidentialité, et le MÊME
/// que celui de l'écran de consentement : ce que l'utilisateur accepte est
/// exactement ce qu'il pourra relire ensuite, à la virgule près.
class LegalDocument extends StatelessWidget {
  const LegalDocument({
    super.key,
    required this.sections,
    this.controller,
    this.padding = const EdgeInsets.only(bottom: 40),
    this.header,
  });

  final List<LegalSection> sections;
  final ScrollController? controller;
  final EdgeInsets padding;
  final Widget? header;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    // Le texte contractuel n'est publié qu'en français (langue qui fait foi).
    // En anglais, on l'explique brièvement plutôt que d'afficher une traduction
    // qui n'aurait aucune valeur juridique.
    final showFrenchNote = Localizations.localeOf(context).languageCode == 'en';
    return ListView(
      controller: controller,
      padding: padding,
      children: [
        if (header != null) header!,
        if (showFrenchNote)
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
            child: Container(
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: AppTheme.info.withValues(alpha: 0.10),
                borderRadius: BorderRadius.circular(12),
              ),
              child: Row(
                children: [
                  const Icon(Icons.info_outline, size: 18, color: AppTheme.info),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Text(l.legalFrenchNote,
                        style: TextStyle(fontSize: 12.5, height: 1.4, color: colors.ink)),
                  ),
                ],
              ),
            ),
          ),
        for (final section in sections) ...[
          SectionHeader(title: section.title),
          CardSection(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                for (var i = 0; i < section.body.length; i++) ...[
                  if (i > 0) const SizedBox(height: 12),
                  Text(
                    section.body[i],
                    style: TextStyle(fontSize: 13.5, height: 1.6, color: colors.ink),
                  ),
                ],
              ],
            ),
          ),
        ],
      ],
    );
  }
}

/// Bandeau de tête : date de version. Une politique sans date ne permet pas de
/// savoir quelle rédaction l'utilisateur a acceptée — et c'est précisément la
/// question qu'on pose le jour d'un litige.
class LegalHeader extends StatelessWidget {
  const LegalHeader({super.key, required this.title, required this.icon});

  final String title;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    return Padding(
      padding: const EdgeInsets.only(top: 16),
      child: CardSection(
        padding: const EdgeInsets.all(16),
        child: Row(
          children: [
            Container(
              width: 40,
              height: 40,
              decoration: BoxDecoration(
                color: colors.softGreen,
                borderRadius: BorderRadius.circular(12),
              ),
              child: Icon(icon, size: 20, color: AppTheme.brandGreen),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title,
                      style: TextStyle(
                          fontSize: 15, fontWeight: FontWeight.w800, color: colors.ink)),
                  const SizedBox(height: 2),
                  Text(l.legalVersion(Legal.lastUpdated, Legal.company),
                      style: TextStyle(fontSize: 12, color: colors.subtle)),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
