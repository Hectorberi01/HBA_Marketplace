import 'dart:async';

import 'package:flutter/material.dart';

import '../../core/theme/app_theme.dart';

/// Notifications légères (toasts) affichées **en haut** de l'écran.
///
/// Pourquoi ne plus utiliser `SnackBar` :
///
///  1. **Position.** Un SnackBar naît en bas, par construction. Or le bas de nos
///     écrans est occupé par ce qui compte : la barre d'onglets, « Acheter »,
///     « Payer ». Le toast venait donc recouvrir précisément le bouton que
///     l'utilisateur s'apprêtait à toucher.
///
///  2. **Disparition.** Le SnackBar ne se ferme pas toujours au bout de sa
///     `duration` : `ScaffoldMessenger` suspend le compte à rebours quand la
///     navigation accessible est active (VoiceOver/TalkBack), et un SnackBar
///     porté par un Scaffold démonté peut rester affiché par le messenger de la
///     route parente. Résultat : une bannière qui « reste collée ».
///
/// Ici, le toast est un [OverlayEntry] posé sur l'overlay racine, avec sa PROPRE
/// minuterie. Il se retire tout seul, quoi qu'il arrive à l'écran qui l'a
/// déclenché — et on peut le chasser d'une simple tape.
class AppNotify {
  const AppNotify._();

  /// Un seul toast à la fois : le nouveau chasse l'ancien, jamais d'empilement.
  static OverlayEntry? _current;

  static void _show(
    BuildContext context, {
    required String message,
    required IconData icon,
    required Color iconColor,
    String? actionLabel,
    VoidCallback? onAction,
    Duration duration = const Duration(seconds: 3),
  }) {
    // `rootOverlay` : au-dessus de la barre d'onglets et des feuilles modales.
    // `maybeOf` plutôt que `of` : appelé depuis un contexte sans Overlay (test,
    // callback tardif), on ne fait rien plutôt que de lever.
    final overlay = Overlay.maybeOf(context, rootOverlay: true);
    if (overlay == null) return;

    _removeCurrent();

    late final OverlayEntry entry;
    entry = OverlayEntry(
      builder: (_) => _TopToast(
        message: message,
        icon: icon,
        iconColor: iconColor,
        actionLabel: actionLabel,
        onAction: onAction,
        duration: duration,
        onFinished: () {
          if (identical(_current, entry)) _current = null;
          if (entry.mounted) entry.remove();
        },
      ),
    );

    _current = entry;
    overlay.insert(entry);
  }

  static void _removeCurrent() {
    final previous = _current;
    _current = null;
    if (previous != null && previous.mounted) previous.remove();
  }

  /// Notification de succès (icône verte).
  static void success(
    BuildContext context,
    String message, {
    String? actionLabel,
    VoidCallback? onAction,
  }) =>
      _show(
        context,
        message: message,
        icon: Icons.check_circle,
        iconColor: AppTheme.brandGreen,
        actionLabel: actionLabel,
        onAction: onAction,
      );

  /// Notification informative (icône neutre) — ex. coordonnées de support.
  static void info(BuildContext context, String message, {String? actionLabel, VoidCallback? onAction}) => _show(
        context,
        message: message,
        icon: Icons.info_outline,
        iconColor: AppTheme.brandGreen,
        actionLabel: actionLabel,
        onAction: onAction,
      );

  /// Notification d'erreur (icône rouge, durée un peu plus longue : on lit plus
  /// lentement un message qu'on n'attendait pas).
  static void error(BuildContext context, String message) => _show(
        context,
        message: message,
        icon: Icons.error_outline,
        iconColor: AppTheme.danger,
        duration: const Duration(seconds: 4),
      );
}

class _TopToast extends StatefulWidget {
  const _TopToast({
    required this.message,
    required this.icon,
    required this.iconColor,
    required this.duration,
    required this.onFinished,
    this.actionLabel,
    this.onAction,
  });

  final String message;
  final IconData icon;
  final Color iconColor;
  final Duration duration;
  final VoidCallback onFinished;
  final String? actionLabel;
  final VoidCallback? onAction;

  @override
  State<_TopToast> createState() => _TopToastState();
}

class _TopToastState extends State<_TopToast> with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 220),
  );

  Timer? _timer;

  /// Garde-fou : la sortie peut être demandée par la minuterie, par une tape ou
  /// par le bouton d'action. Sans ce drapeau, deux sorties concurrentes
  /// appelleraient `remove()` deux fois — et `OverlayEntry.remove()` lève si
  /// l'entrée n'est plus montée.
  bool _leaving = false;

  @override
  void initState() {
    super.initState();
    _controller.forward();
    _timer = Timer(widget.duration, _hide);
  }

  Future<void> _hide() async {
    if (_leaving) return;
    _leaving = true;
    _timer?.cancel();
    if (mounted) {
      await _controller.reverse();
    }
    widget.onFinished();
  }

  @override
  void dispose() {
    _timer?.cancel();
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final topInset = MediaQuery.of(context).padding.top;
    final curved = CurvedAnimation(
      parent: _controller,
      curve: Curves.easeOutCubic,
      reverseCurve: Curves.easeInCubic,
    );

    return Positioned(
      top: topInset + 8,
      left: 12,
      right: 12,
      child: SlideTransition(
        position: Tween(begin: const Offset(0, -1.4), end: Offset.zero).animate(curved),
        child: FadeTransition(
          opacity: curved,
          child: Material(
            color: Colors.transparent,
            child: GestureDetector(
              // Tape ou glissement vers le haut : le toast s'efface. Il ne doit
              // jamais être une gêne dont on ne puisse pas se débarrasser.
              onTap: _hide,
              onVerticalDragEnd: (details) {
                if ((details.primaryVelocity ?? 0) < 0) _hide();
              },
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                decoration: BoxDecoration(
                  color: AppTheme.surface,
                  borderRadius: BorderRadius.circular(16),
                  border: Border.all(color: AppTheme.line),
                  boxShadow: [
                    BoxShadow(
                      color: Colors.black.withValues(alpha: 0.10),
                      blurRadius: 24,
                      offset: const Offset(0, 8),
                    ),
                  ],
                ),
                child: Row(
                  children: [
                    Container(
                      width: 34,
                      height: 34,
                      decoration: BoxDecoration(
                        color: widget.iconColor.withValues(alpha: 0.12),
                        shape: BoxShape.circle,
                      ),
                      child: Icon(widget.icon, color: widget.iconColor, size: 19),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Text(
                        widget.message,
                        style: TextStyle(
                          color: AppTheme.ink,
                          fontWeight: FontWeight.w700,
                          fontSize: 14,
                          height: 1.3,
                        ),
                      ),
                    ),
                    if (widget.actionLabel != null && widget.onAction != null) ...[
                      const SizedBox(width: 8),
                      TextButton(
                        style: TextButton.styleFrom(
                          foregroundColor: AppTheme.brandGreen,
                          padding: const EdgeInsets.symmetric(horizontal: 10),
                          minimumSize: const Size(0, 36),
                          tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                        ),
                        onPressed: () {
                          // On ferme AVANT d'agir : l'action navigue souvent
                          // (« Voir » → le panier), et un toast qui survit à la
                          // navigation est exactement le défaut qu'on corrige.
                          _hide();
                          widget.onAction!.call();
                        },
                        child: Text(
                          widget.actionLabel!,
                          style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 13),
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
