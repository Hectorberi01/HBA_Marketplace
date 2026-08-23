import 'dart:io';

import 'package:cached_network_image/cached_network_image.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/identity/seller_identity.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/commune_field.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../shop_data.dart';

class ShopScreen extends ConsumerWidget {
  const ShopScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    final shop = ref.watch(shopProvider);

    return Scaffold(
      appBar: AppBar(title: Text(l.shopTitle)),
      body: shop.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(message: e.toString(), onRetry: () => ref.invalidate(shopProvider)),
        data: (s) => RefreshIndicator(
          onRefresh: () async => ref.invalidate(shopProvider),
          child: ListView(
            padding: const EdgeInsets.only(bottom: 32),
            children: [
              const SizedBox(height: 16),
              CardSection(
                padding: const EdgeInsets.all(16),
                child: Row(
                  children: [
                    GestureDetector(
                      onTap: () => _changeLogo(context, ref, s),
                      child: Stack(
                        clipBehavior: Clip.none,
                        children: [
                          ClipRRect(
                            borderRadius: BorderRadius.circular(14),
                            child: SizedBox(
                              width: 56,
                              height: 56,
                              child: s.logoUrl == null
                                  ? Container(
                                      color: colors.softGreen,
                                      alignment: Alignment.center,
                                      child: const Icon(Icons.storefront, color: AppTheme.brandGreen),
                                    )
                                  : CachedNetworkImage(imageUrl: s.logoUrl!, fit: BoxFit.cover),
                            ),
                          ),
                          Positioned(
                            right: -4,
                            bottom: -4,
                            child: Container(
                              padding: const EdgeInsets.all(4),
                              decoration: BoxDecoration(
                                color: AppTheme.brandGreen,
                                shape: BoxShape.circle,
                                border: Border.all(color: Colors.white, width: 2),
                              ),
                              child: const Icon(Icons.photo_camera, size: 12, color: Colors.white),
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(s.shopName,
                              style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w800)),
                          const SizedBox(height: 6),
                          StatusPill.kyb(l, s.kybStatus),
                        ],
                      ),
                    ),
                    IconButton(
                      onPressed: () => _editProfile(context, ref, s),
                      icon: Icon(Icons.edit_outlined, color: colors.subtle),
                    ),
                  ],
                ),
              ),

              if (s.description.isNotEmpty) ...[
                const SizedBox(height: 12),
                CardSection(
                  padding: const EdgeInsets.all(16),
                  child: Text(s.description,
                      style: TextStyle(fontSize: 13, height: 1.5, color: colors.ink)),
                ),
              ],

              SectionHeader(title: l.shopCompanyInfo),
              CardSection(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    if (s.metadata == null || s.metadata!.isEmpty) ...[
                      Text(
                        l.shopCompanyInfoEmpty,
                        style: TextStyle(fontSize: 13, color: colors.ink, height: 1.4),
                      ),
                      const SizedBox(height: 12),
                      FilledButton(
                        onPressed: () => _editCompany(context, ref, s.metadata),
                        child: Text(l.shopFillIn),
                      ),
                    ] else ...[
                      if (s.metadata!.legalName != null)
                        KeyValueRow(label: l.shopLegalName, value: s.metadata!.legalName!),
                      if (s.metadata!.rccm != null) KeyValueRow(label: 'RCCM', value: s.metadata!.rccm!),
                      if (s.metadata!.ifu != null) KeyValueRow(label: 'IFU', value: s.metadata!.ifu!),
                      if (s.metadata!.activity != null)
                        KeyValueRow(label: l.shopActivity, value: s.metadata!.activity!),
                      if (s.metadata!.address != null)
                        KeyValueRow(label: l.shopAddress, value: s.metadata!.address!),
                      // Le LIBELLÉ, pas le code : « Abomey-Calavi », pas « abomey-calavi ».
                      if (s.metadata!.communeName != null && s.metadata!.communeName!.isNotEmpty)
                        KeyValueRow(label: l.shopCity, value: s.metadata!.communeName!),
                      if (s.metadata!.managerName != null)
                        KeyValueRow(label: l.shopManager, value: s.metadata!.managerName!),
                      if (s.metadata!.phone != null) KeyValueRow(label: l.shopPhone, value: s.metadata!.phone!),
                      const SizedBox(height: 12),
                      OutlinedButton(
                        onPressed: () => _editCompany(context, ref, s.metadata),
                        child: Text(l.shopEdit),
                      ),
                    ],
                  ],
                ),
              ),

              SectionHeader(title: l.shopPayoutAccount),
              CardSection(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    if (s.hasPayoutAccount) ...[
                      KeyValueRow(label: l.shopOperator, value: payoutProviderLabel(l, s.payout!.provider)),
                      KeyValueRow(label: l.shopNumber, value: _mask(s.payout!.accountNumber)),
                      KeyValueRow(label: l.shopAccountHolder, value: s.payout!.accountName),
                      const SizedBox(height: 12),
                      OutlinedButton(
                        onPressed: () => _editPayout(context, ref, s.payout),
                        child: Text(l.shopEdit),
                      ),
                    ] else ...[
                      // Sans compte de versement, l'argent gagné reste bloqué : ce
                      // n'est pas un détail de profil, c'est un préalable au retrait.
                      Text(
                        l.shopNoPayoutAccount,
                        style: TextStyle(fontSize: 13, color: colors.ink, height: 1.4),
                      ),
                      const SizedBox(height: 12),
                      FilledButton(
                        onPressed: () => _editPayout(context, ref, null),
                        child: Text(l.shopSaveMyAccount),
                      ),
                    ],
                  ],
                ),
              ),

              SectionHeader(
                title: l.shopDocumentsKyb,
                actionLabel: l.shopAdd,
                onAction: () => _addDocument(context),
              ),
              if (s.documents.isEmpty)
                CardSection(
                  padding: const EdgeInsets.all(16),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      // La vérification conditionne l'activation de la boutique :
                      // sans elle, le vendeur reste bloqué sans comprendre pourquoi.
                      Text(
                        l.shopNoDocuments,
                        style: TextStyle(fontSize: 13, color: colors.ink, height: 1.4),
                      ),
                      const SizedBox(height: 12),
                      FilledButton.icon(
                        onPressed: () => _addDocument(context),
                        icon: const Icon(Icons.upload_file_outlined, size: 18),
                        label: Text(l.shopUploadDocument),
                      ),
                    ],
                  ),
                )
              else
                CardSection(
                  child: Column(
                    children: [
                      for (var i = 0; i < s.documents.length; i++) ...[
                        if (i > 0) Divider(height: 1, color: colors.line),
                        _DocumentTile(document: s.documents[i]),
                      ],
                    ],
                  ),
                ),
              Padding(
                padding: const EdgeInsets.fromLTRB(20, 10, 20, 0),
                child: Text(
                  l.shopDocumentsSecure,
                  style: TextStyle(fontSize: 11, color: colors.subtle, height: 1.4),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  /// Masque le numéro : un écran de boutique se montre, se photographie, se
  /// partage. On n'affiche que la fin, suffisante pour vérifier.
  static String _mask(String account) {
    if (account.length <= 4) return account;
    return '••••${account.substring(account.length - 4)}';
  }

  void _editProfile(BuildContext context, WidgetRef ref, Shop shop) {
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (_) => _ProfileSheet(shop: shop),
    );
  }

  /// CHANGER LE LOGO RENVOIE AUSSI LE NOM DE LA BOUTIQUE.
  ///
  /// Il n'y a plus de route `/shop/logo` : le fichier va sur media-service, puis
  /// son URL est posée par `PUT /api/merchants/{sellerId}/profile` — une mise à
  /// jour de PROFIL, où `shopName` est obligatoire. L'omettre effacerait le nom
  /// en changeant une image.
  Future<void> _changeLogo(BuildContext context, WidgetRef ref, Shop shop) async {
    final l = AppLocalizations.of(context);
    final result = await FilePicker.platform.pickFiles(type: FileType.image, withData: false);
    final path = result?.files.single.path;
    if (path == null) return;
    try {
      // LE `sellerId` VIENT DU SOCLE, PAS DE L'ÉCRAN : les routes
      // `/api/merchants/{sellerId}/…` comparent l'identifiant de l'URL au
      // vendeur du jeton et répondent 403 sinon.
      final sellerId = await ref.read(requiredSellerIdProvider.future);
      await ref.read(shopApiProvider).uploadLogo(
            sellerId,
            File(path),
            shopName: shop.shopName,
            description: shop.description,
          );
      ref.invalidate(shopProvider);
      if (context.mounted) AppNotify.success(context, l.shopLogoUpdated);
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
    }
  }

  void _editCompany(BuildContext context, WidgetRef ref, CompanyInfo? info) {
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (_) => _CompanyInfoSheet(info: info),
    );
  }

  void _editPayout(BuildContext context, WidgetRef ref, PayoutAccount? account) {
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (_) => _PayoutSheet(account: account),
    );
  }

  void _addDocument(BuildContext context) {
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (_) => const _KybSheet(),
    );
  }
}

