import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/theme/app_theme.dart';
import '../catalog_data.dart';

/// Sélection + traitement des photos produit.
///
/// Toute photo passe par le serveur (détourage Cloudinary, fond blanc) AVANT
/// d'être envoyée. C'est ce qui donne au catalogue son aspect homogène : une
/// photo brute — fond de salon, table en bois — au milieu de fiches détourées
/// se repère instantanément et dévalue le produit.
///
/// Le traitement peut échouer (Cloudinary indisponible, réseau coupé). On ne
/// bloque pas la vente pour autant : l'échec est signalé, réessayable, et le
/// vendeur décide en connaissance de cause d'envoyer l'original ou de renoncer.
class ImageProcessing {
  const ImageProcessing._();

  static final _picker = ImagePicker();

  /// Nombre d'images traitées en parallèle.
  ///
  /// Séquentiel, 8 photos = 8 allers-retours bout à bout (facilement 40 s sur
  /// une 3G béninoise) : le vendeur abandonne. Tout en parallèle, on sature le
  /// lien montant et Cloudinary, et les requêtes commencent à expirer. Trois est
  /// le compromis : le débit utile est atteint, la file reste courte.
  static const _concurrency = 3;

  /// Photos redimensionnées à la sélection.
  ///
  /// Le serveur refuse au-delà de 5 Mo, et un capteur moderne produit sans peine
  /// 8 Mo. 2000 px de côté suffisent largement pour une fiche produit — au-delà,
  /// on ne fait que payer du temps d'upload que le vendeur paie en données.
  static const _maxSide = 2000.0;
  static const _quality = 88;

  /// Choisit des photos puis les fait traiter. Renvoie une liste vide si le
  /// vendeur annule.
  static Future<List<ProcessedImage>> pickAndProcess(
    BuildContext context,
    WidgetRef ref, {
    bool multiple = true,
    ImageSource source = ImageSource.gallery,
  }) async {
    final List<XFile> picked;
    if (multiple && source == ImageSource.gallery) {
      picked = await _picker.pickMultiImage(
        imageQuality: _quality,
        maxWidth: _maxSide,
        maxHeight: _maxSide,
      );
    } else {
      final one = await _picker.pickImage(
        source: source,
        imageQuality: _quality,
        maxWidth: _maxSide,
        maxHeight: _maxSide,
      );
      picked = [if (one != null) one];
    }

    if (picked.isEmpty || !context.mounted) return const [];

    // Traitement bloquant mais visible : le vendeur voit ce qui se passe, et
    // ne peut pas valider un formulaire avec des images à moitié traitées.
    //
    // Le `await` avant le `??` est indispensable : sans lui, le `??` porterait
    // sur le Future lui-même (jamais nul) et non sur sa valeur — l'annulation
    // renverrait alors « null » au lieu d'une liste vide.
    final result = await showDialog<List<ProcessedImage>>(
      context: context,
      barrierDismissible: false,
      builder: (_) => _ProcessingDialog(files: picked.map((x) => File(x.path)).toList()),
    );

    return result ?? const [];
  }

  /// Réessaie le détourage d'UNE photo déjà sélectionnée (le fichier d'origine
  /// est toujours sur l'appareil). Renvoie l'image mise à jour — traitée si le
  /// serveur a répondu, porteuse de l'erreur sinon.
  static Future<ProcessedImage> retry(WidgetRef ref, ProcessedImage image) async {
    try {
      final processed = await ref.read(catalogApiProvider).processImage(File(image.sourcePath));
      return image.withResult(processed, null).renamedToJpeg();
    } catch (e) {
      return image.withResult(null, e.toString());
    }
  }

  /// Traite un lot par petits paquets. Un échec isolé n'emporte pas le lot.
  static Future<List<ProcessedImage>> processAll(
    CatalogApi api,
    List<ProcessedImage> images, {
    void Function(ProcessedImage updated)? onEach,
  }) async {
    final out = List<ProcessedImage>.from(images);

    for (var start = 0; start < out.length; start += _concurrency) {
      final end = start + _concurrency < out.length ? start + _concurrency : out.length;

      await Future.wait([
        for (var i = start; i < end; i++)
          () async {
            final index = i;
            try {
              final bytes = await api.processImage(File(out[index].sourcePath));
              out[index] = out[index].withResult(bytes, null).renamedToJpeg();
            } catch (e) {
              out[index] = out[index].withResult(null, e.toString());
            }
            onEach?.call(out[index]);
          }(),
      ]);
    }

    return out;
  }
}

