import 'dart:math';

import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/identity/seller_identity.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../../inventory/inventory_data.dart';
import '../../offers/offers_data.dart';
import '../../offers/presentation/offer_card.dart';
import '../../offers/presentation/offer_sheets.dart';
import '../../shop/shop_data.dart';
import '../catalog_data.dart';
import 'image_processing.dart';

/// Fiche produit complète : photos, informations, déclinaisons, stock et mises en vente.
///
/// C'est ici que se joue la mise en vente, et l'ordre des sections suit cette
/// logique : sans photo le produit ne se vend pas, sans déclinaison il n'a pas de
/// stock, sans mise en vente il n'est pas achetable. La page le dit à chaque étape.
class ProductDetailScreen extends ConsumerWidget {
  const ProductDetailScreen({super.key, required this.productId});
  final String productId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l = AppLocalizations.of(context);
    final product = ref.watch(productProvider(productId));

    // Mises en vente actives du produit : ce sont elles, et elles seules, qui le rendent
    // achetable. « Retirer de la vente » n'a de sens que s'il y en a.
    final activeOffers = (ref.watch(offersProvider).valueOrNull ?? const <Offer>[])
        .where((o) => o.productId == productId && o.status.toLowerCase() == 'active')
        .toList();

    return Scaffold(
      appBar: AppBar(
        title: Text(l.pdTitle),
        actions: [
          product.maybeWhen(
            data: (p) => PopupMenuButton<String>(
              tooltip: l.pdActionsTooltip,
              position: PopupMenuPosition.under,
              onSelected: (a) => _run(context, ref, p, a, activeOffers),
              itemBuilder: (_) => [
                appMenuItem(
                    value: 'preview', icon: Icons.remove_red_eye_outlined, label: l.pdMenuPreview),
                appMenuItem(value: 'edit', icon: Icons.edit_outlined, label: l.pdEditSheet),
                if (activeOffers.isNotEmpty)
                  appMenuItem(
                    value: 'unlist',
                    icon: Icons.pause_circle_outline,
                    label: l.pdUnlist,
                  ),
                if (!p.isActive)
                  appMenuItem(value: 'active', icon: Icons.visibility_outlined, label: l.pdMenuPublish),
                if (p.isActive)
                  appMenuItem(value: 'draft', icon: Icons.edit_note, label: l.pdMenuDraft),
                appMenuItem(value: 'archived', icon: Icons.archive_outlined, label: l.pdMenuArchive),
                appMenuItem(
                  value: 'delete',
                  icon: Icons.delete_outline,
                  label: l.commonDelete,
                  danger: true,
                ),
              ],
            ),
            orElse: () => const SizedBox.shrink(),
          ),
          const SizedBox(width: 4),
        ],
      ),
      body: product.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(
          message: e.toString(),
          onRetry: () => ref.invalidate(productProvider(productId)),
        ),
        data: (p) => RefreshIndicator(
          onRefresh: () async => ref.invalidate(productProvider(productId)),
          child: ListView(
            padding: const EdgeInsets.only(bottom: 40),
            children: [
              _Gallery(product: p),
              _Header(product: p),
              _Infos(product: p),
              _Variants(product: p),
              _Stock(product: p),
              _Offers(product: p),
            ],
          ),
        ),
      ),
    );
  }

  Future<void> _run(
    BuildContext context,
    WidgetRef ref,
    SellerProduct p,
    String action,
    List<Offer> activeOffers,
  ) async {
    final l = AppLocalizations.of(context);
    if (action == 'preview') {
      context.push('/product/${p.id}/preview');
      return;
    }

    if (action == 'edit') {
      _sheet(context, _EditProductSheet(product: p));
      return;
    }

    // « Retirer de la vente » = mettre en pause TOUTES les mises en vente actives.
    //
    // C'est la seule action qui arrête réellement la vente. Repasser le produit
    // en brouillon le retire de la vitrine, mais ses mises en vente restent actives : un
    // acheteur ayant le lien pourrait encore commander. Ici, on coupe à la source.
    if (action == 'unlist') {
      final ok = await showDialog<bool>(
        context: context,
        builder: (dialogContext) => AlertDialog(
          title: Text(l.pdUnlistTitle),
          content: Text(l.pdUnlistBody(activeOffers.length, p.name)),
          actions: [
            TextButton(onPressed: () => Navigator.pop(dialogContext, false), child: Text(l.commonCancel)),
            FilledButton(
              style: FilledButton.styleFrom(backgroundColor: AppTheme.promoOrange),
              onPressed: () => Navigator.pop(dialogContext, true),
              child: Text(l.pdUnlist),
            ),
          ],
        ),
      );
      if (ok != true || !context.mounted) return;

      try {
        final api = ref.read(offersApiProvider);
        for (final offer in activeOffers) {
          await api.changeStatus(offer.id, 'paused');
        }
        ref.invalidate(offersProvider);
        ref.invalidate(productProvider(p.id));
        if (context.mounted) AppNotify.success(context, l.pdUnlistSuccess);
      } catch (e) {
        if (context.mounted) AppNotify.error(context, e.toString());
      }
      return;
    }

    if (action == 'delete') {
      final ok = await showDialog<bool>(
        context: context,
        builder: (dialogContext) => AlertDialog(
          title: Text(l.pdDeleteTitle),
          content: Text(l.pdDeleteBody),
          actions: [
            TextButton(onPressed: () => Navigator.pop(dialogContext, false), child: Text(l.commonCancel)),
            FilledButton(
              style: FilledButton.styleFrom(backgroundColor: AppTheme.danger),
              onPressed: () => Navigator.pop(dialogContext, true),
              child: Text(l.commonDelete),
            ),
          ],
        ),
      );
      if (ok != true || !context.mounted) return;

      try {
        await ref.read(catalogApiProvider).deleteProduct(p.id);
        ref.invalidate(productsProvider);
      ref.read(productsPagedProvider.notifier).refresh();
        if (context.mounted) {
          Navigator.of(context).pop(); // on quitte une fiche qui n'existe plus
          AppNotify.success(context, l.pdDeleteSuccess);
        }
      } catch (e) {
        if (context.mounted) AppNotify.error(context, e.toString());
      }
      return;
    }

    // Changement de statut.
    try {
      await ref.read(catalogApiProvider).changeStatus(p.id, action);
      ref.invalidate(productProvider(p.id));
      ref.invalidate(productsProvider);
      ref.read(productsPagedProvider.notifier).refresh();
      if (context.mounted) AppNotify.success(context, l.pdUpdated);
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
    }
  }
}

