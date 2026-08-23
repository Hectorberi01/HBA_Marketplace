import 'package:flutter/material.dart';

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
    return ListView(
      controller: controller,
      padding: padding,
      children: [
        if (header != null) header!,
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
                    style: TextStyle(fontSize: 13.5, height: 1.6, color: AppTheme.ink),
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
                color: AppTheme.softGreen,
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
                          fontSize: 15, fontWeight: FontWeight.w800, color: AppTheme.ink)),
                  const SizedBox(height: 2),
                  Text('Version du ${Legal.lastUpdated} · ${Legal.company}',
                      style: TextStyle(fontSize: 12, color: AppTheme.subtle)),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
