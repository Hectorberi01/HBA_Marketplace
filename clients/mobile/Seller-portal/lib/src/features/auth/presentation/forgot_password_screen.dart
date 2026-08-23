import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../data/auth_api.dart';

/// Mot de passe oublié — étape 1 : saisie de l'e-mail.
///
/// À la validation, le serveur envoie un code à 6 chiffres (réponse identique que
/// le compte existe ou non), puis on enchaîne sur l'écran de réinitialisation.
class ForgotPasswordScreen extends ConsumerStatefulWidget {
  const ForgotPasswordScreen({super.key});

  @override
  ConsumerState<ForgotPasswordScreen> createState() => _ForgotPasswordScreenState();
}

class _ForgotPasswordScreenState extends ConsumerState<ForgotPasswordScreen> {
  final _form = GlobalKey<FormState>();
  final _email = TextEditingController();
  bool _loading = false;
  String? _error;

  @override
  void dispose() {
    _email.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_form.currentState!.validate()) return;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final email = _email.text.trim();
      await ref.read(authApiProvider).forgotPassword(email);
      if (!mounted) return;
      context.push('/reset-password', extra: email);
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } catch (e) {
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(
        elevation: 0,
        foregroundColor: colors.ink,
        title: Text(l.authForgotTitle),
      ),
      body: GlowBackground(
        child: SafeArea(
          top: false,
          child: Center(
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(28),
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 420),
                child: Form(
                  key: _form,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      const SizedBox(height: 8),
                      const Icon(Icons.lock_reset_outlined, size: 56, color: AppTheme.brandGreen),
                      const SizedBox(height: 20),
                      Text(
                        l.authForgotHeadline,
                        textAlign: TextAlign.center,
                        style: TextStyle(fontSize: 22, fontWeight: FontWeight.w800, color: colors.ink),
                      ),
                      const SizedBox(height: 8),
                      Text(
                        l.authForgotSubtitle,
                        textAlign: TextAlign.center,
                        style: TextStyle(color: colors.subtle, height: 1.5, fontSize: 13),
                      ),
                      const SizedBox(height: 26),
                      TextFormField(
                        controller: _email,
                        keyboardType: TextInputType.emailAddress,
                        autocorrect: false,
                        autofocus: true,
                        decoration: const InputDecoration(
                          hintText: 'vendeur@boutique.com',
                          prefixIcon: Icon(Icons.mail_outline),
                        ),
                        validator: (v) => (v == null || !v.contains('@')) ? l.authForgotEmailInvalid : null,
                        onFieldSubmitted: (_) => _submit(),
                      ),
                      if (_error != null) ...[
                        const SizedBox(height: 14),
                        _ErrorBanner(_error!),
                      ],
                      const SizedBox(height: 24),
                      FilledButton(
                        onPressed: _loading ? null : _submit,
                        child: _loading
                            ? const SizedBox(
                                width: 22,
                                height: 22,
                                child: CircularProgressIndicator(strokeWidth: 2.4, color: Colors.white),
                              )
                            : Text(l.authForgotSendCode),
                      ),
                      const SizedBox(height: 12),
                      TextButton(
                        onPressed: _loading ? null : () => context.go('/login'),
                        child: Text(l.authForgotBackToLogin),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _ErrorBanner extends StatelessWidget {
  const _ErrorBanner(this.message);
  final String message;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: AppTheme.danger.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppTheme.danger.withValues(alpha: 0.25)),
      ),
      child: Row(children: [
        const Icon(Icons.error_outline, color: AppTheme.danger, size: 18),
        const SizedBox(width: 10),
        Expanded(child: Text(message, style: const TextStyle(color: AppTheme.danger, fontSize: 13))),
      ]),
    );
  }
}