void _sheet(BuildContext context, Widget child) {
  showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
    builder: (_) => child,
  );
}

// ---------------------------------------------------------------- Galerie

class _Gallery extends ConsumerStatefulWidget {
  const _Gallery({required this.product});
  final SellerProduct product;

  @override
  ConsumerState<_Gallery> createState() => _GalleryState();
}

class _GalleryState extends ConsumerState<_Gallery> {
  final _page = PageController();
  bool _busy = false;
  int _current = 0;

  @override
  void dispose() {
    _page.dispose();
    super.dispose();
  }

  /// Ajout d'une photo : sélection → envoi sur media-service → rattachement au
  /// produit.
  ///
  /// IL N'Y A PLUS DE DÉTOURAGE. `ImageProcessing.pickAndProcess` rend
  /// désormais la photo TELLE QUELLE : le détourage + fond blanc passait par le
  /// BFF du monolithe (Cloudinary), et media-service redimensionne sans
  /// retoucher (module `imageProcessing`). Le vendeur voit donc son original,
  /// et le catalogue perd son homogénéité de vignettes — c'est une perte réelle,
  /// assumée, pas un oubli.
  Future<void> _add() async {
    final l = AppLocalizations.of(context);
    final images = await ImageProcessing.pickAndProcess(context, ref);
    if (images.isEmpty || !mounted) return;

    // Le serveur refuse au-delà de 5 Mo. Envoyer quand même, c'est brûler les
    // données mobiles du vendeur pour obtenir un 400.
    final tooLarge = images.where((i) => i.isTooLarge).toList();
    final sendable = images.where((i) => !i.isTooLarge).toList();

    if (tooLarge.isNotEmpty) {
      AppNotify.error(context, l.pdPhotosTooLargeIgnored(tooLarge.length));
    }
    if (sendable.isEmpty) return;

    setState(() => _busy = true);
    try {
      // LE DÉPÔT SE FAIT EN DEUX TEMPS, ET L'APPELANT FOURNIT LE DÉPOSANT.
      //
      // `catalog-service` n'a plus de route d'upload : le fichier part d'abord
      // sur media-service, qui rend `{ mediaId, url }`, puis on rattache. C'est
      // `productPhotoUploaderProvider` qui fait le premier temps — `CatalogApi`
      // ne connaît pas media-service, et ne doit pas le connaître.
      final upload = ref.read(productPhotoUploaderProvider);

      // Envoi séquentiel : media-service accepte un fichier à la fois.
      for (final image in sendable) {
        await ref
            .read(catalogApiProvider)
            .uploadImage(widget.product.id, image, uploadPhoto: upload);
      }
      ref.invalidate(productProvider(widget.product.id));
      ref.invalidate(productsProvider);
      ref.read(productsPagedProvider.notifier).refresh();
      if (mounted) AppNotify.success(context, l.pdPhotosAdded(sendable.length));
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _act(Future<void> Function() action, String ok) async {
    setState(() => _busy = true);
    try {
      await action();
      ref.invalidate(productProvider(widget.product.id));
      ref.invalidate(productsProvider);
      ref.read(productsPagedProvider.notifier).refresh();
      if (mounted) AppNotify.success(context, ok);
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    final media = widget.product.media;

    return Column(
      children: [
        SizedBox(
          height: 240,
          child: media.isEmpty
              ? Container(
                  color: colors.bg,
                  alignment: Alignment.center,
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(Icons.add_a_photo_outlined, size: 40, color: colors.subtle),
                      const SizedBox(height: 10),
                      // Sans photo, le produit n'apparaît nulle part : on le dit.
                      Text(l.pdGalleryEmpty,
                          style: TextStyle(fontSize: 12, color: colors.subtle)),
                      const SizedBox(height: 10),
                      FilledButton.icon(
                        onPressed: _busy ? null : _add,
                        icon: const Icon(Icons.add),
                        label: Text(l.pdAddPhotos),
                      ),
                    ],
                  ),
                )
              // Fond blanc + « contain » : la photo produit est vue ENTIÈRE et
              // centrée. En « cover », un casque ou une chaussure se retrouvait
              // rogné aux bords — exactement ce qu'un vendeur veut vérifier.
              : ColoredBox(
                  color: Colors.white,
                  child: PageView.builder(
                    controller: _page,
                    itemCount: media.length,
                    onPageChanged: (i) => setState(() => _current = i),
                    itemBuilder: (_, i) => Padding(
                      padding: const EdgeInsets.all(12),
                      child: CachedNetworkImage(
                        imageUrl: media[i].url,
                        fit: BoxFit.contain,
                        errorWidget: (_, __, ___) => Container(
                          color: colors.bg,
                          alignment: Alignment.center,
                          child: Icon(Icons.broken_image_outlined, color: colors.subtle),
                        ),
                      ),
                    ),
                  ),
                ),
        ),

        // Points de pagination : sans eux, on ne sait pas qu'il y a d'autres
        // photos à faire défiler.
        if (media.length > 1)
          Padding(
            padding: const EdgeInsets.only(top: 10),
            child: Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                for (var i = 0; i < media.length; i++)
                  AnimatedContainer(
                    duration: const Duration(milliseconds: 200),
                    margin: const EdgeInsets.symmetric(horizontal: 3),
                    width: i == _current ? 18 : 6,
                    height: 6,
                    decoration: BoxDecoration(
                      color: i == _current ? AppTheme.brandGreen : colors.line,
                      borderRadius: BorderRadius.circular(3),
                    ),
                  ),
              ],
            ),
          ),
        if (media.isNotEmpty)
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(l.pdPhotoCount(media.length),
                          style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 14)),
                    ),
                    TextButton.icon(
                      onPressed: _busy ? null : _add,
                      icon: const Icon(Icons.add, size: 18),
                      label: Text(l.commonAdd),
                    ),
                  ],
                ),
                const SizedBox(height: 8),
                SizedBox(
                  height: 78,
                  child: ListView.separated(
                    scrollDirection: Axis.horizontal,
                    itemCount: media.length,
                    separatorBuilder: (_, __) => const SizedBox(width: 8),
                    itemBuilder: (_, i) => _Thumb(
                      media: media[i],
                      busy: _busy,
                      selected: i == _current,
                      onTap: () => _page.animateToPage(i,
                          duration: const Duration(milliseconds: 250), curve: Curves.easeOut),
                      onPrimary: media[i].isPrimary
                          ? null
                          : () => _act(
                                () => ref
                                    .read(catalogApiProvider)
                                    .setPrimaryImage(widget.product.id, media[i].id),
                                l.pdPrimarySet,
                              ),
                      onDelete: () => _act(
                        () => ref.read(catalogApiProvider).removeImage(widget.product.id, media[i].id),
                        l.pdPhotoDeleted,
                      ),
                    ),
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  l.pdPrimaryHint,
                  style: TextStyle(fontSize: 11, color: colors.subtle),
                ),
              ],
            ),
          ),
        if (_busy) const Padding(padding: EdgeInsets.only(top: 8), child: LinearProgressIndicator()),
      ],
    );
  }
}

