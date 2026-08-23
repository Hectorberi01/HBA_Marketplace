import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/theme/app_theme.dart';

/// Carte blanche standard : le conteneur de tout le reste.
class DriverCard extends StatelessWidget {
  const DriverCard({super.key, required this.child, this.padding});

  final Widget child;
  final EdgeInsets? padding;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Container(
      width: double.infinity,
      padding: padding ?? const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        border: Border.all(color: colors.line),
      ),
      child: child,
    );
  }
}

/// Champ de saisie de la maquette : libellé au-dessus, bordure fine.
///
/// LE CONTRÔLEUR EST CRÉÉ UNE FOIS, PAS À CHAQUE `build`.
///
/// Le recréer depuis la valeur reposerait le curseur au début à chaque frappe —
/// le texte s'écrirait à l'envers. Défaut invisible en relecture, immédiat à
/// l'usage.
class DriverField extends StatefulWidget {
  const DriverField({
    super.key,
    this.label,
    required this.hint,
    required this.onChanged,
    this.initial = '',
    this.obscure = false,
    this.numeric = false,
    this.trailing,
  });

  final String? label;
  final String hint;
  final String initial;
  final ValueChanged<String> onChanged;
  final bool obscure;
  final bool numeric;
  final Widget? trailing;

  @override
  State<DriverField> createState() => _DriverFieldState();
}

class _DriverFieldState extends State<DriverField> {
  late final TextEditingController _controller =
      TextEditingController(text: widget.initial);

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (widget.label != null) ...[
          Text(
            widget.label!,
            style: TextStyle(fontSize: 12.5, color: colors.subtle),
          ),
          const SizedBox(height: 6),
        ],
        TextField(
          controller: _controller,
          onChanged: widget.onChanged,
          obscureText: widget.obscure,
          keyboardType: widget.numeric ? TextInputType.number : TextInputType.text,
          inputFormatters:
              widget.numeric ? [FilteringTextInputFormatter.digitsOnly] : null,
          style: TextStyle(fontSize: 15.5, color: colors.ink),
          decoration: InputDecoration(
            hintText: widget.hint,
            hintStyle: TextStyle(fontSize: 15.5, color: colors.subtle),
            filled: true,
            fillColor: colors.surface,
            suffixIcon: widget.trailing,
            contentPadding:
                const EdgeInsets.symmetric(horizontal: 16, vertical: 17),
            enabledBorder: OutlineInputBorder(
              borderRadius: BorderRadius.circular(AppTheme.radiusField),
              borderSide: BorderSide(color: colors.line),
            ),
            focusedBorder: OutlineInputBorder(
              borderRadius: BorderRadius.circular(AppTheme.radiusField),
              borderSide: const BorderSide(color: AppTheme.brandGreen, width: 1.6),
            ),
          ),
        ),
      ],
    );
  }
}

/// Bouton principal vert, pleine largeur.
class DriverPrimaryButton extends StatelessWidget {
  const DriverPrimaryButton({super.key, required this.label, this.onPressed});

  final String label;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) => FilledButton(
        onPressed: onPressed,
        style: FilledButton.styleFrom(
          minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
          backgroundColor: AppTheme.brandGreen,
          foregroundColor: Colors.white,
          disabledBackgroundColor: AppTheme.brandGreen.withValues(alpha: 0.35),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(AppTheme.radiusField),
          ),
          textStyle: const TextStyle(fontSize: 16, fontWeight: FontWeight.w800),
        ),
        child: Text(label),
      );
}

/// Bouton secondaire à contour fin.
class DriverSecondaryButton extends StatelessWidget {
  const DriverSecondaryButton({super.key, required this.label, this.onPressed});

  final String label;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return OutlinedButton(
      onPressed: onPressed,
      style: OutlinedButton.styleFrom(
        minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
        side: BorderSide(color: colors.line),
        foregroundColor: colors.ink,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(AppTheme.radiusField),
        ),
        textStyle: const TextStyle(fontSize: 15.5, fontWeight: FontWeight.w700),
      ),
      child: Text(label),
    );
  }
}