/// Une pièce déposée : type, date, statut, et suppression tant qu'elle n'est pas
/// validée. Une pièce VÉRIFIÉE ne se supprime pas depuis l'app — elle fait foi
/// pour l'activation de la boutique.
class _DocumentTile extends ConsumerWidget {
  const _DocumentTile({required this.document});
  final KybDocument document;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    return ListTile(
      dense: true,
      leading: Icon(
        document.isRejected ? Icons.error_outline : Icons.description_outlined,
        color: document.isRejected ? AppTheme.danger : colors.subtle,
      ),
      title: Text(kybTypeLabel(l, document.type),
          style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14)),
      subtitle: Text(
        document.isRejected
            ? l.shopDocumentRejected
            : Format.date(document.uploadedAt),
        style: TextStyle(
          fontSize: 12,
          color: document.isRejected ? AppTheme.danger : colors.subtle,
        ),
      ),
      trailing: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          StatusPill.kyb(l, document.status),
          if (!document.isVerified)
            IconButton(
              tooltip: l.shopDelete,
              icon: Icon(Icons.delete_outline, size: 20, color: colors.subtle),
              onPressed: () => _delete(context, ref),
            ),
        ],
      ),
    );
  }

  Future<void> _delete(BuildContext context, WidgetRef ref) async {
    final l = AppLocalizations.of(context);
    final ok = await showDialog<bool>(
      context: context,
      builder: (dialogContext) {
        final l = AppLocalizations.of(dialogContext);
        return AlertDialog(
          title: Text(l.shopDeleteDocumentTitle),
          content: Text(l.shopDeleteDocumentBody(kybTypeLabel(l, document.type))),
          actions: [
            TextButton(onPressed: () => Navigator.pop(dialogContext, false), child: Text(l.shopCancel)),
            FilledButton(
              style: FilledButton.styleFrom(backgroundColor: AppTheme.danger),
              onPressed: () => Navigator.pop(dialogContext, true),
              child: Text(l.shopDelete),
            ),
          ],
        );
      },
    );
    if (ok != true || !context.mounted) return;

    try {
      final sellerId = await ref.read(requiredSellerIdProvider.future);
      await ref.read(shopApiProvider).removeKybDocument(sellerId, document.id);
      ref.invalidate(shopProvider);
      if (context.mounted) AppNotify.success(context, l.shopDocumentDeleted);
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
    }
  }
}