class _Thumb extends StatelessWidget {
  const _Thumb({
    required this.media,
    required this.busy,
    required this.selected,
    required this.onTap,
    required this.onPrimary,
    required this.onDelete,
  });

  final ProductMedia media;
  final bool busy;
  final bool selected;
  final VoidCallback onTap;
  final VoidCallback? onPrimary;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    return GestureDetector(
      onTap: onTap,
      onLongPress: busy ? null : () => _menu(context),
      child: Stack(
        children: [
          Container(
            width: 78,
            height: 78,
            padding: const EdgeInsets.all(4),
            decoration: BoxDecoration(
              color: Colors.white,
              borderRadius: BorderRadius.circular(12),
              // La vignette affichée à l'écran est encadrée : sans ce repère, on
              // ne sait pas laquelle des photos on est en train de regarder.
              border: Border.all(
                color: selected ? AppTheme.brandGreen : colors.line,
                width: selected ? 2 : 1,
              ),
            ),
            child: ClipRRect(
              borderRadius: BorderRadius.circular(8),
              child: CachedNetworkImage(imageUrl: media.url, fit: BoxFit.contain),
            ),
          ),
          if (media.isPrimary)
            Positioned(
              left: 4,
              bottom: 4,
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                decoration: BoxDecoration(
                  color: AppTheme.brandGreen,
                  borderRadius: BorderRadius.circular(8),
                ),
                child: Text(l.pdPrimaryBadge,
                    style: const TextStyle(color: Colors.white, fontSize: 9, fontWeight: FontWeight.w700)),
              ),
            ),
        ],
      ),
    );
  }

  void _menu(BuildContext context) {
    final l = AppLocalizations.of(context);
    showModalBottomSheet<void>(
      context: context,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(22))),
      builder: (sheetContext) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const SizedBox(height: 12),
            const SheetHandle(),
            if (onPrimary != null)
              ListTile(
                leading: const Icon(Icons.star_outline, color: AppTheme.brandGreen),
                title: Text(l.pdSetPrimary),
                onTap: () {
                  Navigator.pop(sheetContext);
                  onPrimary!();
                },
              ),
            ListTile(
              leading: const Icon(Icons.delete_outline, color: AppTheme.danger),
              title: Text(l.pdDeletePhoto, style: const TextStyle(color: AppTheme.danger)),
              onTap: () {
                Navigator.pop(sheetContext);
                onDelete();
              },
            ),
            const SizedBox(height: 8),
          ],
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------- En-tête

class _Header extends StatelessWidget {
  const _Header({required this.product});
  final SellerProduct product;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 0),
      child: Row(
        children: [
          Expanded(
            child: Text(product.name,
                style: TextStyle(fontSize: 20, fontWeight: FontWeight.w800, color: colors.ink)),
          ),
          const SizedBox(width: 10),
          StatusPill.catalog(l, product.status),
        ],
      ),
    );
  }
}

// ---------------------------------------------------------------- Informations

class _Infos extends ConsumerWidget {
  const _Infos({required this.product});
  final SellerProduct product;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    final category = ref.watch(categoryLabelProvider(product.categoryId));

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SectionHeader(title: l.pdInfoSection),
        CardSection(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              KeyValueRow(label: l.pdInfoCategory, value: category),
              if (product.gtin != null) KeyValueRow(label: 'GTIN', value: product.gtin!),
              if (product.ean != null) KeyValueRow(label: 'EAN', value: product.ean!),
              KeyValueRow(label: 'Photos', value: '${product.media.length}'),
              KeyValueRow(label: l.pdInfoVariants, value: '${product.variants.length}'),
              Divider(height: 22, color: colors.line),
              Text(l.commonDescription,
                  style: TextStyle(fontSize: 12, fontWeight: FontWeight.w800, color: colors.subtle)),
              const SizedBox(height: 6),
              Text(
                product.description.isEmpty
                    ? l.pdInfoNoDescription
                    : product.description,
                style: TextStyle(
                  fontSize: 13,
                  height: 1.5,
                  color: product.description.isEmpty ? colors.subtle : colors.ink,
                  fontStyle: product.description.isEmpty ? FontStyle.italic : FontStyle.normal,
                ),
              ),
              if (product.tags.isNotEmpty) ...[
                const SizedBox(height: 14),
                Wrap(
                  spacing: 6,
                  runSpacing: 6,
                  children: [
                    for (final t in product.tags)
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                        decoration: BoxDecoration(
                          color: colors.bg,
                          borderRadius: BorderRadius.circular(20),
                        ),
                        child: Text(t, style: TextStyle(fontSize: 11, color: colors.subtle)),
                      ),
                  ],
                ),
              ],
            ],
          ),
        ),
      ],
    );
  }
}

