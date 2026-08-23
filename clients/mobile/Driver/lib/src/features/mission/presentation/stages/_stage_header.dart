import 'package:flutter/material.dart';

import '../../../../core/theme/app_theme.dart';

/// En-tête « ‹ Titre » des étapes qui ne sont pas des cartes.
class StageHeader extends StatelessWidget {
  const StageHeader({super.key, required this.title, required this.onBack});

  final String title;
  final VoidCallback onBack;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Padding(
      padding: const EdgeInsets.fromLTRB(10, 6, 20, 6),
      child: Row(
        children: [
          InkWell(
            onTap: onBack,
            borderRadius: BorderRadius.circular(24),
            child: Container(
              width: AppTheme.minTapTarget,
              height: AppTheme.minTapTarget,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: colors.surface,
                shape: BoxShape.circle,
                border: Border.all(color: colors.line),
              ),
              child: Icon(Icons.chevron_left, size: 22, color: colors.ink),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              title,
              style: TextStyle(
                fontSize: 17,
                fontWeight: FontWeight.w800,
                color: colors.ink,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
