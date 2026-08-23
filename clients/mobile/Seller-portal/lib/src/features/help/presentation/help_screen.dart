import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../legal/legal_content.dart';
import '../help_content.dart';

/// Centre d'aide : FAQ recherchable + contact du support. Aucun appel réseau —
/// le contenu est statique et le contact ouvre l'app mail du téléphone.
class HelpScreen extends StatefulWidget {
  const HelpScreen({super.key});

  @override
  State<HelpScreen> createState() => _HelpScreenState();
}

class _HelpScreenState extends State<HelpScreen> {
  final _search = TextEditingController();
  String _query = '';

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  /// Catégories filtrées par la recherche (question OU réponse), dans la langue active.
  List<FaqCategory> _filtered(bool english) {
    final all = HelpContent.faqFor(english);
    final q = _query.trim().toLowerCase();
    if (q.isEmpty) return all;
    return all
        .map((c) => FaqCategory(
              c.title,
              c.icon,
              c.items
                  .where((i) =>
                      i.question.toLowerCase().contains(q) || i.answer.toLowerCase().contains(q))
                  .toList(),
            ))
        .where((c) => c.items.isNotEmpty)
        .toList();
  }

  Future<void> _contactSupport() async {
    final l = AppLocalizations.of(context);
    final subject = Uri.encodeComponent(l.helpEmailSubject);
    final body = Uri.encodeComponent(l.helpEmailBody(Legal.version));
    final uri = Uri.parse('mailto:${HelpContent.supportEmail}?subject=$subject&body=$body');

    if (await canLaunchUrl(uri)) {
      await launchUrl(uri);
    } else {
      // Pas d'app mail configurée : on copie l'adresse pour que le vendeur ne
      // reste pas bloqué.
      await Clipboard.setData(const ClipboardData(text: HelpContent.supportEmail));
      if (mounted) {
        AppNotify.success(context, l.helpEmailCopied(HelpContent.supportEmail));
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final categories = _filtered(Localizations.localeOf(context).languageCode == 'en');
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);

    return Scaffold(
      appBar: AppBar(title: Text(l.helpTitle)),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 32),
        children: [
          // Contact support, en haut : un vendeur qui ouvre l'aide a souvent déjà
          // un problème précis.
          Container(
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: colors.softGreen,
              borderRadius: BorderRadius.circular(14),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(l.helpNeedHelp,
                    style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 16)),
                const SizedBox(height: 4),
                Text(
                  l.helpBrowseFaq,
                  style: TextStyle(fontSize: 13, color: colors.ink, height: 1.4),
                ),
                const SizedBox(height: 12),
                SizedBox(
                  width: double.infinity,
                  child: FilledButton.icon(
                    onPressed: _contactSupport,
                    icon: const Icon(Icons.mail_outline, size: 18),
                    label: Text(l.helpContactSupport),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 16),

          TextField(
            controller: _search,
            onChanged: (v) => setState(() => _query = v),
            decoration: InputDecoration(
              hintText: l.helpSearchHint,
              prefixIcon: const Icon(Icons.search),
              suffixIcon: _query.isEmpty
                  ? null
                  : IconButton(
                      icon: const Icon(Icons.close),
                      onPressed: () {
                        _search.clear();
                        setState(() => _query = '');
                      },
                    ),
              filled: true,
              fillColor: colors.surface,
              border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(12),
                borderSide: BorderSide(color: colors.line),
              ),
            ),
          ),
          const SizedBox(height: 8),

          if (categories.isEmpty)
            Padding(
              padding: const EdgeInsets.only(top: 40),
              child: Column(
                children: [
                  Icon(Icons.search_off, size: 44, color: colors.subtle),
                  const SizedBox(height: 10),
                  Text(l.helpNoResults(_query),
                      style: TextStyle(color: colors.subtle)),
                  const SizedBox(height: 4),
                  TextButton(
                    onPressed: _contactSupport,
                    child: Text(l.helpAskSupport),
                  ),
                ],
              ),
            )
          else
            for (final category in categories) _CategoryBlock(category: category),
        ],
      ),
    );
  }
}

class _CategoryBlock extends StatelessWidget {
  const _CategoryBlock({required this.category});
  final FaqCategory category;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(4, 18, 4, 8),
          child: Row(
            children: [
              Icon(category.icon, size: 18, color: AppTheme.brandGreen),
              const SizedBox(width: 8),
              Text(category.title,
                  style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 14)),
            ],
          ),
        ),
        Container(
          decoration: BoxDecoration(
            color: colors.surface,
            borderRadius: BorderRadius.circular(14),
            border: Border.all(color: colors.line),
          ),
          child: Column(
            children: [
              for (var i = 0; i < category.items.length; i++) ...[
                if (i > 0) Divider(height: 1, color: colors.line),
                Theme(
                  data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
                  child: ExpansionTile(
                    tilePadding: const EdgeInsets.symmetric(horizontal: 16),
                    childrenPadding: const EdgeInsets.fromLTRB(16, 0, 16, 14),
                    expandedCrossAxisAlignment: CrossAxisAlignment.start,
                    title: Text(category.items[i].question,
                        style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14)),
                    children: [
                      Text(category.items[i].answer,
                          style: TextStyle(fontSize: 13, height: 1.5, color: colors.ink)),
                    ],
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