/// Dépôt d'une pièce : type + fichier (PDF ou photo).
class _KybSheet extends ConsumerStatefulWidget {
  const _KybSheet();

  @override
  ConsumerState<_KybSheet> createState() => _KybSheetState();
}

class _KybSheetState extends ConsumerState<_KybSheet> {
  String _type = kKybTypes.first.value;
  File? _file;
  String? _fileError;
  bool _saving = false;

  Future<void> _pick() async {
    final result = await FilePicker.platform.pickFiles(
      type: FileType.custom,
      allowedExtensions: kKybExtensions,
      withData: false,
    );
    final path = result?.files.single.path;
    if (path == null) return;

    final file = File(path);
    final size = await file.length();

    // On applique ICI les règles du serveur (10 Mo, formats autorisés) : faire
    // téléverser un gros fichier sur un réseau mobile pour le voir refusé à
    // l'arrivée est la meilleure façon de faire abandonner un vendeur.
    if (size > kKybMaxBytes) {
      setState(() {
        _file = null;
        _fileError = AppLocalizations.of(context).shopFileTooLarge;
      });
      return;
    }

    setState(() {
      _file = file;
      _fileError = null;
    });
  }

  Future<void> _submit() async {
    final l = AppLocalizations.of(context);
    if (_file == null) {
      setState(() => _fileError = l.shopChooseFileError);
      return;
    }

    setState(() => _saving = true);
    try {
      final sellerId = await ref.read(requiredSellerIdProvider.future);
      await ref
          .read(shopApiProvider)
          .uploadKybDocument(sellerId, file: _file!, type: _type);
      ref.invalidate(shopProvider);
      if (mounted) {
        Navigator.pop(context);
        AppNotify.success(context, l.shopDocumentSent);
      }
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    final name = _file?.path.split(Platform.pathSeparator).last;

    return Padding(
      padding: sheetPadding(context),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const SheetHandle(),
          Text(l.shopKybTitle,
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: colors.ink)),
          const SizedBox(height: 6),
          Text(
            l.shopKybSubtitle,
            style: TextStyle(fontSize: 12, color: colors.subtle, height: 1.45),
          ),
          const SizedBox(height: 18),

          AppDropdown<String>(
            value: _type,
            label: l.shopDocumentType,
            options: kybTypeOptions(l),
            onChanged: (v) => setState(() => _type = v ?? _type),
          ),
          const SizedBox(height: 16),

          InkWell(
            onTap: _saving ? null : _pick,
            borderRadius: BorderRadius.circular(14),
            child: Container(
              padding: const EdgeInsets.symmetric(vertical: 22, horizontal: 16),
              decoration: BoxDecoration(
                color: colors.bg,
                borderRadius: BorderRadius.circular(14),
                border: Border.all(
                  color: _fileError != null ? AppTheme.danger : colors.line,
                ),
              ),
              child: Column(
                children: [
                  Icon(
                    _file == null ? Icons.upload_file_outlined : Icons.check_circle,
                    size: 28,
                    color: _file == null ? colors.subtle : AppTheme.brandGreen,
                  ),
                  const SizedBox(height: 10),
                  Text(
                    name ?? l.shopChooseFile,
                    textAlign: TextAlign.center,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.w700,
                      color: _file == null ? colors.ink : AppTheme.brandGreen,
                    ),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    l.shopFileFormats,
                    style: TextStyle(fontSize: 11, color: colors.subtle),
                  ),
                ],
              ),
            ),
          ),

          if (_fileError != null) ...[
            const SizedBox(height: 8),
            Text(_fileError!, style: const TextStyle(fontSize: 12, color: AppTheme.danger)),
          ],

          const SizedBox(height: 20),
          FilledButton(
            onPressed: (_saving || _file == null) ? null : _submit,
            child: _saving
                ? const SizedBox(
                    width: 22,
                    height: 22,
                    child: CircularProgressIndicator(strokeWidth: 2.4, color: Colors.white))
                : Text(l.shopSubmitForVerification),
          ),
        ],
      ),
    );
  }
}

