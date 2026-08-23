import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/network/api_exception.dart';
import '../../../core/security/biometric_service.dart';
import '../../../core/theme/app_theme.dart';
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

  /// Le champ MFA n'apparaît QUE si le serveur le réclame : l'afficher d'emblée
  /// ferait croire à tous les vendeurs qu'ils doivent saisir un code.
  bool _mfaRequired = false;
  String? _error;
  // Vrai quand le login échoue faute d'e-mail vérifié : on propose de renvoyer le code.
  bool _needsVerification = false;

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
      final email = _email.text.trim();
      final password = _password.text;
      await ref.read(authControllerProvider.notifier).login(
            email,
            password,
            mfaCode: _mfaRequired && _mfa.text.trim().isNotEmpty ? _mfa.text.trim() : null,
          );
      // Mémorise les identifiants pour la biométrie si le vendeur l'a demandé.
      if (_rememberBio && _bioAvailable) {
        await ref.read(biometricServiceProvider).enable(email, password);
      }
      // Succès : le routeur bascule tout seul (il écoute l'état d'auth).
    } on ApiException catch (e) {
      setState(() {
        _error = e.message;
        if (e.code == 'mfa_required') _mfaRequired = true;
        if (e.code == 'identity.auth.pending_approval') _needsVerification = true;
      });
    } catch (e) {
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  /// Renvoie un code de vérification puis ouvre l'écran de saisie du code.
  ///
  /// ═══════════════════════════════════════════════════════════════════════════
  /// ON N'APPREND PLUS RIEN SUR L'EXISTENCE DU COMPTE, ET C'EST LE POINT.
  ///
  /// Cette méthode appelait `authApi.resendCode(email)`, qui rendait l'`userId`
  /// du compte — puis affichait « aucun compte à vérifier » quand il revenait
  /// vide. Deux problèmes, et le second est le vrai :
  ///
  ///   • la méthode N'EXISTE PLUS : elle visait `/seller/auth/resend` du BFF du
  ///     monolithe. La passerelle expose `POST /api/auth/email/resend`, qui rend
  ///     204 et RIEN d'autre ;
  ///   • le message « aucun compte à vérifier » était un ORACLE
  ///     D'ÉNUMÉRATION sur une route anonyme : saisir des adresses en série
  ///     disait lesquelles ont un compte chez nous. C'est précisément pour cela
  ///     que le serveur a cessé de renvoyer l'identifiant.
  ///
  /// On enchaîne donc TOUJOURS sur l'écran de saisie du code, adresse inconnue
  /// comprise. La vraie parade au renvoi en boucle est le limiteur `otp` de la
  /// passerelle (5 essais / 5 min), pas un message d'écran.
  ///
  /// `shopName` PART VIDE, ET L'ÉCRAN SUIVANT LE DEMANDERA.
  ///
  /// On arrive ici depuis la CONNEXION : le nom de boutique n'a jamais été saisi.
  /// `VerifyCodeScreen` affiche alors son propre champ (`_needsShopName`).
  /// ═══════════════════════════════════════════════════════════════════════════
  Future<void> _resendCode() async {
    final l = AppLocalizations.of(context);
    final email = _email.text.trim();
    if (!email.contains('@')) {
      setState(() => _error = l.authLoginEnterEmail);
      return;
    }
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      await ref.read(authApiProvider).resendEmailCode(email);
      if (!mounted) return;
      setState(() => _needsVerification = false);
      context.push('/verify', extra: SellerVerifyArgs(email: email, shopName: ''));
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } catch (e) {
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  /// Déverrouille par biométrie, remplit le formulaire et se connecte.
  Future<void> _biometricLogin() async {
    final l = AppLocalizations.of(context);
    final creds =
        await ref.read(biometricServiceProvider).unlock(l.authLoginBiometricReason);
    if (creds == null || !mounted) return;
    _email.text = creds.email;
    _password.text = creds.password;
    await _submit();
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    // ═══════════════════════════════════════════════════════════════════════
    // ÉCRAN CONNEXION — maquette HBA Partner.
    //
    // La maquette annonce « Écran plein blanc, sans chrome ni bottom nav ».
    // D'où la disparition de `GlowBackground` : le dégradé qu'il peignait est
    // exactement le « chrome » que la maquette écarte.
    //
    // CONTENU ALIGNÉ À GAUCHE, PAS CENTRÉ.
    //
    // La maquette pose le logo, le sur-titre et le titre au bord gauche. Un
    // `Center` avec `TextAlign.center` — la version précédente — donne un écran
    // symétrique très différent, et surtout plus lent à lire : l'œil doit
    // retrouver le début de chaque ligne.
    // ═══════════════════════════════════════════════════════════════════════
    return Scaffold(
      backgroundColor: Colors.white,
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(24, 12, 24, 28),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 480),
            child: Form(
              key: _form,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const SizedBox(height: 24),
                  const Align(
                    alignment: Alignment.centerLeft,
                    child: BrandLogo(size: 56),
                  ),
                  const SizedBox(height: 28),

                  // Sur-titre : capitales espacées, comme la maquette.
                  Text(
                    l.authLoginBrandKicker,
                    style: TextStyle(
                      fontSize: 12,
                      fontWeight: FontWeight.w800,
                      letterSpacing: 1.4,
                      color: colors.subtle,
                    ),
                  ),
                  const SizedBox(height: 10),

                  Text(
                    l.authLoginTitle,
                    style: TextStyle(
                      fontSize: 30,
                      fontWeight: FontWeight.w800,
                      // Interligne resserré : le titre tient sur deux lignes dans
                      // la maquette. À 1.4 (défaut), les deux lignes s'écartent
                      // et le bloc cesse de se lire comme un seul titre.
                      height: 1.16,
                      color: colors.ink,
                    ),
                  ),
                  const SizedBox(height: 12),

                  Text(
                    l.authLoginTagline,
                    style: TextStyle(fontSize: 15, height: 1.45, color: colors.subtle),
                  ),
                  const SizedBox(height: 30),

                  _FieldLabel(l.authLoginEmailLabel),
                  TextFormField(
                    controller: _email,
                    keyboardType: TextInputType.emailAddress,
                    autocorrect: false,
                    // PAS D'ICÔNE DE PRÉFIXE : la maquette n'en montre aucune.
                    //
                    // Une icône décale le texte saisi vers la droite et casse
                    // l'alignement du champ avec le titre et le bouton.
                    decoration: const InputDecoration(hintText: 'hector@hbapartner.com'),

                    // ═══════════════════════════════════════════════════════════
                    // LE LIBELLÉ DIT « TÉLÉPHONE OU E-MAIL ». LA VALIDATION
                    //    N'ACCEPTE QUE L'E-MAIL, ET C'EST DÉLIBÉRÉ.
                    //
                    // La maquette élargit le champ au numéro de téléphone. Or
                    // identity-service ne sait résoudre un compte que par
                    // adresse : `IIdentityModuleApi` n'expose que
                    // `GetUserByEmailAsync`. Accepter un numéro ici enverrait
                    // l'utilisateur vers un échec serveur générique, après la
                    // saisie du mot de passe.
                    //
                    // Refuser tôt, avec un message clair, vaut mieux qu'un
                    // parcours qui semble marcher et casse à la dernière étape.
                    // À rouvrir dès qu'identity-service saura chercher par
                    // téléphone — c'est un changement de service, pas d'écran.
                    // ═══════════════════════════════════════════════════════════
                    validator: (v) => (v == null || !v.contains('@')) ? l.authLoginEmailInvalid : null,
                  ),
                  const SizedBox(height: 18),

                  _FieldLabel(l.authLoginPasswordLabel),
                  TextFormField(
                    controller: _password,
                    obscureText: _obscure,
                    decoration: InputDecoration(
                      hintText: '••••••••••',
                      suffixIcon: IconButton(
                        icon: Icon(_obscure ? Icons.visibility_outlined : Icons.visibility_off_outlined),
                        onPressed: () => setState(() => _obscure = !_obscure),
                        // Le bouton œil est la seule zone tactile du champ :
                        // il doit à lui seul atteindre le plancher de 48px.
                        constraints: const BoxConstraints(
                          minWidth: AppTheme.minTapTarget,
                          minHeight: AppTheme.minTapTarget,
                        ),
                      ),
                    ),
                    validator: (v) => (v == null || v.length < 6) ? l.authLoginPasswordMin : null,
                    onFieldSubmitted: (_) => _submit(),
                  ),

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
                            l.authLoginEnableBio(_bioLabel),
                            style: TextStyle(fontSize: 13, color: colors.subtle),
                          ),
                        ),
                      ],
                    ),

                  if (_mfaRequired) ...[
                    const SizedBox(height: 18),
                    _FieldLabel(l.authLoginMfaLabel),
                    TextFormField(
                      controller: _mfa,
                      keyboardType: TextInputType.number,
                      decoration: const InputDecoration(
                        hintText: '123456',
                        prefixIcon: Icon(Icons.shield_outlined),
                      ),
                    ),
                  ],

                  if (_error != null) ...[
                    const SizedBox(height: 14),
                    _ErrorBanner(_error!),
                  ],

                  if (_needsVerification) ...[
                    const SizedBox(height: 12),
                    OutlinedButton.icon(
                      onPressed: _loading ? null : _resendCode,
                      icon: const Icon(Icons.mark_email_unread_outlined),
                      label: Text(l.authLoginResendVerification),
                    ),
                  ],

                  const SizedBox(height: 26),
                  SizedBox(
                    height: AppTheme.primaryButtonHeight,
                    child: FilledButton(
                      onPressed: _loading ? null : _submit,
                      child: _loading
                          ? const SizedBox(
                              width: 22,
                              height: 22,
                              child: CircularProgressIndicator(strokeWidth: 2.4, color: Colors.white),
                            )
                          : Text(l.authLoginSignIn),
                    ),
                  ),

                  const SizedBox(height: 6),

                  // Action secondaire 1. `TextButton` garantit à lui seul les
                  // 48px de hauteur tactile ; un `GestureDetector` sur le texte
                  // n'en ferait que 20 et serait difficile à viser au pouce.
                  TextButton(
                    onPressed: _loading ? null : () => context.push('/forgot-password'),
                    style: TextButton.styleFrom(
                      minimumSize: const Size.fromHeight(AppTheme.minTapTarget),
                      foregroundColor: colors.subtle,
                    ),
                    child: Text(l.authLoginForgotPassword),
                  ),

                  // LA CONNEXION BIOMÉTRIQUE N'EST PAS SUR LA MAQUETTE.
                  //
                  // Celle-ci annonce « un seul CTA primaire, deux actions
                  // secondaires » — soit « Mot de passe oublié ? » et « Créer un
                  // compte partenaire ». La biométrie ferait une troisième.
                  //
                  // Elle est CONSERVÉE quand même : c'est une fonctionnalité qui
                  // marche, adossée à `local_auth`, et qu'un partenaire qui l'a
                  // activée utilise à chaque ouverture. La supprimer parce
                  // qu'une maquette ne la montre pas serait retirer du code
                  // d'authentification en état de marche sur un argument de mise
                  // en page. Elle ne s'affiche d'ailleurs QUE si l'appareil la
                  // gère ET que le partenaire l'a activée — donc jamais sur la
                  // première connexion, qui est ce que la maquette représente.
                  //
                  // À trancher avec le design, pas à supprimer en silence.
                  if (_bioAvailable && _bioEnabled) ...[
                    const SizedBox(height: 4),
                    OutlinedButton.icon(
                      onPressed: _loading ? null : _biometricLogin,
                      style: OutlinedButton.styleFrom(
                        minimumSize: const Size.fromHeight(AppTheme.minTapTarget),
                      ),
                      icon: Icon(_bioLabel == 'Face ID' ? Icons.face_outlined : Icons.fingerprint,
                          size: 20),
                      label: Text(l.authLoginSignInWithBio(_bioLabel)),
                    ),
                  ],

                  const SizedBox(height: 8),
                  Divider(color: colors.line, height: 1),
                  const SizedBox(height: 8),

                  // Action secondaire 2. Auto-inscription depuis l'app : la
                  // boutique ne pourra publier qu'après validation du profil (KYB).
                  TextButton(
                    onPressed: _loading ? null : () => context.push('/register'),
                    style: TextButton.styleFrom(
                      minimumSize: const Size.fromHeight(AppTheme.minTapTarget),
                      foregroundColor: AppTheme.brandGreen,
                      textStyle: const TextStyle(fontWeight: FontWeight.w700),
                    ),
                    child: Text(l.authLoginCreateAccount),
                  ),
                  const SizedBox(height: 12),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _FieldLabel extends StatelessWidget {
  const _FieldLabel(this.text);
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
        Expanded(
          child: Text(message, style: const TextStyle(color: AppTheme.danger, fontSize: 13)),
        ),
      ]),
    );
  }
}