// ---------------------------------------------------------------- Déclinaisons

class _Variants extends ConsumerWidget {
  const _Variants({required this.product});
  final SellerProduct product;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SectionHeader(
          title: l.pdVariantsSection(product.variants.length),
          actionLabel: l.pdSectionAdd,
          onAction: () => _sheet(context, _VariantSheet(product: product)),
        ),
        if (product.variants.isEmpty)
          CardSection(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                // Point de blocage réel : sans déclinaison, pas de SKU ; sans SKU,
                // ni stock ni mise en vente. Autant l'expliquer plutôt qu'afficher « vide ».
                Text(
                  l.pdVariantsEmpty,
                  style: TextStyle(fontSize: 13, color: colors.ink, height: 1.4),
                ),
                const SizedBox(height: 12),
                FilledButton(
                  onPressed: () => _sheet(context, _VariantSheet(product: product)),
                  child: Text(l.pdCreateVariant),
                ),
              ],
            ),
          )
        else
          CardSection(
            child: Column(
              children: [
                for (var i = 0; i < product.variants.length; i++) ...[
                  if (i > 0) Divider(height: 1, color: colors.line),
                  ListTile(
                    title: Text(product.variants[i].label,
                        style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 14)),
                    subtitle: Text(
                      'SKU ${product.variants[i].sku}'
                      '${product.variants[i].weightGrams > 0 ? ' · ${product.variants[i].weightGrams} g' : ''}'
                      // DIT SUR LA LIGNE, pas dans une icône : un vendeur qui
                      // ne voit plus ses commandes sur une taille doit lire ici
                      // pourquoi, sans avoir à ouvrir un menu.
                      '${product.variants[i].isActive ? '' : ' · retirée de la vente'}',
                      style: TextStyle(
                        fontSize: 12,
                        color: product.variants[i].isActive ? colors.subtle : AppTheme.promoOrange,
                      ),
                    ),
                    // TROIS GESTES, PLUS UNE SEULE POUBELLE (tâche #230).
                    //
                    // Il n'y avait que « Supprimer » — donc un vendeur dont la
                    // taille 42 est épuisée pour la saison n'avait pas d'autre
                    // choix. Or supprimer libère le SKU, perd les attributs et le
                    // code-barres, et laisse un historique de commandes qui pointe
                    // vers rien. « Retirer de la vente » est ce qu'il veut neuf
                    // fois sur dix, et c'est maintenant l'option évidente.
                    trailing: PopupMenuButton<String>(
                      position: PopupMenuPosition.under,
                      onSelected: (v) => switch (v) {
                        'vente' => _basculerVente(context, ref, product.variants[i]),
                        _ => _delete(context, ref, product.variants[i]),
                      },
                      itemBuilder: (_) => [
                        appMenuItem(
                          value: 'vente',
                          icon: product.variants[i].isActive
                              ? Icons.remove_shopping_cart_outlined
                              : Icons.add_shopping_cart_outlined,
                          label: product.variants[i].isActive
                              ? 'Retirer de la vente'
                              : 'Remettre en vente',
                        ),
                        appMenuItem(
                            value: 'supprimer',
                            icon: Icons.delete_outline,
                            label: 'Supprimer définitivement',
                            danger: true),
                      ],
                    ),
                  ),
                ],
              ],
            ),
          ),
      ],
    );
  }

  /// Bascule la mise en vente d'une déclinaison.
  ///
  /// LA DÉSACTIVATION EST ANNONCÉE AVANT, PAS CONSTATÉE APRÈS.
  ///
  /// Elle archive les mises en vente de cette déclinaison, et l'archivage est
  /// TERMINAL : réactiver ne rétablit aucun prix. Le vendeur doit le savoir avant
  /// d'appuyer — sinon il découvre son écran de mises en vente vidé, et croit à une
  /// panne.
  Future<void> _basculerVente(
      BuildContext context, WidgetRef ref, ProductVariant variant) async {
    if (variant.isActive) {
      final ok = await showDialog<bool>(
        context: context,
        builder: (d) => AlertDialog(
          title: const Text('Retirer de la vente ?'),
          content: Text(
            '« ${variant.label} » ne sera plus achetable. Ses mises en vente seront '
            'fermées DÉFINITIVEMENT : en la remettant en vente plus tard, vous devrez '
            'refixer son prix.\n\nLa déclinaison, son code et son stock sont conservés.',
          ),
          actions: [
            TextButton(onPressed: () => Navigator.pop(d, false), child: const Text('Annuler')),
            FilledButton(
                onPressed: () => Navigator.pop(d, true), child: const Text('Retirer')),
          ],
        ),
      );
      if (ok != true || !context.mounted) return;
    }

    try {
      final archivees = await ref
          .read(catalogApiProvider)
          .setVariantActive(product.id, variant.id, active: !variant.isActive);
      ref.invalidate(productProvider(product.id));
      ref.invalidate(offersProvider);
      if (!context.mounted) return;

      AppNotify.success(
        context,
        variant.isActive
            ? (archivees > 0
                ? 'Retirée de la vente. $archivees mise(s) en vente fermée(s).'
                : 'Retirée de la vente.')
            : 'Remise en vente. Fixez son prix depuis « Mises en vente ».',
      );
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
    }
  }

  Future<void> _delete(BuildContext context, WidgetRef ref, ProductVariant variant) async {
    final l = AppLocalizations.of(context);
    final ok = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text(l.pdDeleteVariantTitle),
        content: Text(l.pdDeleteVariantBody(variant.label, variant.sku)),
        actions: [
          TextButton(onPressed: () => Navigator.pop(dialogContext, false), child: Text(l.commonCancel)),
          FilledButton(
            style: FilledButton.styleFrom(backgroundColor: AppTheme.danger),
            onPressed: () => Navigator.pop(dialogContext, true),
            child: Text(l.commonDelete),
          ),
        ],
      ),
    );
    if (ok != true || !context.mounted) return;

    try {
      await ref.read(catalogApiProvider).removeVariant(product.id, variant.id);
      ref.invalidate(productProvider(product.id));
      if (context.mounted) AppNotify.success(context, l.pdVariantDeleted);
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
    }
  }
}

