import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/network/api_exception.dart';
import '../../../core/providers/core_providers.dart';
import '../../../core/security/biometric_service.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../application/auth_controller.dart';
import '../data/auth_api.dart';

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});

  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _form = GlobalKey<FormState>();
  final _email = TextEditingController();
  final _password = TextEditingController();
  final _mfa = TextEditingController();
  bool _loading = false;
  bool _obscure = true;
  String? _error;
  // Vrai quand le login échoue parce que l'e-mail n'est pas encore vérifié :
  // on propose alors de renvoyer le code.
  bool _needsVerification = false;
  // Vrai quand le compte a la double authentification : on affiche le champ code.
  bool _mfaRequired = false;

  // Biométrie (Face ID / Touch ID).
  bool _bioAvailable = false;
  bool _bioEnabled = false;
  bool _rememberBio = false;
  String _bioLabel = 'Biométrie';

  @override
  void initState() {
    super.initState();
    _initBiometrics();
  }

  Future<void> _initBiometrics() async {
    final bio = ref.read(biometricServiceProvider);
    final available = await bio.isAvailable;
    if (!available || !mounted) return;
    final enabled = await bio.isEnabled;
    final label = await bio.label();
    if (!mounted) return;
    setState(() {
      _bioAvailable = true;
      _bioEnabled = enabled;
      _bioLabel = label;
    });
  }

  /// Déverrouille par biométrie puis rouvre la session via le refresh token
  /// mémorisé — sans mot de passe.
  Future<void> _biometricLogin() async {
    final session = await ref.read(biometricServiceProvider).unlock('Connectez-vous à HBA Express');
    if (session == null || !mounted) return;
    // Pré-remplit l'e-mail : si le jeton est périmé, le repli mot de passe est prêt.
    _email.text = session.email;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      await ref.read(authControllerProvider.notifier).loginWithBiometrics(session.refreshToken);
      // Le routeur bascule seul dès la session ouverte.
    } catch (e) {
      // Jeton révoqué/expiré : on désarme la biométrie périmée et on invite à
      // retaper le mot de passe (l'e-mail est déjà rempli).
      await ref.read(biometricServiceProvider).disable();
      if (mounted) {
        setState(() {
          _bioEnabled = false;
          _error = 'Connexion biométrique expirée. Saisissez votre mot de passe.';
        });
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    _mfa.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_form.currentState!.validate()) return;
    setState(() {
      _loading = true;
      _error = null;
      _needsVerification = false;
    });
    try {
      await ref.read(authControllerProvider.notifier).login(
            _email.text.trim(),
            _password.text,
            mfaCode: _mfaRequired && _mfa.text.trim().isNotEmpty ? _mfa.text.trim() : null,
          );
      // Arme la biométrie si demandé — avec le REFRESH TOKEN fraîchement obtenu,
      // jamais le mot de passe. Le token suit ensuite les rotations tout seul.
      if (_rememberBio && _bioAvailable) {
        final rt = await ref.read(tokenStorageProvider).refreshToken;
        if (rt != null && rt.isNotEmpty) {
          await ref.read(biometricServiceProvider).enable(_email.text.trim(), rt);
        }
      }
    } catch (e) {
      final code = e is ApiException ? e.code : null;
      setState(() {
        _error = e is ApiException ? e.message : e.toString();
        _needsVerification = code == 'identity.auth.pending_approval';
        if (code == 'mfa_required') _mfaRequired = true;
      });
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  /// Renvoie un code de vérification puis ouvre l'écran de saisie du code.
  Future<void> _resendCode() async {
    final email = _email.text.trim();
    if (!email.contains('@')) {
      setState(() => _error = 'Saisissez votre adresse e-mail.');
      return;
    }
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      await ref.read(authApiProvider).resendEmailCode(email);
      if (!mounted) return;

      // ON NE SAIT PLUS SI LE COMPTE EXISTE, ET C'EST LE BUT.
      //
      // L'ancien appel rendait l'`userId` — donc la réponse disait si l'adresse
      // était inscrite, sur une route anonyme. On énumérait ainsi les comptes de
      // la plateforme. La passerelle répond désormais la même chose dans tous
      // les cas.
      //
      // Conséquence assumée pour l'interface : on ne peut plus afficher
      // « aucun compte pour cette adresse ». On avance vers la saisie du code,
      // et c'est là que l'erreur apparaîtra — au moment où elle n'apprend plus
      // rien à personne, puisqu'il faut alors connaître le code.
      setState(() => _needsVerification = false);
      context.push('/verify-email', extra: EmailVerifyArgs(email: email));
    } catch (e) {
      setState(() => _error = e is ApiException ? e.message : e.toString());
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
                      const SizedBox(height: 20),
                      const Center(child: BrandLogo(size: 64)),
                      const SizedBox(height: 22),
                      Text('Se connecter',
                          textAlign: TextAlign.center,
                          style: TextStyle(fontSize: 26, fontWeight: FontWeight.w800, color: AppTheme.ink)),
                      const SizedBox(height: 6),
                      Text('Accédez à vos commandes et favoris',
                          textAlign: TextAlign.center, style: TextStyle(color: AppTheme.subtle)),
                      const SizedBox(height: 32),
                      const _FieldLabel('Email'),
                      TextFormField(
                        controller: _email,
                        keyboardType: TextInputType.emailAddress,
                        decoration: const InputDecoration(
                          hintText: 'exemple@email.com',
                          prefixIcon: Icon(Icons.mail_outline),
                        ),
                        validator: (v) => (v == null || !v.contains('@')) ? 'E-mail invalide' : null,
                      ),
                      const SizedBox(height: 18),
                      const _FieldLabel('Mot de passe'),
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
                        validator: (v) => (v == null || v.length < 6) ? '6 caractères minimum' : null,
                      ),
                      Align(
                        alignment: Alignment.centerRight,
                        child: TextButton(
                          onPressed: () => _showForgot(context),
                          child: const Text('Mot de passe oublié ?',
                              style: TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w600)),
                        ),
                      ),
                      // Proposition d'activer la biométrie (si dispo et pas déjà active).
                      if (_bioAvailable && !_bioEnabled)
                        Row(
                          children: [
                            Checkbox(
                              value: _rememberBio,
                              activeColor: AppTheme.brandGreen,
                              visualDensity: VisualDensity.compact,
                              onChanged: (v) => setState(() => _rememberBio = v ?? false),
                            ),
                            Expanded(
                              child: Text(
                                'Activer $_bioLabel pour les prochaines connexions',
                                style: TextStyle(fontSize: 13, color: AppTheme.subtle),
                              ),
                            ),
                          ],
                        ),
                      // Champ code 2FA : affiché seulement quand le serveur le réclame,
                      // pour ne pas dérouter les comptes sans double authentification.
                      if (_mfaRequired) ...[
                        const SizedBox(height: 6),
                        const _FieldLabel('Code de double authentification'),
                        TextFormField(
                          controller: _mfa,
                          keyboardType: TextInputType.number,
                          autofocus: true,
                          decoration: const InputDecoration(
                            hintText: '123456',
                            prefixIcon: Icon(Icons.shield_outlined),
                          ),
                        ),
                        const SizedBox(height: 12),
                      ],
                      if (_error != null) ...[
                        const SizedBox(height: 4),
                        _ErrorBanner(_error!),
                      ],
                      if (_needsVerification) ...[
                        const SizedBox(height: 10),
                        OutlinedButton.icon(
                          onPressed: _loading ? null : _resendCode,
                          icon: const Icon(Icons.mark_email_unread_outlined),
                          label: const Text('Renvoyer le code de vérification'),
                        ),
                      ],
                      const SizedBox(height: 8),
                      FilledButton(
                        onPressed: _loading ? null : _submit,
                        child: _loading
                            ? const SizedBox(height: 22, width: 22, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                            : const Text('Se connecter'),
                      ),
                      if (_bioAvailable && _bioEnabled) ...[
                        const SizedBox(height: 12),
                        OutlinedButton.icon(
                          onPressed: _loading ? null : _biometricLogin,
                          icon: Icon(_bioLabel == 'Face ID' ? Icons.face_outlined : Icons.fingerprint, size: 20),
                          label: Text('Se connecter avec $_bioLabel'),
                        ),
                      ],
                      const SizedBox(height: 22),
                      Row(children: [
                        Expanded(child: Divider(color: AppTheme.line)),
                        Padding(
                          padding: const EdgeInsets.symmetric(horizontal: 12),
                          child: Text('OU', style: TextStyle(color: AppTheme.subtle.withValues(alpha: 0.9), fontSize: 12, fontWeight: FontWeight.w600)),
                        ),
                        Expanded(child: Divider(color: AppTheme.line)),
                      ]),
                      const SizedBox(height: 20),
                      Row(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          Text('Pas de compte ? ', style: TextStyle(color: AppTheme.subtle)),
                          GestureDetector(
                            onTap: _loading ? null : () => context.push('/register'),
                            child: const Text('Créer un compte',
                                style: TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w800)),
                          ),
                        ],
                      ),
                      const SizedBox(height: 16),
                      // SORTIE VERS LE CATALOGUE (exigence App Store 5.1.1(v)).
                      //
                      // Sans elle, un visiteur qui touche « Panier » ou « Compte »
                      // atterrit ici SANS AUCUN RETOUR possible : l'app redevient, de
                      // fait, bloquée derrière la connexion — le motif exact du refus.
                      Center(
                        child: TextButton.icon(
                          onPressed: _loading ? null : () => context.go('/home'),
                          icon: const Icon(Icons.storefront_outlined, size: 18),
                          label: const Text('Parcourir sans compte',
                              style: TextStyle(fontWeight: FontWeight.w700)),
                          style: TextButton.styleFrom(foregroundColor: AppTheme.subtle),
                        ),
                      ),
                      const SizedBox(height: 24),
                      const _SecurePaymentsFooter(),
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

  void _showForgot(BuildContext context) {
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: AppTheme.surface,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (_) => _ForgotResetSheet(initialEmail: _email.text),
    );
  }
}

