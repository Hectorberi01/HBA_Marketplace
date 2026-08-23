import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../application/auth_controller.dart';
import '../data/auth_api.dart';

/// Auto-inscription vendeur — étape 2 : saisie du code reçu par e-mail.
///
/// CE N'EST PLUS LA DERNIÈRE ÉTAPE, MALGRÉ CE QUE DIT LE BOUTON.
///
/// L'ancien parcours finissait ici : `/seller/auth/verify` validait le code,
/// créait la boutique et attribuait le rôle vendeur d'un coup. Sur la plateforme
/// HBA, ces trois gestes vivent dans deux services et exigent une session :
/// l'écran valide l'adresse, met le nom de boutique de côté
/// (`pendingShopProvider`), et c'est la CONNEXION qui suit qui crée la boutique.
class VerifyCodeScreen extends ConsumerStatefulWidget {
  const VerifyCodeScreen({super.key, required this.args});

  final SellerVerifyArgs args;

  @override
  ConsumerState<VerifyCodeScreen> createState() => _VerifyCodeScreenState();
}

class _VerifyCodeScreenState extends ConsumerState<VerifyCodeScreen> {
  final _code = TextEditingController();
  // Saisi ici uniquement quand on arrive depuis l'écran de connexion (reprise
  // après abandon) : dans ce cas le nom de boutique n'a pas été transmis.
  final _shopName = TextEditingController();
  bool _loading = false;
  bool _resending = false;
  String? _error;

  bool get _needsShopName => widget.args.shopName.trim().isEmpty;

  @override
  void dispose() {
    _code.dispose();
    _shopName.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final l = AppLocalizations.of(context);
    final code = _code.text.trim();
    if (code.length < 6) {
      setState(() => _error = l.authVerifyEnterCode);
      return;
    }
    // Nom de boutique : transmis à l'inscription, ou saisi ici quand on reprend
    // depuis l'écran de connexion.
    final shopName = _needsShopName ? _shopName.text.trim() : widget.args.shopName;
    if (shopName.isEmpty) {
      setState(() => _error = l.authVerifyEnterShopName);
      return;
    }
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      // ═══════════════════════════════════════════════════════════════════════
      // CETTE ÉTAPE NE CRÉE PLUS LA BOUTIQUE. ELLE VALIDE UNE ADRESSE.
      //
      // `authApi.verify(userId:, code:, shopName:, company:)` n'existe plus : la
      // méthode visait `/seller/auth/verify` du BFF du monolithe, qui validait le
      // code, CRÉAIT la boutique et attribuait le rôle vendeur en un seul appel.
      // La passerelle n'expose rien sous `/seller`, et aucune route HBA ne fait
      // ces trois choses ensemble.
      //
      // `POST /api/auth/email/verify` marque l'adresse comme vérifiée, et c'est
      // tout : ni boutique, ni rôle. Il prend `{ email, code }` — l'`userId` n'y
      // figure plus, la vérification se faisant PAR ADRESSE.
      //
      // La boutique se crée par `POST /api/merchants`, qui exige une SESSION.
      // On dépose donc le nom saisi dans `pendingShopProvider`, et la connexion
      // qui suit le consomme (`AuthController._createPendingShopIfAny`). Sans ce
      // relais, le nom de boutique disparaissait entre les deux écrans et le
      // compte restait acheteur — 403 sur toute l'application vendeur.
      // ═══════════════════════════════════════════════════════════════════════
      await ref.read(authApiProvider).confirmEmail(email: widget.args.email, code: code);

      final company = widget.args.company;
      ref.read(pendingShopProvider.notifier).state = PendingShop(
        shopName: shopName,
        metadata: (company == null || company.isEmpty) ? null : company.toJson(),
      );

      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(l.authVerifyAccountCreated)),
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
      // ═══════════════════════════════════════════════════════════════════════
      // ON NE RÉINSCRIT PLUS LE COMPTE POUR LUI RENVOYER UN CODE.
      //
      // Cette méthode rappelait `register()` avec un mot de passe et un état
      // civil VIDES, en pariant sur un comportement du BFF du monolithe (« un
      // compte existant reçoit un nouveau code, le mot de passe est ignoré »).
      // Sur `POST /api/auth/register`, ce pari échoue : `RegisterRequest`
      // déclare `PhoneNumber` et `Password` non nullables, la validation refuse
      // les chaînes vides, et le vendeur recevait « données invalides » sans
      // comprendre pourquoi un bouton « Renvoyer le code » lui parle de son
      // numéro de téléphone.
      //
      // La route dédiée existe : `POST /api/auth/email/resend`. Elle répond 204
      // dans TOUS les cas — adresse inconnue, compte déjà vérifié, demande trop
      // rapprochée — pour ne rien révéler sur l'existence du compte. Le limiteur
      // `otp` de la passerelle (5 essais / 5 min) est la vraie parade au renvoi
      // en boucle ; le succès affiché ne prouve donc pas qu'un e-mail est parti.
      // ═══════════════════════════════════════════════════════════════════════
      await ref.read(authApiProvider).resendEmailCode(widget.args.email);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(l.authVerifyCodeResent)),
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
        title: Text(l.authVerifyTitle),
      ),
      body: GlowBackground(
        child: SafeArea(
          top: false,
          child: Center(
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(28),
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 420),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const SizedBox(height: 8),
                    const Icon(Icons.mark_email_read_outlined, size: 56, color: AppTheme.brandGreen),
                    const SizedBox(height: 20),
                    Text(
                      l.authVerifyHeadline,
                      textAlign: TextAlign.center,
                      style: TextStyle(fontSize: 22, fontWeight: FontWeight.w800, color: colors.ink),
                    ),
                    const SizedBox(height: 8),
                    Text.rich(
                      TextSpan(
                        text: l.authVerifySentTo,
                        style: TextStyle(color: colors.subtle, height: 1.5),
                        children: [
                          TextSpan(
                            text: widget.args.email,
                            style: TextStyle(color: colors.ink, fontWeight: FontWeight.w700),
                          ),
                        ],
                      ),
                      textAlign: TextAlign.center,
                    ),
                    const SizedBox(height: 28),

                    // Nom de boutique — affiché seulement quand on reprend depuis
                    // l'écran de connexion (il n'a pas été saisi à l'inscription).
                    if (_needsShopName) ...[
                      TextField(
                        controller: _shopName,
                        textCapitalization: TextCapitalization.words,
                        decoration: InputDecoration(
                          labelText: l.authVerifyShopNameLabel,
                          hintText: l.authVerifyShopNameHint,
                        ),
                      ),
                      const SizedBox(height: 16),
                    ],

                    TextField(
                      controller: _code,
                      keyboardType: TextInputType.number,
                      textAlign: TextAlign.center,
                      maxLength: 6,
                      autofocus: true,
                      inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                      style: TextStyle(
                        fontSize: 30,
                        fontWeight: FontWeight.w800,
                        letterSpacing: 10,
                        color: colors.ink,
                      ),
                      decoration: InputDecoration(
                        counterText: '',
                        hintText: '••••••',
                        hintStyle: TextStyle(letterSpacing: 10, color: colors.subtle),
                      ),
                      onSubmitted: (_) => _submit(),
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
                          : Text(l.authVerifyValidate),
                    ),
                    const SizedBox(height: 12),
                    TextButton(
                      onPressed: _resending ? null : _resend,
                      child: Text(_resending ? l.authVerifySending : l.authVerifyResend),
                    ),
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