class _VariantSheet extends ConsumerStatefulWidget {
  const _VariantSheet({required this.product});
  final SellerProduct product;

  @override
  ConsumerState<_VariantSheet> createState() => _VariantSheetState();
}

class _VariantSheetState extends ConsumerState<_VariantSheet> {
  final _form = GlobalKey<FormState>();
  final _sku = TextEditingController();
  final _barcode = TextEditingController();
  final _weight = TextEditingController(text: '0');

  /// Attributs libres (Taille → 42, Couleur → Noir).
  final List<({TextEditingController key, TextEditingController value})> _attributes = [];
  bool _saving = false;

  @override
  void initState() {
    super.initState();
    _addAttribute();
    // SKU pré-rempli (préfixe = 6 car. de l'ID vendeur + code aléatoire), au même
    // format que la génération serveur. Reste modifiable ; laissé vide, le serveur
    // le régénère de toute façon.
    _sku.text = _suggestSku(ref.read(shopProvider).valueOrNull?.id);
  }

  /// Suggestion de SKU côté client. Aligné sur `Sku.Generate` du backend :
  /// 6 premiers caractères de l'ID vendeur (sans tirets), un tiret, puis 8
  /// caractères base36 aléatoires. Sans ID vendeur connu, on ne met que le code.
  static String _suggestSku(String? sellerId) {
    const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';
    final rand = Random.secure();
    final code = List.generate(8, (_) => alphabet[rand.nextInt(alphabet.length)]).join();
    final compact = (sellerId ?? '').replaceAll('-', '');
    if (compact.length < 6) return code;
    return '${compact.substring(0, 6).toUpperCase()}-$code';
  }

  void _addAttribute() {
    _attributes.add((key: TextEditingController(), value: TextEditingController()));
  }

  /// Retire une ligne d'attribut. On libère ses contrôleurs ici : les garder
  /// jusqu'au dispose ferait fuir de la mémoire à chaque ajout/retrait.
  void _removeAttribute(int index) {
    final removed = _attributes.removeAt(index);
    removed.key.dispose();
    removed.value.dispose();
    setState(() {});
  }

  @override
  void dispose() {
    _sku.dispose();
    _barcode.dispose();
    _weight.dispose();
    for (final a in _attributes) {
      a.key.dispose();
      a.value.dispose();
    }
    super.dispose();
  }

  Future<void> _save() async {
    if (!_form.currentState!.validate()) return;

    final attributes = <String, String>{};
    for (final a in _attributes) {
      final k = a.key.text.trim();
      final v = a.value.text.trim();
      if (k.isNotEmpty && v.isNotEmpty) attributes[k] = v;
    }

    setState(() => _saving = true);
    try {
      await ref.read(catalogApiProvider).addVariant(
            widget.product.id,
            sku: _sku.text.trim(),
            attributes: attributes,
            barcode: _barcode.text.trim().isEmpty ? null : _barcode.text.trim(),
            weightGrams: int.tryParse(_weight.text.trim()) ?? 0,
          );
      ref.invalidate(productProvider(widget.product.id));
      if (mounted) {
        Navigator.pop(context);
        AppNotify.success(context, AppLocalizations.of(context).pdVariantAdded);
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
      child: SingleChildScrollView(
        child: Form(
          key: _form,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const SheetHandle(),
              Text(l.pdNewVariant,
                  style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: colors.ink)),
              const SizedBox(height: 6),
              Text(
                l.pdVariantSkuIntro,
                style: TextStyle(fontSize: 12, color: colors.subtle, height: 1.4),
              ),
              const SizedBox(height: 18),
              TextFormField(
                controller: _sku,
                decoration: InputDecoration(
                  labelText: l.commonSku,
                  helperText: l.pdSkuHelper,
                ),
              ),
              const SizedBox(height: 14),
              for (var i = 0; i < _attributes.length; i++) ...[
                Row(
                  children: [
                    Expanded(
                      child: TextFormField(
                        controller: _attributes[i].key,
                        decoration: InputDecoration(labelText: l.pdAttrLabel, hintText: l.pdAttrHintSize),
                      ),
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: TextFormField(
                        controller: _attributes[i].value,
                        decoration: InputDecoration(labelText: l.pdValueLabel, hintText: '42'),
                      ),
                    ),
                    // On garde toujours au moins une ligne : un formulaire sans
                    // aucun champ d'attribut n'aurait plus de point d'entrée.
                    IconButton(
                      tooltip: l.pdRemoveAttr,
                      onPressed: _attributes.length == 1 ? null : () => _removeAttribute(i),
                      icon: Icon(
                        Icons.remove_circle_outline,
                        color: _attributes.length == 1 ? colors.line : AppTheme.danger,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 10),
              ],
              Align(
                alignment: Alignment.centerLeft,
                child: TextButton.icon(
                  onPressed: () => setState(_addAttribute),
                  icon: const Icon(Icons.add, size: 18),
                  label: Text(l.pdAddAttr),
                ),
              ),
              const SizedBox(height: 6),
              TextFormField(
                controller: _barcode,
                decoration: InputDecoration(labelText: l.pdBarcodeOptional),
              ),
              const SizedBox(height: 14),
              TextFormField(
                controller: _weight,
                keyboardType: TextInputType.number,
                decoration: InputDecoration(labelText: l.pdWeight, suffixText: 'g'),
              ),
              const SizedBox(height: 22),
              FilledButton(
                onPressed: _saving ? null : _save,
                child: _saving
                    ? const SizedBox(
                        width: 22,
                        height: 22,
                        child: CircularProgressIndicator(strokeWidth: 2.4, color: Colors.white))
                    : Text(l.pdAddVariant),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------- Stock

class _Stock extends StatelessWidget {
  const _Stock({required this.product});
  final SellerProduct product;

  @override
  Widget build(BuildContext context) {
    if (product.variants.isEmpty) {
      return const SizedBox.shrink(); // sans SKU, il n'y a rien à stocker
    }

    final l = AppLocalizations.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SectionHeader(title: l.commonStock),
        for (final v in product.variants)
          Padding(
            padding: const EdgeInsets.only(bottom: 10),
            child: _StockBySku(sku: v.sku, label: v.label),
          ),
      ],
    );
  }
}

class _StockBySku extends ConsumerWidget {
  const _StockBySku({required this.sku, required this.label});
  final String sku;
  final String label;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    final items = ref.watch(inventoryBySkuProvider(sku));

    return CardSection(
      padding: const EdgeInsets.all(14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(label, style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 14)),
                    Text('SKU $sku', style: TextStyle(fontSize: 11, color: colors.subtle)),
                  ],
                ),
              ),
              TextButton.icon(
                onPressed: () => _sheet(context, _CreateStockSheet(sku: sku)),
                icon: const Icon(Icons.add, size: 18),
                label: Text(l.pdLocationShort),
              ),
            ],
          ),
          const SizedBox(height: 6),
          items.when(
            loading: () => const Padding(
              padding: EdgeInsets.symmetric(vertical: 8),
              child: LinearProgressIndicator(),
            ),
            error: (e, _) => Text(l.pdStockUnavailable(e.toString()),
                style: const TextStyle(fontSize: 12, color: AppTheme.danger)),
            data: (list) => list.isEmpty
                ? Text(l.pdNoStockForSku,
                    style: TextStyle(fontSize: 12, color: colors.subtle))
                : Column(
                    children: [
                      for (final it in list) _StockRow(item: it),
                    ],
                  ),
          ),
        ],
      ),
    );
  }
}

