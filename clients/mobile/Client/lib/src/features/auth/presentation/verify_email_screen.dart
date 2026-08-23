import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../data/auth_api.dart';

/// Vérification de l'adresse e-mail par code à 6 chiffres, à la création de compte.
///
/// Une fois le code validé, l'e-mail est vérifié mais le compte reste en attente
/// de validation par un administrateur : on l'annonce clairement plutôt que de
/// tenter une connexion qui échouerait.
class VerifyEmailScreen extends ConsumerStatefulWidget {
  const VerifyEmailScreen({super.key, required this.args});

  final EmailVerifyArgs args;

  @override
  ConsumerState<VerifyEmailScreen> createState() => _VerifyEmailScreenState();
}

class _VerifyEmailScreenState extends ConsumerState<VerifyEmailScreen> {
  final _code = TextEditingController();
  bool _loading = false;
  bool _done = false;
  String? _error;

  @override
  void dispose() {
    _code.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final code = _code.text.trim();
    if (code.length < 6) {
      setState(() => _error = 'Entrez le code à 6 chiffres.');
      return;
    }
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      await ref.read(authApiProvider).confirmEmail(email: widget.args.email, code: code);
      if (!mounted) return;
      setState(() => _done = true);
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
    return Scaffold(
      backgroundColor: AppTheme.bg,
      body: GlowBackground(
        child: SafeArea(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 460),
              child: _done ? _buildDone(context) : _buildForm(context),
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildForm(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Align(
          alignment: Alignment.centerLeft,
          child: SoftCircleButton(icon: Icons.arrow_back, onTap: () => context.pop(), semanticLabel: 'Retour'),
        ),
        const SizedBox(height: 20),
        const Center(child: Icon(Icons.mark_email_read_outlined, size: 56, color: AppTheme.brandGreen)),
        const SizedBox(height: 20),
        Text('Vérifiez votre e-mail',
            textAlign: TextAlign.center,
            style: TextStyle(fontSize: 24, fontWeight: FontWeight.w800, color: AppTheme.ink)),
        const SizedBox(height: 8),
        Text.rich(
          TextSpan(
            text: 'Entrez le code à 6 chiffres envoyé à\n',
            style: TextStyle(color: AppTheme.subtle, height: 1.5),
            children: [
              TextSpan(
                text: widget.args.email,
                style: TextStyle(color: AppTheme.ink, fontWeight: FontWeight.w700),
              ),
            ],
          ),
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 28),
        TextField(
          controller: _code,
          keyboardType: TextInputType.number,
          textAlign: TextAlign.center,
          maxLength: 6,
          autofocus: true,
          inputFormatters: [FilteringTextInputFormatter.digitsOnly],
          style: TextStyle(fontSize: 30, fontWeight: FontWeight.w800, letterSpacing: 10, color: AppTheme.ink),
          decoration: InputDecoration(
            counterText: '',
            hintText: '••••••',
            hintStyle: TextStyle(letterSpacing: 10, color: AppTheme.subtle),
          ),
          onSubmitted: (_) => _submit(),
        ),
        if (_error != null) ...[
          const SizedBox(height: 14),
          Text(_error!, textAlign: TextAlign.center, style: const TextStyle(color: AppTheme.danger, fontSize: 13)),
        ],
        const SizedBox(height: 24),
        FilledButton(
          onPressed: _loading ? null : _submit,
          child: _loading
              ? const SizedBox(height: 22, width: 22, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
              : const Text('Valider'),
        ),
      ],
    );
  }

  Widget _buildDone(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SizedBox(height: 40),
        const Center(child: Icon(Icons.verified_outlined, size: 64, color: AppTheme.brandGreen)),
        const SizedBox(height: 24),
        Text('E-mail vérifié',
            textAlign: TextAlign.center,
            style: TextStyle(fontSize: 24, fontWeight: FontWeight.w800, color: AppTheme.ink)),
        const SizedBox(height: 12),
        Text(
          'Merci ! Votre adresse est confirmée. Votre compte sera activé après validation par notre équipe — vous pourrez alors vous connecter.',
          textAlign: TextAlign.center,
          style: TextStyle(color: AppTheme.subtle, height: 1.5),
        ),
        const SizedBox(height: 32),
        FilledButton(
          onPressed: () => context.go('/login'),
          child: const Text('Retour à la connexion'),
        ),
      ],
    );
  }
}
