import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/commune_field.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../data/auth_api.dart';

/// Auto-inscription vendeur — étape 1.
///
/// Le vendeur saisit son identité, le nom de sa boutique et (optionnellement) les
/// informations de sa société. À la validation, le serveur crée le compte (ou
/// reconnaît un acheteur existant) et envoie un code à 6 chiffres. On enchaîne
/// alors sur l'écran de saisie du code, qui finalise la création de la boutique.
class RegisterScreen extends ConsumerStatefulWidget {
  const RegisterScreen({super.key});

  @override
  ConsumerState<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends ConsumerState<RegisterScreen> {
  final _form = GlobalKey<FormState>();

  final _shopName = TextEditingController();
  final _firstName = TextEditingController();
  final _lastName = TextEditingController();
  final _email = TextEditingController();
  final _phone = TextEditingController();
  final _password = TextEditingController();

  // Société (metadata, tout optionnel).
  final _legalName = TextEditingController();
  final _rccm = TextEditingController();
  final _ifu = TextEditingController();
  final _activity = TextEditingController();
  final _address = TextEditingController();
  String? _commune;
  final _managerName = TextEditingController();
  final _companyPhone = TextEditingController();

  bool _loading = false;
  bool _obscure = true;
  bool _showCompany = false;
  String? _error;

  @override
  void dispose() {
    for (final c in [
      _shopName, _firstName, _lastName, _email, _phone, _password,
      _legalName, _rccm, _ifu, _activity, _address, _managerName, _companyPhone,
    ]) {
      c.dispose();
    }
    super.dispose();
  }

  String? _t(TextEditingController c) => c.text.trim().isEmpty ? null : c.text.trim();

  CompanyInfo _company() => CompanyInfo(
        legalName: _t(_legalName),
        rccm: _t(_rccm),
        ifu: _t(_ifu),
        activity: _t(_activity),
        address: _t(_address),
        commune: _commune,
        managerName: _t(_managerName),
        phone: _t(_companyPhone),
      );

  Future<void> _submit() async {
    if (!_form.currentState!.validate()) return;
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      // ═══════════════════════════════════════════════════════════════════════
      // CET APPEL NE CRÉE PLUS LA BOUTIQUE, ET IL N'EN SAIT MÊME RIEN.
      //
      // Il visait `POST /seller/auth/register` du BFF du monolithe, qui rendait
      // un `SellerRegisterResult { userId, isNewAccount }` — ce type n'existe
      // plus, et le chemin non plus : la passerelle n'expose rien sous `/seller`.
      //
      // `POST /api/auth/register` crée le COMPTE dans identity-service et rend
      // son identifiant, point. À ce stade, le compte n'a que le rôle `Buyer` :
      // ni boutique, ni rôle vendeur. La boutique se crée APRÈS la connexion,
      // par `POST /api/merchants` (voir `AuthController.registerShop`).
      //
      // `isNewAccount` A DISPARU AVEC LE RESTE, ET C'ÉTAIT UN ORACLE.
      //
      // Le champ disait « cette adresse avait déjà un compte » à un visiteur non
      // authentifié : de quoi énumérer les inscrits de la plateforme. Le parcours
      // n'en avait besoin que pour adapter un libellé.
      //
      // L'identifiant renvoyé N'EST PAS TRANSMIS À L'ÉTAPE SUIVANTE : la
      // vérification se fait désormais PAR ADRESSE (`POST /api/auth/email/verify`,
      // contrat `{ email, code }`). On l'ignore donc sciemment plutôt que de le
      // promener dans le routeur pour rien.
      // ═══════════════════════════════════════════════════════════════════════
      await ref.read(authApiProvider).register(
            email: _email.text.trim(),
            password: _password.text,
            firstName: _firstName.text.trim(),
            lastName: _lastName.text.trim(),
            phoneNumber: _phone.text.trim(),
          );

      if (!mounted) return;
      final company = _company();
      context.push(
        '/verify',
        extra: SellerVerifyArgs(
          email: _email.text.trim(),
          shopName: _shopName.text.trim(),
          company: company.isEmpty ? null : company,
        ),
      );
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
        title: Text(l.authRegisterTitle),
      ),
      body: GlowBackground(
        child: SafeArea(
          top: false,
          child: Center(
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(24),
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 460),
                child: Form(
                  key: _form,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      const SizedBox(height: 8),
                      Text(
                        l.authRegisterHeadline,
                        style: TextStyle(fontSize: 22, fontWeight: FontWeight.w800, color: colors.ink),
                      ),
                      const SizedBox(height: 6),
                      Text(
                        l.authRegisterSubtitle,
                        style: TextStyle(color: colors.subtle, height: 1.5, fontSize: 13),
                      ),
                      const SizedBox(height: 24),

                      _Label(l.authRegisterShopNameLabel),
                      _Field(
                        controller: _shopName,
                        hint: l.authRegisterShopNameHint,
                        icon: Icons.storefront_outlined,
                        validator: (v) => (v == null || v.trim().length < 2) ? l.authRegisterShopNameRequired : null,
                      ),
                      const SizedBox(height: 18),

                      Row(children: [
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              _Label(l.authRegisterFirstNameLabel),
                              _Field(
                                controller: _firstName,
                                hint: 'Aïcha',
                                validator: (v) => (v == null || v.trim().isEmpty) ? l.authRegisterRequired : null,
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              _Label(l.authRegisterLastNameLabel),
                              _Field(
                                controller: _lastName,
                                hint: 'Diallo',
                                validator: (v) => (v == null || v.trim().isEmpty) ? l.authRegisterRequired : null,
                              ),
                            ],
                          ),
                        ),
                      ]),
                      const SizedBox(height: 18),

                      _Label(l.authRegisterEmailLabel),
                      _Field(
                        controller: _email,
                        hint: 'vendeur@boutique.com',
                        icon: Icons.mail_outline,
                        keyboardType: TextInputType.emailAddress,
                        validator: (v) => (v == null || !v.contains('@')) ? l.authRegisterEmailInvalid : null,
                      ),
                      const SizedBox(height: 18),

                      _Label(l.authRegisterPhoneLabel),
                      _Field(
                        controller: _phone,
                        hint: '+229 01 00 00 00',
                        icon: Icons.phone_outlined,
                        keyboardType: TextInputType.phone,
                        validator: (v) => (v == null || v.trim().length < 6) ? l.authRegisterPhoneRequired : null,
                      ),
                      const SizedBox(height: 18),

                      _Label(l.authRegisterPasswordLabel),
                      _Field(
                        controller: _password,
                        hint: '••••••••',
                        icon: Icons.lock_outline,
                        obscure: _obscure,
                        suffix: IconButton(
                          icon: Icon(_obscure ? Icons.visibility_outlined : Icons.visibility_off_outlined),
                          onPressed: () => setState(() => _obscure = !_obscure),
                        ),
                        validator: (v) => (v == null || v.length < 6) ? l.authRegisterPasswordMin : null,
                      ),

                      const SizedBox(height: 20),
                      // Section société repliable : facultative, on n'encombre pas
                      // l'inscription de base avec des champs administratifs.
                      _CompanyToggle(
                        expanded: _showCompany,
                        onTap: () => setState(() => _showCompany = !_showCompany),
                      ),
                      if (_showCompany) ...[
                        const SizedBox(height: 16),
                        _Label(l.authRegisterLegalNameLabel),
                        _Field(controller: _legalName, hint: 'SARL Ma Boutique'),
                        const SizedBox(height: 14),
                        Row(children: [
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [const _Label('RCCM'), _Field(controller: _rccm, hint: 'RB/COT/…')],
                            ),
                          ),
                          const SizedBox(width: 12),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [const _Label('IFU'), _Field(controller: _ifu, hint: '000…')],
                            ),
                          ),
                        ]),
                        const SizedBox(height: 14),
                        _Label(l.authRegisterActivityLabel),
                        _Field(controller: _activity, hint: l.authRegisterActivityHint),
                        const SizedBox(height: 14),
                        _Label(l.authRegisterAddressLabel),
                        _Field(controller: _address, hint: l.authRegisterAddressHint),
                        const SizedBox(height: 14),
                        _Label(l.authRegisterCityLabel),
                        CommuneField(
                          selectedCode: _commune,
                          onSelected: (code) => setState(() => _commune = code),
                          label: '',
                        ),
                        const SizedBox(height: 14),
                        _Label(l.authRegisterManagerLabel),
                        _Field(controller: _managerName, hint: l.authRegisterManagerHint),
                        const SizedBox(height: 14),
                        _Label(l.authRegisterCompanyPhoneLabel),
                        _Field(controller: _companyPhone, hint: '+229 …', keyboardType: TextInputType.phone),
                      ],

                      if (_error != null) ...[
                        const SizedBox(height: 16),
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
                            : Text(l.authRegisterContinue),
                      ),
                      const SizedBox(height: 12),
                      TextButton(
                        onPressed: _loading ? null : () => context.go('/login'),
                        child: Text(l.authRegisterHaveAccount),
                      ),
                      const SizedBox(height: 8),
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