class _StockRow extends ConsumerWidget {
  const _StockRow({required this.item});
  final InventoryItem item;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    // Résolution du lieu : l'API ne renvoie qu'un identifiant.
    final locations = ref.watch(locationsProvider).valueOrNull ?? const <ShipLocation>[];
    var place = l.pdLocationFallback;
    for (final loc in locations) {
      if (loc.id == item.locationId) {
        place = loc.label;
        break;
      }
    }

    return Padding(
      padding: const EdgeInsets.only(top: 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(child: Text(place, style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w600))),
              // « Disponible » = ce qui reste vendable une fois les commandes en
              // cours déduites. C'est le chiffre qui compte, pas le physique.
              Text(l.pdStockAvailable(item.available),
                  style: TextStyle(
                    fontWeight: FontWeight.w800,
                    fontSize: 14,
                    color: item.isLowStock ? AppTheme.danger : AppTheme.brandGreen,
                  )),
              if (item.isLowStock) ...[
                const SizedBox(width: 6),
                StatusBadge(label: l.pdStockLow, color: AppTheme.danger),
              ],
            ],
          ),
          Text(
            l.pdStockLine(item.onHand, item.reserved, item.reorderThreshold),
            style: TextStyle(fontSize: 11, color: colors.subtle),
          ),
          const SizedBox(height: 8),
          Wrap(
            spacing: 8,
            children: [
              OutlinedButton(
                onPressed: () => _ask(context, ref, _StockAction.receive),
                child: Text(l.pdStockReceive),
              ),
              OutlinedButton(
                onPressed: () => _ask(context, ref, _StockAction.adjust),
                child: Text(l.pdStockAdjust),
              ),
              OutlinedButton(
                onPressed: () => _ask(context, ref, _StockAction.threshold),
                child: Text(l.pdThresholdShort),
              ),
            ],
          ),
        ],
      ),
    );
  }

  void _ask(BuildContext context, WidgetRef ref, _StockAction action) {
    _sheet(context, _StockActionSheet(item: item, action: action));
  }
}

enum _StockAction { receive, adjust, threshold }

class _StockActionSheet extends ConsumerStatefulWidget {
  const _StockActionSheet({required this.item, required this.action});
  final InventoryItem item;
  final _StockAction action;

  @override
  ConsumerState<_StockActionSheet> createState() => _StockActionSheetState();
}

class _StockActionSheetState extends ConsumerState<_StockActionSheet> {
  late final TextEditingController _value = TextEditingController(
    text: widget.action == _StockAction.threshold ? '${widget.item.reorderThreshold}' : '',
  );
  bool _saving = false;

  @override
  void dispose() {
    _value.dispose();
    super.dispose();
  }

  ({String title, String hint, String label}) _texts(AppLocalizations l) {
    switch (widget.action) {
      case _StockAction.receive:
        return (
          title: l.pdReceiveTitle,
          hint: l.pdReceiveHint,
          label: l.pdQuantity,
        );
      case _StockAction.adjust:
        return (
          title: l.pdAdjustTitle,
          // L'ajustement est SIGNÉ : c'est ce qui le distingue d'une réception,
          // et ce qui permet de tracer une perte plutôt que de la maquiller.
          hint: l.pdAdjustHint,
          label: l.pdAdjustLabel,
        );
      case _StockAction.threshold:
        return (
          title: l.pdAlertThreshold,
          hint: l.pdThresholdHint,
          label: l.pdThresholdShort,
        );
    }
  }