/// Tuile de statistique : une valeur, un libellé.
class DriverStatTile extends StatelessWidget {
  const DriverStatTile({
    super.key,
    required this.label,
    required this.value,
    this.unit,
    this.caption,
  });

  final String label;
  final String value;
  final String? unit;
  final String? caption;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return DriverCard(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 13),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: TextStyle(fontSize: 12, color: colors.subtle)),
          const SizedBox(height: 6),
          Row(
            crossAxisAlignment: CrossAxisAlignment.baseline,
            textBaseline: TextBaseline.alphabetic,
            children: [
              Flexible(
                child: Text(
                  value,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontSize: 20,
                    fontWeight: FontWeight.w800,
                    color: colors.ink,
                  ),
                ),
              ),
              if (unit != null) ...[
                const SizedBox(width: 4),
                Text(
                  unit!,
                  style: TextStyle(
                    fontSize: 11,
                    fontWeight: FontWeight.w700,
                    color: colors.subtle,
                  ),
                ),
              ],
            ],
          ),
          if (caption != null) ...[
            const SizedBox(height: 2),
            Text(caption!, style: TextStyle(fontSize: 11, color: colors.subtle)),
          ],
        ],
      ),
    );
  }
}

/// Cloche de notifications, avec pastille.
class DriverBell extends StatelessWidget {
  const DriverBell({super.key, this.hasUnread = true, this.onTap});

  final bool hasUnread;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(12),
      child: Container(
        width: AppTheme.minTapTarget,
        height: AppTheme.minTapTarget,
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: colors.line),
        ),
        child: Stack(
          alignment: Alignment.center,
          clipBehavior: Clip.none,
          children: [
            Icon(Icons.notifications_none_rounded, size: 21, color: colors.ink),
            if (hasUnread)
              Positioned(
                top: 12,
                right: 13,
                child: Container(
                  width: 8,
                  height: 8,
                  decoration: const BoxDecoration(
                    color: AppTheme.danger,
                    shape: BoxShape.circle,
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

/// État vide encadré de pointillés — « Aucune mission active ».
class DriverEmptyBox extends StatelessWidget {
  const DriverEmptyBox({super.key, required this.title, required this.message});

  final String title;
  final String message;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 22),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        border: Border.all(color: colors.line),
      ),
      child: Column(
        children: [
          Text(
            title,
            style: TextStyle(
              fontSize: 15.5,
              fontWeight: FontWeight.w800,
              color: colors.ink,
            ),
          ),
          const SizedBox(height: 4),
          Text(
            message,
            textAlign: TextAlign.center,
            style: TextStyle(fontSize: 13, color: colors.subtle),
          ),
        ],
      ),
    );
  }
}

/// Écran encore vide, nommé. Sert de destination aux onglets non implémentés.
///
/// UN ÉCRAN QUI DIT SON NOM PLUTÔT QU'UNE PAGE BLANCHE.
///
/// Une route manquante fait planter go_router ; une page blanche fait croire à
/// un bug. Nommer ce qui n'existe pas encore est la seule option honnête.
class DriverPlaceholderScreen extends StatelessWidget {
  const DriverPlaceholderScreen({
    super.key,
    required this.title,
    required this.note,
  });

  final String title;
  final String note;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(Icons.construction_outlined, size: 34, color: colors.line),
              const SizedBox(height: 14),
              Text(
                title,
                style: TextStyle(
                  fontSize: 19,
                  fontWeight: FontWeight.w800,
                  color: colors.ink,
                ),
              ),
              const SizedBox(height: 6),
              Text(
                note,
                textAlign: TextAlign.center,
                style: TextStyle(fontSize: 13.5, height: 1.4, color: colors.subtle),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