class _ProfileSheet extends ConsumerStatefulWidget {
  const _ProfileSheet({required this.shop});
  final Shop shop;

  @override
  ConsumerState<_ProfileSheet> createState() => _ProfileSheetState();
}

class _ProfileSheetState extends ConsumerState<_ProfileSheet> {
  late final TextEditingController _name = TextEditingController(text: widget.shop.shopName);
  late final TextEditingController _description = TextEditingController(text: widget.shop.description);
  bool _saving = false;

  @override
  void dispose() {
    _name.dispose();
    _description.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final l = AppLocalizations.of(context);
    if (_name.text.trim().isEmpty) {
      AppNotify.error(context, l.shopNameRequired);
      return;
    }
    setState(() => _saving = true);
    try {
      final sellerId = await ref.read(requiredSellerIdProvider.future);
      await ref.read(shopApiProvider).updateProfile(
            sellerId,
            shopName: _name.text.trim(),
            description: _description.text.trim(),
            logoUrl: widget.shop.logoUrl,
          );
      ref.invalidate(shopProvider);
      if (mounted) {
        Navigator.pop(context);
        AppNotify.success(context, l.shopShopUpdated);
      }
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    return Padding(
      padding: sheetPadding(context),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const SheetHandle(),
          Text(l.shopProfileTitle,
              style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800, color: colors.ink)),
          const SizedBox(height: 18),
          TextField(controller: _name, decoration: InputDecoration(labelText: l.shopShopName)),
          const SizedBox(height: 14),
          TextField(
            controller: _description,
            maxLines: 3,
            decoration: InputDecoration(labelText: l.shopDescription),
          ),
          const SizedBox(height: 20),
          FilledButton(
            onPressed: _saving ? null : _save,
            child: _saving
                ? const SizedBox(
                    width: 22,
                    height: 22,
                    child: CircularProgressIndicator(strokeWidth: 2.4, color: Colors.white))
                : Text(l.shopSave),
          ),
        ],
      ),
    );
  }
}

