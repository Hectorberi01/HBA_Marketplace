import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/commune_field.dart';
import '../../../shared/widgets/partner_widgets.dart';
import '../../inventory/inventory_data.dart';
import '../offers_data.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// LIEUX D'EXPÉDITION — `/api/inventory/owners/{sellerId}/locations`.
///
/// CET ÉCRAN A LONGTEMPS ÉTÉ CLASSÉ « SANS AMONT », ET C'ÉTAIT UN DIAGNOSTIC
///    FAUX.
///
/// Il portait la mention `NotMigrated('offers')`, avec l'explication que « les
/// lieux appartiennent à Products/Offers, qui n'est pas extrait ». C'était
/// inexact : un lieu d'expédition est un `FulfillmentLocation`
/// d'inventory-service, et la LECTURE y allait déjà — `locationsProvider`
/// appelle `/api/inventory/owners/{id}/locations` depuis le début.
///
/// Ce qui manquait n'était pas le module, c'était le droit d'écrire : les
/// créations vivaient sous `MapAdminGroup`. Depuis VEN11, elles sont dans un
/// groupe vendeur gardé par `DenyUnlessOwnerAsync`.
///
/// La leçon vaut d'être écrite : une absence attribuée au mauvais module
/// condamne un écran bien plus longtemps que le manque réel.
///
/// CE QUI RESTE VRAI DE L'ANCIEN COMMENTAIRE : le lien entre un lieu et une
/// OFFRE (`shipFromLocationId`) appartient bien à Products/Offers, non extrait.
/// On peut donc déclarer ses lieux et y poser du stock ; on ne peut pas encore
/// dire « cette offre part de ce lieu-là ».
///
/// LE POINT DE REPÈRE N'EST PAS UN ORNEMENT.
///
/// C'est ce que LIT le coursier venu chercher le colis. Au Bénin, une grande
/// partie des rues n'ont ni nom ni numéro : « en face de la pharmacie
/// Sainte-Rita » est l'adresse réelle, et la ligne d'adresse ne l'est pas. C'est
/// pourquoi il est obligatoire ici alors que le service l'accepte vide.
/// ═════════════════════════════════════════════════════════════════════════════
class ShippingLocationsScreen extends ConsumerWidget {
  const ShippingLocationsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final async = ref.watch(locationsProvider);

    return Scaffold(
      backgroundColor: colors.bg,
      appBar: AppBar(title: const Text('Lieux d\'expédition')),
      body: async.when(
        loading: () => const LoadingView(),

        // « PAS DE BOUTIQUE » ARRIVE ICI AUSSI : `locationsProvider` passe par
        // `requiredSellerIdProvider`, qui lève une erreur NOMMÉE plutôt que
        // d'envoyer un identifiant vide et de récolter un 404 incompréhensible.
        error: (e, _) => ErrorView(
          message: e.toString(),
          onRetry: () => ref.invalidate(locationsProvider),
        ),
        data: (lieux) => RefreshIndicator(
          onRefresh: () async => ref.invalidate(locationsProvider),
          child: lieux.isEmpty
              ? ListView(
                  children: const [
                    SizedBox(height: 80),
                    PartnerEmptyState(
                      icon: Icons.location_on_outlined,
                      message: 'Aucun lieu de retrait enregistré.\n'
                          'Le coursier a besoin d\'un point de repère pour venir '
                          'chercher vos colis.',
                    ),
                  ],
                )
              : ListView.separated(
                  padding: const EdgeInsets.fromLTRB(16, 16, 16, 96),
                  itemCount: lieux.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 12),
                  itemBuilder: (_, i) => _LocationCard(location: lieux[i]),
                ),
        ),
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _ouvrirCreation(context, ref),
        backgroundColor: AppTheme.brandGreen,
        foregroundColor: Colors.white,
        icon: const Icon(Icons.add, size: 20),
        label: const Text(
          'Ajouter un lieu',
          style: TextStyle(fontSize: 14.5, fontWeight: FontWeight.w700),
        ),
      ),
    );
  }

  Future<void> _ouvrirCreation(BuildContext context, WidgetRef ref) async {
    final saisie = await showModalBottomSheet<_NouveauLieu>(
      context: context,
      isScrollControlled: true,
      builder: (_) => const _CreateLocationSheet(),
    );

    if (saisie == null || !context.mounted) return;

    try {
      await ref.read(inventoryApiProvider).createLocation(
            // `SellerAddress`, ET LE COMMENTAIRE PRÉCÉDENT SE TROMPAIT.
            //
            // Il affirmait que `'Warehouse'` était « la valeur attendue par le
            // domaine ». `FulfillmentLocationType` n'a que deux membres :
            // `SellerAddress` et `PlatformWarehouse`. Toute création partait donc
            // en 400 — « Type de lieu invalide » — et sans lieu d'expédition,
            // aucune mise en vente n'est possible : la faute bloquait toute la
            // chaîne, pour une chaîne de caractères.
            //
            // C'est bien `SellerAddress` qu'il faut : `PlatformWarehouse` est un
            // entrepôt HBA, et le domaine lui IMPOSE un `OwnerId` nul (cf.
            // `FulfillmentLocation.Create`). L'envoyer depuis l'espace vendeur
            // créerait un lieu qui n'appartient à personne — donc invisible dans
            // sa propre liste, et hors de portée de la garde d'appartenance.
            type: 'SellerAddress',
            commune: saisie.communeCode,
            quartier: saisie.quartier,
            landmark: saisie.landmark,
            contactPhone: saisie.phone,
          );
      ref.invalidate(locationsProvider);
      if (context.mounted) AppNotify.success(context, 'Lieu enregistré.');
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
    }
  }
}