/// Feuille de réinitialisation en deux étapes : (1) e-mail → jeton, (2) jeton +
/// nouveau mot de passe. En mode dev (sans e-mailing) le jeton est pré-rempli.
class _ForgotResetSheet extends ConsumerStatefulWidget {
  const _ForgotResetSheet({required this.initialEmail});
  final String initialEmail;

  @override
  ConsumerState<_ForgotResetSheet> createState() => _ForgotResetSheetState();
}

class _ForgotResetSheetState extends ConsumerState<_ForgotResetSheet> {
  late final _email = TextEditingController(text: widget.initialEmail);
  final _token = TextEditingController();
  final _password = TextEditingController();
  int _step = 1;
  bool _loading = false;
  String? _error;

  @override
  void dispose() {
    _email.dispose();
    _token.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _request() async {
    if (!_email.text.contains('@')) {
      setState(() => _error = 'E-mail invalide');
      return;
    }
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      await ref.read(authApiProvider).forgotPassword(_email.text.trim());
      if (!mounted) return;
      // Le code arrive par e-mail : on passe à l'étape 2 (saisie du code + nouveau
      // mot de passe). Réponse identique qu'un compte existe ou non (anti-énumération).
      setState(() => _step = 2);
      AppNotify.info(context, 'Si un compte existe, un code de réinitialisation a été envoyé par e-mail.');
    } catch (e) {
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _reset() async {
    if (_password.text.length < 8) {
      setState(() => _error = '8 caractères minimum');
      return;
    }
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      await ref.read(authApiProvider).resetPassword(
            email: _email.text.trim(),
            token: _token.text.trim(),
            newPassword: _password.text,
          );
      if (!mounted) return;
      Navigator.pop(context);
      AppNotify.success(context, 'Mot de passe réinitialisé. Connectez-vous.');
    } catch (e) {
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.fromLTRB(20, 20, 20, sheetBottomInset(context)),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(_step == 1 ? 'Mot de passe oublié' : 'Nouveau mot de passe',
              style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w800)),
          const SizedBox(height: 6),
          Text(
            _step == 1
                ? 'Saisissez votre e-mail, nous vous enverrons un code de réinitialisation.'
                : 'Saisissez le code reçu et votre nouveau mot de passe.',
            style: TextStyle(color: AppTheme.subtle),
          ),
          const SizedBox(height: 16),
          if (_step == 1)
            TextField(
              controller: _email,
              keyboardType: TextInputType.emailAddress,
              decoration: const InputDecoration(hintText: 'exemple@email.com', prefixIcon: Icon(Icons.mail_outline)),
            )
          else ...[
            TextField(
              controller: _token,
              decoration: const InputDecoration(hintText: 'Code de réinitialisation', prefixIcon: Icon(Icons.vpn_key_outlined)),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _password,
              obscureText: true,
              decoration: const InputDecoration(hintText: 'Nouveau mot de passe (8+)', prefixIcon: Icon(Icons.lock_outline)),
            ),
          ],
          if (_error != null) ...[
            const SizedBox(height: 10),
            Text(_error!, style: const TextStyle(color: AppTheme.danger, fontSize: 13)),
          ],
          const SizedBox(height: 16),
          FilledButton(
            onPressed: _loading ? null : (_step == 1 ? _request : _reset),
            child: _loading
                ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                : Text(_step == 1 ? 'Envoyer le code' : 'Réinitialiser'),
          ),
        ],
      ),
    );
  }
}