  Future<void> _save() async {
    final l = AppLocalizations.of(context);
    final n = int.tryParse(_value.text.trim());
    if (n == null) {
      AppNotify.error(context, l.pdEnterInteger);
      return;
    }
    if (widget.action != _StockAction.adjust && n < 0) {
      AppNotify.error(context, l.pdValuePositive);
      return;
    }
    if (widget.action == _StockAction.adjust && n == 0) {
      AppNotify.error(context, l.pdAdjustZero);
      return;
    }

    setState(() => _saving = true);
    try {
      final api = ref.read(inventoryApiProvider);
      switch (widget.action) {
        case _StockAction.receive:
          await api.receive(widget.item.id, n);
        case _StockAction.adjust:
          await api.adjust(widget.item.id, n);
        case _StockAction.threshold:
          await api.setThreshold(widget.item.id, n);
      }
      ref.invalidate(inventoryBySkuProvider(widget.item.sku));
      if (mounted) {
        Navigator.pop(context);
        AppNotify.success(context, l.pdStockUpdated);
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
    final t = _texts(l);

    return Padding(
      padding: sheetPadding(context),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(t.title, style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: colors.ink)),
          const SizedBox(height: 6),
          Text(t.hint, style: TextStyle(fontSize: 12, color: colors.subtle, height: 1.4)),
          const SizedBox(height: 18),
          TextField(
            controller: _value,
            autofocus: true,
            keyboardType: TextInputType.numberWithOptions(
              signed: widget.action == _StockAction.adjust,
            ),
            decoration: InputDecoration(labelText: t.label),
          ),
          const SizedBox(height: 20),
          FilledButton(
            onPressed: _saving ? null : _save,
            child: _saving
                ? const SizedBox(
                    width: 22,
                    height: 22,
                    child: CircularProgressIndicator(strokeWidth: 2.4, color: Colors.white))
                : Text(l.pdValidate),
          ),
        ],
      ),
    );
  }
}

/// Ouvrir un stock pour ce SKU dans un lieu donné.
class _CreateStockSheet extends ConsumerStatefulWidget {
  const _CreateStockSheet({required this.sku});
  final String sku;

  @override
  ConsumerState<_CreateStockSheet> createState() => _CreateStockSheetState();
}

class _CreateStockSheetState extends ConsumerState<_CreateStockSheet> {
  final _onHand = TextEditingController(text: '0');
  final _threshold = TextEditingController(text: '0');
  String? _locationId;
  bool _saving = false;

  @override
  void dispose() {
    _onHand.dispose();
    _threshold.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final l = AppLocalizations.of(context);
    if (_locationId == null) {
      AppNotify.error(context, l.pdChooseLocation);
      return;
    }

    setState(() => _saving = true);
    try {
      await ref.read(inventoryApiProvider).createItem(
            sku: widget.sku,
            locationId: _locationId!,
            onHand: int.tryParse(_onHand.text.trim()) ?? 0,
            reorderThreshold: int.tryParse(_threshold.text.trim()) ?? 0,
          );
      ref.invalidate(inventoryBySkuProvider(widget.sku));
      if (mounted) {
        Navigator.pop(context);
        AppNotify.success(context, l.pdStockCreated);
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
    final locations = ref.watch(locationsProvider);

    return Padding(
      padding: sheetPadding(context),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(l.pdStockSkuTitle(widget.sku),
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: colors.ink)),
          const SizedBox(height: 18),
          locations.when(
            loading: () => const LinearProgressIndicator(),
            error: (e, _) =>
                Text(l.pdLocationsUnavailable(e.toString()), style: const TextStyle(color: AppTheme.danger, fontSize: 12)),
            data: (list) => list.isEmpty
                ? Text(
                    l.pdNoShipLocation,
                    style: const TextStyle(color: AppTheme.promoOrange, fontSize: 12),
                  )
                : AppDropdown<String>(
                    value: _locationId,
                    label: l.pdShipLocation,
                    options: [for (final loc in list) (value: loc.id, label: loc.label)],
                    onChanged: (v) => setState(() => _locationId = v),
                  ),
          ),
          const SizedBox(height: 14),
          TextField(
            controller: _onHand,
            keyboardType: TextInputType.number,
            decoration: InputDecoration(labelText: l.pdStockQty),
          ),
          const SizedBox(height: 14),
          TextField(
            controller: _threshold,
            keyboardType: TextInputType.number,
            decoration: InputDecoration(labelText: l.pdAlertThreshold),
          ),
          const SizedBox(height: 20),
          FilledButton(
            onPressed: _saving ? null : _save,
            child: _saving
                ? const SizedBox(
                    width: 22,
                    height: 22,
                    child: CircularProgressIndicator(strokeWidth: 2.4, color: Colors.white))
                : Text(l.pdCreateStock),
          ),
        ],
      ),
    );
  }
}

// ---------------------------------------------------------------- Mises en vente

/// Mises en vente du produit, gérées SUR PLACE : créer, changer le prix, mettre en pause.
///
/// Les feuilles de saisie sont celles de l'écran Mises en vente (composant partagé) :
/// le calcul « prix net → prix acheteur » n'existe qu'à un seul endroit, donc
/// les deux écrans ne pourront jamais afficher deux prix différents.
class _Offers extends ConsumerWidget {
  const _Offers({required this.product});
  final SellerProduct product;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    final offers = ref.watch(offersProvider);
    final variants = product.variants;