class _CompanyInfoSheet extends ConsumerStatefulWidget {
  const _CompanyInfoSheet({this.info});
  final CompanyInfo? info;

  @override
  ConsumerState<_CompanyInfoSheet> createState() => _CompanyInfoSheetState();
}

class _CompanyInfoSheetState extends ConsumerState<_CompanyInfoSheet> {
  late final _legalName = TextEditingController(text: widget.info?.legalName ?? '');
  late final _rccm = TextEditingController(text: widget.info?.rccm ?? '');
  late final _ifu = TextEditingController(text: widget.info?.ifu ?? '');
  late final _activity = TextEditingController(text: widget.info?.activity ?? '');
  late final _address = TextEditingController(text: widget.info?.address ?? '');
  late String? _commune = widget.info?.commune;
  late final _manager = TextEditingController(text: widget.info?.managerName ?? '');
  late final _phone = TextEditingController(text: widget.info?.phone ?? '');
  bool _saving = false;

  @override
  void dispose() {
    for (final c in [_legalName, _rccm, _ifu, _activity, _address, _manager, _phone]) {
      c.dispose();
    }
    super.dispose();
  }

  String? _t(TextEditingController c) => c.text.trim().isEmpty ? null : c.text.trim();

  Future<void> _save() async {
    final l = AppLocalizations.of(context);
    setState(() => _saving = true);
    try {
      final sellerId = await ref.read(requiredSellerIdProvider.future);
      await ref.read(shopApiProvider).updateMetadata(
          sellerId,
          CompanyInfo(
            legalName: _t(_legalName),
            rccm: _t(_rccm),
            ifu: _t(_ifu),
            activity: _t(_activity),
            address: _t(_address),
            commune: _commune,
            managerName: _t(_manager),
            phone: _t(_phone),
          ));
      ref.invalidate(shopProvider);
      if (mounted) {
        // Toast AVANT le pop : après pop, le contexte de la feuille est mort.
        AppNotify.success(context, l.shopCompanyInfoUpdated);
        Navigator.pop(context);
      }
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Widget _field(TextEditingController c, String label, {TextInputType? keyboard}) => Padding(
        padding: const EdgeInsets.only(bottom: 14),
        child: TextField(
          controller: c,
          keyboardType: keyboard,
          decoration: InputDecoration(labelText: label),
        ),
      );

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    return Padding(
      padding: sheetPadding(context, top: 18),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SheetHandle(),
            Text(l.shopCompanyInfo,
                style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800, color: colors.ink)),
            const SizedBox(height: 6),
            Text(
              l.shopAllFieldsOptional,
              style: TextStyle(fontSize: 12, color: colors.subtle),
            ),
            const SizedBox(height: 16),
            _field(_legalName, l.shopLegalName),
            _field(_rccm, 'RCCM'),
            _field(_ifu, 'IFU'),
            _field(_activity, l.shopActivity),
            _field(_address, l.shopAddress),
            // Liste fermée, comme le lieu d'expédition et l'adresse acheteur :
            // une seule façon de désigner une commune dans toute l'application.
            CommuneField(
              selectedCode: _commune,
              onSelected: (code) => setState(() => _commune = code),
              label: l.shopCity,
            ),
            _field(_manager, l.shopManager),
            _field(_phone, l.shopPhone, keyboard: TextInputType.phone),
            const SizedBox(height: 8),
            FilledButton(
              onPressed: _saving ? null : _save,
              child: _saving
                  ? const SizedBox(
                      width: 22,
                      height: 22,
                      child: CircularProgressIndicator(strokeWidth: 2.4, color: Colors.white),
                    )
                  : Text(l.shopSave),
            ),
          ],
        ),
      ),
    );
  }
}