class _FieldLabel extends StatelessWidget {
  const _FieldLabel(this.text);
  final String text;
  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 8, left: 2),
        child: Text(text, style: TextStyle(fontWeight: FontWeight.w700, color: AppTheme.ink)),
      );
}

class _ErrorBanner extends StatelessWidget {
  const _ErrorBanner(this.message);
  final String message;
  @override
  Widget build(BuildContext context) => Container(
        margin: const EdgeInsets.only(top: 8),
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
        decoration: BoxDecoration(color: AppTheme.danger.withValues(alpha: 0.08), borderRadius: BorderRadius.circular(10)),
        child: Row(children: [
          const Icon(Icons.error_outline, color: AppTheme.danger, size: 18),
          const SizedBox(width: 8),
          Expanded(child: Text(message, style: const TextStyle(color: AppTheme.danger, fontSize: 13))),
        ]),
      );
}

class _SecurePaymentsFooter extends StatelessWidget {
  const _SecurePaymentsFooter();
  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            _payChip(Icons.credit_card),
            const SizedBox(width: 10),
            _payChip(Icons.account_balance_wallet_outlined),
            const SizedBox(width: 10),
            _payChip(Icons.phone_android),
          ],
        ),
        const SizedBox(height: 12),
        Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(Icons.shield_outlined, size: 13, color: AppTheme.subtle),
            const SizedBox(width: 6),
            Text('CONNEXION SÉCURISÉE SSL',
                style: TextStyle(color: AppTheme.subtle.withValues(alpha: 0.9), fontSize: 11, fontWeight: FontWeight.w700, letterSpacing: 0.4)),
          ],
        ),
      ],
    );
  }

  Widget _payChip(IconData icon) => Container(
        width: 42,
        height: 28,
        decoration: BoxDecoration(color: AppTheme.bg, borderRadius: BorderRadius.circular(6), border: Border.all(color: AppTheme.line)),
        child: Icon(icon, size: 16, color: AppTheme.subtle),
      );
}