class _LocationCard extends ConsumerWidget {
  const _LocationCard({required this.location});

  final ShipLocation location;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);

    return PartnerCard(
      child: Row(
        children: [
          Icon(Icons.location_on_outlined, size: 20, color: colors.subtle),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              location.label,
              style: TextStyle(
                fontSize: 14.5,
                fontWeight: FontWeight.w600,
                height: 1.35,
                color: colors.ink,
              ),
            ),
          ),
          IconButton(
            icon: const Icon(Icons.delete_outline, size: 20, color: AppTheme.danger),
            onPressed: () => _supprimer(context, ref),
          ),
        ],
      ),
    );
  }

  Future<void> _supprimer(BuildContext context, WidgetRef ref) async {
    final confirme = await showDialog<bool>(
      context: context,
      builder: (d) => AlertDialog(
        title: const Text('Supprimer ce lieu ?'),
        // ON ANNONCE LE REFUS POSSIBLE PLUTÔT QUE DE LE SUBIR. Le service
        // rejette la suppression d'un lieu qui porte encore du stock ; le dire
        // ici évite qu'un conflit passe pour une panne.
        content: Text(
          '${location.label}\n\n'
          'La suppression est refusée si du stock y est encore enregistré : '
          'videz-le d\'abord.',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(d).pop(false), child: const Text('Annuler')),
          TextButton(
            onPressed: () => Navigator.of(d).pop(true),
            style: TextButton.styleFrom(foregroundColor: AppTheme.danger),
            child: const Text('Supprimer'),
          ),
        ],
      ),
    );

    if (confirme != true || !context.mounted) return;

    try {
      await ref.read(inventoryApiProvider).deleteLocation(location.id);
      ref.invalidate(locationsProvider);
      if (context.mounted) AppNotify.success(context, 'Lieu supprimé.');
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
    }
  }
}

/// Ce que la feuille de saisie rend à l'écran.
class _NouveauLieu {
  const _NouveauLieu({
    required this.communeCode,
    required this.landmark,
    this.quartier,
    this.phone,
  });

  final String communeCode;
  final String landmark;
  final String? quartier;
  final String? phone;
}

class _CreateLocationSheet extends StatefulWidget {
  const _CreateLocationSheet();

  @override
  State<_CreateLocationSheet> createState() => _CreateLocationSheetState();
}

class _CreateLocationSheetState extends State<_CreateLocationSheet> {
  final _landmark = TextEditingController();
  final _quartier = TextEditingController();
  final _phone = TextEditingController();
  String? _commune;

  @override
  void dispose() {
    _landmark.dispose();
    _quartier.dispose();
    _phone.dispose();
    super.dispose();
  }

  /// LA COMMUNE ET LE REPÈRE SONT EXIGÉS ; LE RESTE NON.
  ///
  /// Sans commune, aucun routage ni aucune zone de livraison n'est calculable.
  /// Sans repère, le coursier n'a rien à chercher. Le quartier et le téléphone
  /// aident, ils ne conditionnent rien.
  bool get _valide => _commune != null && _landmark.text.trim().length >= 3;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Padding(
      padding: EdgeInsets.fromLTRB(20, 20, 20, MediaQuery.of(context).viewInsets.bottom + 24),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Nouveau lieu de retrait',
              style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800),
            ),
            const SizedBox(height: 6),
            Text(
              'C\'est ici que le coursier viendra chercher vos colis.',
              style: TextStyle(fontSize: 13.5, height: 1.4, color: colors.subtle),
            ),
            const SizedBox(height: 18),

            CommuneField(
              selectedCode: _commune,
              onSelected: (code) => setState(() => _commune = code),
            ),
            const SizedBox(height: 14),

            TextField(
              controller: _landmark,
              textCapitalization: TextCapitalization.sentences,
              onChanged: (_) => setState(() {}),
              decoration: const InputDecoration(
                labelText: 'Point de repère',
                hintText: 'En face de la pharmacie Sainte-Rita',
                helperText: 'C\'est ce que le coursier cherchera sur place.',
              ),
            ),
            const SizedBox(height: 14),

            TextField(
              controller: _quartier,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(
                labelText: 'Quartier (facultatif)',
                hintText: 'Cadjèhoun',
              ),
            ),
            const SizedBox(height: 14),

            TextField(
              controller: _phone,
              keyboardType: TextInputType.phone,
              decoration: const InputDecoration(
                labelText: 'Téléphone sur place (facultatif)',
                helperText: 'Appelé si le coursier ne trouve pas.',
              ),
            ),
            const SizedBox(height: 22),

            SizedBox(
              width: double.infinity,
              child: FilledButton(
                onPressed: _valide
                    ? () => Navigator.of(context).pop(_NouveauLieu(
                          communeCode: _commune!,
                          landmark: _landmark.text.trim(),
                          quartier: _quartier.text.trim().isEmpty ? null : _quartier.text.trim(),
                          phone: _phone.text.trim().isEmpty ? null : _phone.text.trim(),
                        ))
                    : null,
                style: FilledButton.styleFrom(
                  minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
                ),
                child: const Text('Enregistrer'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