class _PayoutSheet extends ConsumerStatefulWidget {
  const _PayoutSheet({this.account});
  final PayoutAccount? account;

  @override
  ConsumerState<_PayoutSheet> createState() => _PayoutSheetState();
}

class _PayoutSheetState extends ConsumerState<_PayoutSheet> {
  final _form = GlobalKey<FormState>();
  late final TextEditingController _number =
      TextEditingController(text: widget.account?.accountNumber ?? '');
  late final TextEditingController _holder =
      TextEditingController(text: widget.account?.accountName ?? '');
  late String _provider = widget.account?.provider ?? kPayoutProviders.first.value;
  bool _saving = false;

  @override
  void dispose() {
    _number.dispose();
    _holder.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final l = AppLocalizations.of(context);
    if (!_form.currentState!.validate()) return;
    setState(() => _saving = true);
    try {
      final sellerId = await ref.read(requiredSellerIdProvider.future);
      await ref.read(shopApiProvider).setPayoutAccount(
            sellerId,
            provider: _provider,
            accountNumber: _number.text.trim(),
            accountName: _holder.text.trim(),
          );
      ref.invalidate(shopProvider);
      if (mounted) {
        Navigator.pop(context);
        AppNotify.success(context, l.shopPayoutSaved);
      }
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    return Padding(
      padding: sheetPadding(context),
      child: Form(
        key: _form,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SheetHandle(),
            Text(l.shopPayoutAccount,
                style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800, color: colors.ink)),
            const SizedBox(height: 6),
            Text(
              l.shopPayoutHint,
              style: TextStyle(fontSize: 12, color: colors.subtle, height: 1.4),
            ),
            const SizedBox(height: 18),

            // Seuls les opérateurs réellement routables sont proposés : en offrir
            // d'autres produirait des retraits impossibles à verser.
            AppDropdown<String>(
              value: _provider,
              label: l.shopOperator,
              options: payoutProviderOptions(l),
              onChanged: (v) => setState(() => _provider = v ?? _provider),
            ),
            const SizedBox(height: 14),
            TextFormField(
              controller: _number,
              keyboardType: TextInputType.phone,
              decoration: InputDecoration(labelText: l.shopMobileMoneyNumber, hintText: '+229…'),
              validator: (v) {
                final digits = (v ?? '').replaceAll(RegExp(r'\D'), '');
                return digits.length < 8 ? l.shopInvalidNumber : null;
              },
            ),
            const SizedBox(height: 14),
            TextFormField(
              controller: _holder,
              decoration: InputDecoration(labelText: l.shopAccountHolderName),
              validator: (v) => (v == null || v.trim().isEmpty) ? l.shopHolderNameRequired : null,
            ),
            const SizedBox(height: 20),
            FilledButton(
              onPressed: _saving ? null : _save,
              child: _saving
                  ? const SizedBox(
                      width: 22,
                      height: 22,
                      child: CircularProgressIndicator(strokeWidth: 2.4, color: Colors.white))
                  : Text(l.shopSave),
            ),
          ],
        ),
      ),
    );
  }
}