extension on ProcessedImage {
  /// Cloudinary renvoie du JPEG, quel que soit le format d'entrée. Garder
  /// « photo.png » comme nom ferait déclarer `image/png` pour des octets JPEG :
  /// le serveur valide sur le type déclaré et l'accepterait, mais le fichier
  /// stocké serait mal étiqueté — et un jour, quelque chose s'en apercevrait.
  ProcessedImage renamedToJpeg() {
    if (!isProcessed) return this;
    final base = fileName.contains('.') ? fileName.substring(0, fileName.lastIndexOf('.')) : fileName;
    return ProcessedImage(
      fileName: '$base.jpg',
      sourcePath: sourcePath,
      original: original,
      processed: processed,
      error: null,
    );
  }
}

/// Traite les images par paquets en montrant l'avancement, puis affiche le
/// comparatif avant/après pour validation.
class _ProcessingDialog extends ConsumerStatefulWidget {
  const _ProcessingDialog({required this.files});
  final List<File> files;

  @override
  ConsumerState<_ProcessingDialog> createState() => _ProcessingDialogState();
}

class _ProcessingDialogState extends ConsumerState<_ProcessingDialog> {
  List<ProcessedImage> _results = [];
  int _done = 0;
  bool _finished = false;
  bool _running = true;

  @override
  void initState() {
    super.initState();
    _run();
  }

  Future<void> _run() async {
    // Les octets d'origine sont lus AVANT tout appel réseau : ils servent au
    // comparatif « avant / après », et de repli si le détourage échoue.
    final initial = <ProcessedImage>[];
    for (final file in widget.files) {
      initial.add(ProcessedImage(
        fileName: file.path.split(Platform.pathSeparator).last,
        sourcePath: file.path,
        original: await file.readAsBytes(),
      ));
    }

    if (!mounted) return;
    setState(() {
      _results = initial;
      _done = 0;
      _running = true;
      _finished = false;
    });

    final processed = await ImageProcessing.processAll(
      ref.read(catalogApiProvider),
      initial,
      onEach: (_) {
        if (mounted) setState(() => _done++);
      },
    );

    if (!mounted) return;
    setState(() {
      _results = processed;
      _running = false;
      _finished = true;
    });
  }

  Future<void> _retryFailed() async {
    final failed = [
      for (var i = 0; i < _results.length; i++)
        if (!_results[i].isProcessed) i,
    ];
    if (failed.isEmpty) return;

    setState(() {
      _running = true;
      _finished = false;
      _done = _results.length - failed.length;
    });

    final retried = await ImageProcessing.processAll(
      ref.read(catalogApiProvider),
      [for (final i in failed) _results[i]],
      onEach: (_) {
        if (mounted) setState(() => _done++);
      },
    );

    if (!mounted) return;
    final merged = List<ProcessedImage>.from(_results);
    for (var k = 0; k < failed.length; k++) {
      merged[failed[k]] = retried[k];
    }

    setState(() {
      _results = merged;
      _running = false;
      _finished = true;
    });
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    final failed = _results.where((r) => !r.isProcessed).length;

    return Dialog(
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(22)),
      insetPadding: const EdgeInsets.symmetric(horizontal: 20, vertical: 40),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 22, 20, 18),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Container(
                  width: 36,
                  height: 36,
                  decoration: BoxDecoration(
                    color: colors.softGreen,
                    borderRadius: BorderRadius.circular(10),
                  ),
                  child: const Icon(Icons.auto_fix_high_rounded, size: 19, color: AppTheme.brandGreen),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(
                    _finished ? l.imgpReady : l.imgpPreparing,
                    style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800, color: colors.ink),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              _finished
                  ? l.imgpDoneDesc
                  : l.imgpInProgress(_done, widget.files.length),
              style: TextStyle(fontSize: 12.5, color: colors.subtle, height: 1.45),
            ),
            const SizedBox(height: 16),

            if (_running)
              ClipRRect(
                borderRadius: const BorderRadius.all(Radius.circular(4)),
                child: LinearProgressIndicator(
                  minHeight: 4,
                  backgroundColor: colors.line,
                  // Une progression chiffrée plutôt qu'une barre qui tourne : le
                  // vendeur sait combien de temps il lui reste à patienter.
                  value: widget.files.isEmpty ? null : _done / widget.files.length,
                ),
              ),