class _CompanyToggle extends StatelessWidget {
  const _CompanyToggle({required this.expanded, required this.onTap});
  final bool expanded;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(12),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 8),
        child: Row(children: [
          Icon(Icons.business_outlined, size: 20, color: colors.subtle),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              l.authRegisterCompanyToggle,
              style: TextStyle(fontWeight: FontWeight.w700, color: colors.ink, fontSize: 14),
            ),
          ),
          Icon(expanded ? Icons.expand_less : Icons.expand_more, color: colors.subtle),
        ]),
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

class _Field extends StatelessWidget {
  const _Field({
    required this.controller,
    this.hint,
    this.icon,
    this.suffix,
    this.obscure = false,
    this.keyboardType,
    this.validator,
  });

  final TextEditingController controller;
  final String? hint;
  final IconData? icon;
  final Widget? suffix;
  final bool obscure;
  final TextInputType? keyboardType;
  final String? Function(String?)? validator;

  @override
  Widget build(BuildContext context) {
    return TextFormField(
      controller: controller,
      obscureText: obscure,
      autocorrect: false,
      keyboardType: keyboardType,
      decoration: InputDecoration(
        hintText: hint,
        prefixIcon: icon == null ? null : Icon(icon),
        suffixIcon: suffix,
      ),
      validator: validator,
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
