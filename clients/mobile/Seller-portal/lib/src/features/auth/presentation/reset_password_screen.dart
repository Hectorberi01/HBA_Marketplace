import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../data/auth_api.dart';

/// Mot de passe oublié — étape 2 : code reçu par e-mail + nouveau mot de passe.
class ResetPasswordScreen extends ConsumerStatefulWidget {
  const ResetPasswordScreen({super.key, required this.email});

  final String email;

  @override
  ConsumerState<ResetPasswordScreen> createState() => _ResetPasswordScreenState();
}

class _ResetPasswordScreenState extends ConsumerState<ResetPasswordScreen> {
  final _form = GlobalKey<FormState>();
  final _code = TextEditingController();
  final _password = TextEditingController();
  bool _loading = false;
  bool _obscure = true;
  bool _resending = false;
  String? _error;

  @override
  void dispose() {
    _code.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_form.currentState!.validate()) return;
    final l = AppLocalizations.of(context);
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      // LE PARAMÈTRE S'APPELLE `token`, ET CE N'EST PAS UN RENOMMAGE COSMÉTIQUE.
      //
      // On envoyait `code:`. Le contrat serveur est
      // `ResetPasswordRequest(Email, Token, NewPassword)` : le corps arrivait
      // donc avec un jeton NUL, la réinitialisation échouait en validation, et
      // le message renvoyé ne nommait pas le champ fautif — le vendeur voyait
      // « demande invalide » en ayant saisi le bon code.
      //
      // Le libellé de l'écran reste « code » : c'est bien un code à six chiffres
      // que le vendeur reçoit. Seul le nom du champ dans le corps HTTP change.
      await ref.read(authApiProvider).resetPassword(
            email: widget.email,
            token: _code.text.trim(),
            newPassword: _password.text,
          );
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(l.authResetSuccess)),
      );
      context.go('/login');
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } catch (e) {
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _resend() async {
    final l = AppLocalizations.of(context);
    setState(() {
      _resending = true;
      _error = null;
    });
    try {
      await ref.read(authApiProvider).forgotPassword(widget.email);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(l.authResetCodeResent)),
      );
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } catch (e) {
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _resending = false);
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
        title: Text(l.authResetTitle),
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
                      const Icon(Icons.password_outlined, size: 56, color: AppTheme.brandGreen),
                      const SizedBox(height: 20),
                      Text(
                        l.authResetHeadline,
                        textAlign: TextAlign.center,
                        style: TextStyle(fontSize: 22, fontWeight: FontWeight.w800, color: colors.ink),
                      ),
                      const SizedBox(height: 8),
                      Text.rich(
                        TextSpan(
                          text: l.authResetSentTo,
                          style: TextStyle(color: colors.subtle, height: 1.5),
                          children: [
                            TextSpan(
                              text: widget.email,
                              style: TextStyle(color: colors.ink, fontWeight: FontWeight.w700),
                            ),
                          ],
                        ),
                        textAlign: TextAlign.center,
                      ),
                      const SizedBox(height: 24),

                      _Label(l.authResetCodeLabel),
                      TextFormField(
                        controller: _code,
                        keyboardType: TextInputType.number,
                        maxLength: 6,
                        autofocus: true,
                        inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                        decoration: const InputDecoration(counterText: '', hintText: '••••••'),
                        validator: (v) => (v == null || v.trim().length < 6) ? l.authResetCodeRequired : null,
                      ),
                      const SizedBox(height: 16),

                      _Label(l.authResetPasswordLabel),
                      TextFormField(
                        controller: _password,
                        obscureText: _obscure,
                        decoration: InputDecoration(
                          hintText: '••••••••',
                          prefixIcon: const Icon(Icons.lock_outline),
                          suffixIcon: IconButton(
                            icon: Icon(_obscure ? Icons.visibility_outlined : Icons.visibility_off_outlined),
                            onPressed: () => setState(() => _obscure = !_obscure),
                          ),
                        ),
                        validator: (v) => (v == null || v.length < 6) ? l.authResetPasswordMin : null,
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
                            : Text(l.authResetSubmit),
                      ),
                      const SizedBox(height: 12),
                      TextButton(
                        onPressed: _resending ? null : _resend,
                        child: Text(_resending ? l.authResetSending : l.authResetResend),
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

class _Label extends StatelessWidget {
  const _Label(this.text);
  final String text;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: 8, left: 2),
      child: Text(text,
          style: TextStyle(fontSize: 13, fontWeight: FontWeight.w700, color: colors.ink)),
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