    return offers.when(
      loading: () => const Padding(padding: EdgeInsets.all(16), child: LinearProgressIndicator()),
      error: (e, _) => Padding(
        padding: const EdgeInsets.all(16),
        child: Text(l.pdOffersUnavailable(e.toString()),
            style: const TextStyle(fontSize: 12, color: AppTheme.danger)),
      ),
      data: (all) {
        final mine = all.where((o) => o.productId == product.id).toList();
        final canCreate = variants.isNotEmpty;

        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SectionHeader(
              title: l.pdOffersSection(mine.length),
              actionLabel: l.pdSectionAdd,
              onAction: canCreate
                  ? () => OfferSheets.create(context, productId: product.id, variants: variants)
                  : null,
            ),
            if (mine.isEmpty)
              CardSection(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    // Le vrai déclencheur de la vente : un produit « En ligne »
                    // sans mise en vente active n'est PAS achetable. C'est l'erreur la
                    // plus coûteuse du back-office, et la moins visible.
                    Text(
                      canCreate
                          ? l.pdNoOfferCanCreate
                          : l.pdNoOfferNoVariant,
                      style: TextStyle(fontSize: 13, color: colors.ink, height: 1.4),
                    ),
                    const SizedBox(height: 12),
                    FilledButton(
                      onPressed: canCreate
                          ? () => OfferSheets.create(context, productId: product.id, variants: variants)
                          : null,
                      child: Text(l.pdPutOnSale),
                    ),
                  ],
                ),
              )
            else
              Column(
                children: [
                  for (final o in mine)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 10),
                      child: OfferCard(offer: o, showProductName: false),
                    ),
                ],
              ),
          ],
        );
      },
    );
  }
}

// ---------------------------------------------------------------- Édition fiche

class _EditProductSheet extends ConsumerStatefulWidget {
  const _EditProductSheet({required this.product});
  final SellerProduct product;

  @override
  ConsumerState<_EditProductSheet> createState() => _EditProductSheetState();
}

class _EditProductSheetState extends ConsumerState<_EditProductSheet> {
  late final TextEditingController _name = TextEditingController(text: widget.product.name);
  late final TextEditingController _description = TextEditingController(text: widget.product.description);
  late final TextEditingController _gtin = TextEditingController(text: widget.product.gtin ?? '');
  late final TextEditingController _ean = TextEditingController(text: widget.product.ean ?? '');
  bool _saving = false;

  @override
  void dispose() {
    _name.dispose();
    _description.dispose();
    _gtin.dispose();
    _ean.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final l = AppLocalizations.of(context);
    if (_name.text.trim().length < 3) {
      AppNotify.error(context, l.pdNameMin3);
      return;
    }

    setState(() => _saving = true);
    try {
      final api = ref.read(catalogApiProvider);

      // ───────────────────────────────────────────────────────────────────────
      // RELECTURE DE LA FICHE JUSTE AVANT L'ÉCRITURE. CE N'EST PAS UN APPEL DE
      // CONFORT.
      //
      // `PUT /seller/products/{id}` remplace la fiche ENTIÈRE : les quatre champs
      // que cette feuille n'édite pas (marque, groupe, attributs, tags) doivent
      // être renvoyés, sinon ils sont effacés.
      //
      // Les reprendre depuis `widget.product` ne suffit pas. `productProvider`
      // est un `FutureProvider.family` SANS `autoDispose` : la fiche reste en
      // cache tant que l'app vit. Or un administrateur peut poser ou retirer le
      // tag `featured` à tout moment (`PUT /admin/products/{id}/tags`). Si cela
      // arrive pendant que le vendeur a la fiche ouverte, renvoyer les tags
      // mémorisés écrase la décision de l'admin — et fait disparaître le produit
      // de la vitrine d'accueil. C'est le bug d'origine, par une autre porte.
      //
      // On relit donc l'état RÉEL, et on n'écrit rien si la relecture échoue :
      // écrire sur la foi de données périmées détruit, alors qu'abandonner ne
      // coûte qu'un message.
      // ───────────────────────────────────────────────────────────────────────
      final fresh = await api.product(widget.product.id);

      // `sellerId` ET `categoryId` SONT OBLIGATOIRES DANS LE CORPS, MÊME
      // IGNORÉS PAR LE GESTIONNAIRE.
      //
      // `ProductRequest` les déclare NON nullables : les omettre fait échouer la
      // désérialisation avant même d'atteindre `UpdateProductCommand`, qui ne
      // les relit pas. Le `sellerId` vient du socle d'identité — jamais d'un
      // champ d'écran — et la catégorie est reprise de la relecture.
      final sellerId = await ref.read(requiredSellerIdProvider.future);

      await api.updateProduct(
            widget.product.id,
            sellerId: sellerId,
            categoryId: fresh.categoryId,
            name: _name.text.trim(),
            description: _description.text.trim(),
            gtin: _gtin.text.trim().isEmpty ? null : _gtin.text.trim(),
            ean: _ean.text.trim().isEmpty ? null : _ean.text.trim(),
            // Non éditables ici — repassés tels quels. Le jour où cette feuille
            // gagnera un sélecteur de marque ou un éditeur de tags, c'est ICI
            // qu'il faudra brancher les contrôleurs, pas ailleurs.
            brandId: fresh.brandId,
            productGroupId: fresh.productGroupId,
            attributes: fresh.attributes,
            tags: fresh.tags,
          );
      ref.invalidate(productProvider(widget.product.id));
      ref.invalidate(productsProvider);
      ref.read(productsPagedProvider.notifier).refresh();
      if (mounted) {
        Navigator.pop(context);
        AppNotify.success(context, l.pdSheetUpdated);
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
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(l.pdEditSheet,
                style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: colors.ink)),
            const SizedBox(height: 18),
            TextField(controller: _name, decoration: InputDecoration(labelText: l.pdName)),
            const SizedBox(height: 14),
            TextField(
              controller: _description,
              maxLines: 4,
              decoration: InputDecoration(labelText: l.commonDescription),
            ),
            const SizedBox(height: 14),
            TextField(
              controller: _gtin,
              decoration: InputDecoration(labelText: l.pdGtinOptional),
            ),
            const SizedBox(height: 14),
            TextField(
              controller: _ean,
              decoration: InputDecoration(labelText: l.pdEanOptional),
            ),
            const SizedBox(height: 22),
            FilledButton(
              onPressed: _saving ? null : _save,
              child: _saving
                  ? const SizedBox(
                      width: 22,
                      height: 22,
                      child: CircularProgressIndicator(strokeWidth: 2.4, color: Colors.white))
                  : Text(l.commonSave),
            ),
          ],
        ),
      ),
    );
  }
}