            if (_results.isNotEmpty)
              Flexible(
                child: ListView.separated(
                  shrinkWrap: true,
                  padding: const EdgeInsets.only(top: 12),
                  itemCount: _results.length,
                  separatorBuilder: (_, __) => Divider(height: 22, color: colors.line),
                  itemBuilder: (_, i) => _BeforeAfter(image: _results[i]),
                ),
              ),

            if (_finished && failed > 0) ...[
              const SizedBox(height: 14),
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: AppTheme.promoOrange.withValues(alpha: 0.10),
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(color: AppTheme.promoOrange.withValues(alpha: 0.25)),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        const Icon(Icons.warning_amber_rounded, size: 18, color: AppTheme.promoOrange),
                        const SizedBox(width: 10),
                        Expanded(
                          child: Text(
                            failed == _results.length
                                ? l.imgpAllFailed
                                : l.imgpSomeFailed(failed),
                            style: TextStyle(fontSize: 12, color: colors.ink, height: 1.4),
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 8),
                    Align(
                      alignment: Alignment.centerLeft,
                      child: TextButton.icon(
                        onPressed: _retryFailed,
                        icon: const Icon(Icons.refresh, size: 17),
                        label: Text(l.imgpRetry),
                        style: TextButton.styleFrom(
                          foregroundColor: AppTheme.brandGreen,
                          padding: const EdgeInsets.symmetric(horizontal: 8),
                          visualDensity: VisualDensity.compact,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ],

            const SizedBox(height: 20),
            Row(
              children: [
                Expanded(
                  child: OutlinedButton(
                    onPressed: () => Navigator.pop(context, <ProcessedImage>[]),
                    child: Text(l.commonCancel),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: FilledButton(
                    onPressed: _finished ? () => Navigator.pop(context, _results) : null,
                    child: Text(l.imgpUse),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// Comparatif « avant / après » d'une photo.
class _BeforeAfter extends StatelessWidget {
  const _BeforeAfter({required this.image});
  final ProcessedImage image;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    return Row(
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        Expanded(child: _tile(l.imgpBefore, image.original, highlighted: false, colors: colors)),
        SizedBox(
          width: 34,
          child: Icon(Icons.arrow_forward_rounded, size: 18, color: colors.subtle),
        ),
        Expanded(
          child: image.isProcessed
              ? _tile(l.imgpAfter, image.processed!, highlighted: true, colors: colors)
              : _failedTile(colors, l.imgpNotProcessed),
        ),
      ],
    );
  }

  /// Les deux vignettes ont la MÊME taille et le même cadrage (`contain`) : sans
  /// cela, la comparaison serait faussée par la mise en page elle-même.
  Widget _tile(String label, Uint8List bytes, {required bool highlighted, required AppColors colors}) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        AspectRatio(
          aspectRatio: 1,
          child: Container(
            padding: const EdgeInsets.all(5),
            decoration: BoxDecoration(
              color: Colors.white,
              borderRadius: BorderRadius.circular(14),
              border: Border.all(
                color: highlighted ? AppTheme.brandGreen : colors.line,
                width: highlighted ? 1.6 : 1,
              ),
            ),
            child: ClipRRect(
              borderRadius: BorderRadius.circular(10),
              child: Image.memory(bytes, fit: BoxFit.contain, width: double.infinity),
            ),
          ),
        ),
        const SizedBox(height: 6),
        Text(
          label,
          style: TextStyle(
            fontSize: 11,
            fontWeight: FontWeight.w700,
            color: highlighted ? AppTheme.brandGreen : colors.subtle,
          ),
        ),
      ],
    );
  }

  Widget _failedTile(AppColors colors, String label) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        AspectRatio(
          aspectRatio: 1,
          child: Container(
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: colors.bg,
              borderRadius: BorderRadius.circular(14),
              border: Border.all(color: colors.line),
            ),
            child: Icon(Icons.image_not_supported_outlined, color: colors.subtle),
          ),
        ),
        const SizedBox(height: 6),
        Text(label,
            style: const TextStyle(fontSize: 11, color: AppTheme.promoOrange, fontWeight: FontWeight.w700)),
      ],
    );
  }
}
