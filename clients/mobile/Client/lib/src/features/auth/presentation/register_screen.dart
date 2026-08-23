import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../application/auth_controller.dart';
import '../data/auth_api.dart';

class RegisterScreen extends ConsumerStatefulWidget {
  const RegisterScreen({super.key});

  @override
  ConsumerState<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends ConsumerState<RegisterScreen> {
  final _form = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _email = TextEditingController();
  final _phone = TextEditingController();
  final _password = TextEditingController();

  String _dialCode = '+229'; // Bénin par défaut (marché cible)
  bool _obscure = true;
  bool _accepted = false;
  bool _loading = false;
  String? _error;
  double _strength = 0;

  static const _dialCodes = ['+229', '+225', '+221', '+228', '+233', '+234', '+237', '+33'];

  // Détecteurs de tape des deux liens légaux. Créés une fois et LIBÉRÉS dans
  // `dispose` : un `TapGestureRecognizer` construit dans `build` fuirait à chaque
  // reconstruction de l'écran — et il y en a une à chaque frappe du mot de passe.
  late final _termsTap = TapGestureRecognizer()..onTap = () => context.push('/terms');
  late final _privacyTap = TapGestureRecognizer()..onTap = () => context.push('/privacy');

  @override
  void dispose() {
    _name.dispose();
    _email.dispose();
    _phone.dispose();
    _password.dispose();
    _termsTap.dispose();
    _privacyTap.dispose();
    super.dispose();
  }

  void _onPassword(String v) {
    double s = 0;
    if (v.length >= 8) s += 0.34;
    if (RegExp(r'[A-Z]').hasMatch(v) || RegExp(r'[0-9]').hasMatch(v)) s += 0.33;
    if (RegExp(r'[^A-Za-z0-9]').hasMatch(v)) s += 0.33;
    setState(() => _strength = s.clamp(0, 1));
  }

  String get _strengthLabel {
    if (_strength >= 0.9) return 'Fort';
    if (_strength >= 0.6) return 'Moyen';
    if (_strength > 0) return 'Faible';
    return '';
  }

  Future<void> _submit() async {
    if (!_form.currentState!.validate()) return;
    if (!_accepted) {
      setState(() => _error = 'Veuillez accepter les conditions d’utilisation.');
      return;
    }
    final parts = _name.text.trim().split(RegExp(r'\s+'));
    final firstName = parts.first;
    final lastName = parts.length > 1 ? parts.sublist(1).join(' ') : '';
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final userId = await ref.read(authControllerProvider.notifier).register(
            firstName: firstName,
            lastName: lastName,
            email: _email.text.trim(),
            phoneNumber: _phone.text.trim().isEmpty ? null : '$_dialCode ${_phone.text.trim()}',
            password: _password.text,
          );
      if (!mounted) return;
      if (userId.isEmpty) {
        setState(() => _error = "Inscription impossible pour le moment.");
        return;
      }
      // Le compte est créé : on demande le code envoyé par e-mail.
      //
      // L'identifiant rendu par l'inscription n'est plus transmis : la
      // vérification se fait désormais sur l'ADRESSE. Il reste utile ici pour
      // constater que la création a bien eu lieu (le test juste au-dessus), et
      // s'arrête là.
      context.push(
        '/verify-email',
        extra: EmailVerifyArgs(email: _email.text.trim()),
      );
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
              child: Form(
                key: _form,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Align(
                      alignment: Alignment.centerLeft,
                      child: SoftCircleButton(icon: Icons.arrow_back, onTap: () => context.pop(), semanticLabel: 'Retour'),
                    ),
                    const SizedBox(height: 16),
                    Text('Créer un compte',
                        style: TextStyle(fontSize: 26, fontWeight: FontWeight.w800, color: AppTheme.ink)),
                    const SizedBox(height: 6),
                    Text('Rejoignez-nous pour profiter d’une expérience personnalisée.',
                        style: TextStyle(color: AppTheme.subtle)),
                    const SizedBox(height: 26),
                    const _Label('Nom complet'),
                    TextFormField(
                      controller: _name,
                      textCapitalization: TextCapitalization.words,
                      decoration: const InputDecoration(hintText: 'Jean Dupont', prefixIcon: Icon(Icons.person_outline)),
                      validator: (v) => (v == null || v.trim().isEmpty) ? 'Nom requis' : null,
                    ),
                    const SizedBox(height: 16),
                    const _Label('Email'),
                    TextFormField(
                      controller: _email,
                      keyboardType: TextInputType.emailAddress,
                      decoration: const InputDecoration(hintText: 'exemple@email.com', prefixIcon: Icon(Icons.mail_outline)),
                      validator: (v) => (v == null || !v.contains('@')) ? 'E-mail invalide' : null,
                    ),
                    const SizedBox(height: 16),
                    const _Label('Téléphone'),
                    Row(children: [
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 12),
                        height: 52,
                        decoration: BoxDecoration(
                          color: AppTheme.surface,
                          borderRadius: BorderRadius.circular(12),
                          border: Border.all(color: AppTheme.line),
                        ),
                        child: DropdownButtonHideUnderline(
                          child: DropdownButton<String>(
                            value: _dialCode,
                            items: _dialCodes
                                .map((c) => DropdownMenuItem(value: c, child: Text(c, style: const TextStyle(fontWeight: FontWeight.w600))))
                                .toList(),
                            onChanged: (v) => setState(() => _dialCode = v ?? _dialCode),
                          ),
                        ),
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        child: TextFormField(
                          controller: _phone,
                          keyboardType: TextInputType.phone,
                          decoration: const InputDecoration(hintText: '07 00 00 00 00'),
                          // Hors indicatif, un numéro béninois (+229) fait 10 chiffres.
                          validator: (v) {
                            final s = (v ?? '').trim();
                            if (s.isEmpty) return null; // le téléphone reste optionnel
                            final digits = s.replaceAll(RegExp(r'\D'), '');
                            if (_dialCode == '+229' && digits.length != 10) {
                              return '10 chiffres attendus';
                            }
                            return null;
                          },
                        ),
                      ),
                    ]),
                    const SizedBox(height: 16),
                    const _Label('Mot de passe'),
                    TextFormField(
                      controller: _password,
                      obscureText: _obscure,
                      onChanged: _onPassword,
                      decoration: InputDecoration(
                        hintText: '••••••••',
                        prefixIcon: const Icon(Icons.lock_outline),
                        suffixIcon: IconButton(
                          icon: Icon(_obscure ? Icons.visibility_outlined : Icons.visibility_off_outlined),
                          onPressed: () => setState(() => _obscure = !_obscure),
                        ),
                      ),
                      validator: (v) => (v == null || v.length < 8) ? '8 caractères minimum' : null,
                    ),
                    if (_strengthLabel.isNotEmpty) ...[
                      const SizedBox(height: 8),
                      Row(children: [
                        Expanded(
                          child: ClipRRect(
                            borderRadius: BorderRadius.circular(4),
                            child: LinearProgressIndicator(
                              value: _strength,
                              minHeight: 5,
                              backgroundColor: AppTheme.line,
                              valueColor: AlwaysStoppedAnimation(
                                  _strength >= 0.9 ? AppTheme.brandGreen : (_strength >= 0.6 ? AppTheme.promoOrange : AppTheme.danger)),
                            ),
                          ),
                        ),
                        const SizedBox(width: 10),
                        Text('Force : $_strengthLabel', style: TextStyle(fontSize: 12, color: AppTheme.subtle)),
                      ]),
                    ],
                    const SizedBox(height: 16),
                    Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      SizedBox(
                        width: 24,
                        height: 24,
                        child: Checkbox(
                          value: _accepted,
                          activeColor: AppTheme.brandGreen,
                          onChanged: (v) => setState(() => _accepted = v ?? false),
                        ),
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        // ─────────────────────────────────────────────────────────
                        // CES DEUX LIENS DOIVENT ÊTRE CLIQUABLES.
                        //
                        // Ils étaient stylés en vert gras — donc lus comme des liens —
                        // mais sans `recognizer` : parfaitement inertes. On demandait
                        // à l'utilisateur de cocher qu'il accepte des documents qu'il
                        // ne pouvait pas ouvrir.
                        //
                        // Les écrans existaient pourtant (/terms et /privacy) : rien
                        // dans l'application n'y menait. C'est un motif de rejet App
                        // Store, sur le terrain exact des deux refus précédents, et
                        // une non-conformité Play Data Safety.
                        // ─────────────────────────────────────────────────────────
                        child: Text.rich(TextSpan(
                          style: TextStyle(color: AppTheme.subtle, fontSize: 13),
                          children: [
                            const TextSpan(text: 'J’accepte les '),
                            TextSpan(
                              text: 'Conditions d’utilisation',
                              style: const TextStyle(
                                color: AppTheme.brandGreen,
                                fontWeight: FontWeight.w700,
                                decoration: TextDecoration.underline,
                              ),
                              recognizer: _termsTap,
                            ),
                            const TextSpan(text: ' et la '),
                            TextSpan(
                              text: 'Politique de confidentialité',
                              style: const TextStyle(
                                color: AppTheme.brandGreen,
                                fontWeight: FontWeight.w700,
                                decoration: TextDecoration.underline,
                              ),
                              recognizer: _privacyTap,
                            ),
                            const TextSpan(text: '.'),
                          ],
                        )),
                      ),
                    ]),
                    if (_error != null) ...[
                      const SizedBox(height: 10),
                      Text(_error!, style: const TextStyle(color: AppTheme.danger, fontSize: 13)),
                    ],
                    const SizedBox(height: 18),
                    FilledButton(
                      onPressed: _loading ? null : _submit,
                      child: _loading
                          ? const SizedBox(height: 22, width: 22, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                          : const Text('Créer mon compte'),
                    ),
                    const SizedBox(height: 18),
                    Row(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Text('Déjà inscrit ? ', style: TextStyle(color: AppTheme.subtle)),
                        GestureDetector(
                          onTap: () => context.pop(),
                          child: const Text('Se connecter', style: TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w800)),
                        ),
                      ],
                    ),
                    const SizedBox(height: 16),
                    Row(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Icon(Icons.shield_outlined, size: 13, color: AppTheme.subtle),
                        const SizedBox(width: 6),
                        Text('Vos données sont sécurisées',
                            style: TextStyle(color: AppTheme.subtle.withValues(alpha: 0.9), fontSize: 12)),
                      ],
                    ),
                    const SizedBox(height: 12),
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

class _Label extends StatelessWidget {
  const _Label(this.text);
  final String text;
  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 8, left: 2),
        child: Text(text, style: TextStyle(fontWeight: FontWeight.w700, color: AppTheme.ink)),
      );
}
