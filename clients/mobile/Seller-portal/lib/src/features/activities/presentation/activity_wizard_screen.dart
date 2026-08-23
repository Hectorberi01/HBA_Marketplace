import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:image_picker/image_picker.dart';

import '../../../core/identity/seller_identity.dart';
import '../../../core/media/media_upload.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/partner_widgets.dart';
import '../../shop/shop_data.dart';
import '../activities_data.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// NOUVELLE ACTIVITÉ — quatre étapes.
///
/// CE QUE LA MAQUETTE DEMANDE ET QUE LE DOMAINE NE SAIT PAS PORTER.
///
/// Trois écarts ont été relevés avant d'écrire une ligne, parce que collecter une
/// valeur qu'aucune route n'accepte est le défaut le plus coûteux de ce dépôt : le
/// partenaire saisit, l'écran confirme, et rien n'est enregistré.
///
///   • TYPE DE CUISINE — « Cuisine ivoirienne · Fast-food · Grillades ·
///     Pâtisserie ». `Restaurant` n'a AUCUN champ de ce genre : ni colonne, ni
///     commande, ni contrat. Le champ est donc ABSENT de cet assistant plutôt que
///     présent et jeté. L'ajouter demande une colonne, une migration, une commande
///     et une projection — c'est un travail, pas un oubli d'affichage.
///
///   • ORANGE MONEY — `PayoutProvider` vaut `MtnMomo`, `MoovMoney`, `Wave`,
///     `BankAccount`, `Celtis`. La maquette est ivoirienne (Orange Money, +225,
///     Cocody Angré) ; la plateforme est béninoise. Les canaux proposés ici sont
///     ceux que le serveur accepte — proposer Orange Money produirait un 400
///     `invalid_provider` après saisie du numéro.
///
///   • ADRESSE EN UNE LIGNE — le domaine ne stocke pas du texte libre : il attend
///     un `FulfillmentLocationId`, créé dans Inventory avec commune, quartier,
///     repère. C'est aussi ainsi qu'une adresse se donne à Cotonou ou Calavi, et
///     c'est ce qu'un livreur saura suivre.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ÉTAPE 3 N'EST PAS « DE L'ACTIVITÉ », ET LE DIRE ÉVITE UN MALENTENDU CHER.
///
/// Documents légaux et compte de versement appartiennent au DOSSIER VENDEUR
/// (`/api/merchants/{sellerId}/…`), pas à la boutique ni au restaurant. Ils sont
/// donc communs à toutes vos activités : les redemander par activité laisserait
/// croire qu'il faut redéposer un RCCM par boutique. L'étape les AFFICHE, permet de
/// les compléter, et le dit.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// SEPT APPELS, AUCUNE TRANSACTION. LE RÉSUMÉ FINAL DOIT ÊTRE HONNÊTE.
///
/// Créer, déposer le logo, le rattacher, créer le lieu, le rattacher, poser les
/// horaires, enregistrer le versement : sept routes sur quatre services. Un échec
/// au cinquième laisse les quatre premiers appliqués.
///
/// On ne prétend donc PAS à un enregistrement atomique. L'activité est créée
/// d'abord — c'est le seul appel irremplaçable — puis chaque réglage est tenté et
/// RAPPORTÉ. Tout annuler sur un échec de réseau ferait ressaisir dix champs ;
/// afficher « échec » sur une activité déjà créée ferait créer un doublon.
/// ═════════════════════════════════════════════════════════════════════════════
class ActivityWizardScreen extends ConsumerStatefulWidget {
  const ActivityWizardScreen({super.key});

  @override
  ConsumerState<ActivityWizardScreen> createState() => _ActivityWizardScreenState();
}

class _ActivityWizardScreenState extends ConsumerState<ActivityWizardScreen> {
  static const _nbEtapes = 4;

  int _etape = 0;
  HbaUniverse? _type;

  final _nom = TextEditingController();
  final _description = TextEditingController();
  final _telephone = TextEditingController();
  final _commune = TextEditingController();
  final _quartier = TextEditingController();
  final _repere = TextEditingController();

  /// LES OCTETS, PAS LE CHEMIN. Sur iOS le fichier du sélecteur vit dans un
  /// cache que le système peut vider entre la sélection et la validation, quatre
  /// écrans plus loin.
  Uint8List? _logo;
  String? _logoNom;

  /// Une seule plage, appliquée aux SEPT jours.
  ///
  /// LE SERVEUR ATTEND UNE GRILLE PAR JOUR, et la maquette montre une seule
  /// ligne « 11:00 – 23:00 ». On développe donc la plage sur les sept jours plutôt
  /// que d'envoyer une grille incomplète — un jour absent de la grille est un jour
  /// FERMÉ, et le restaurateur ne comprendrait pas d'être fermé du mardi au
  /// dimanche. Les horaires par jour se règlent ensuite, dans les réglages.
  TimeOfDay _ouverture = const TimeOfDay(hour: 8, minute: 0);
  TimeOfDay _fermeture = const TimeOfDay(hour: 22, minute: 0);

  String? _payoutProvider;
  final _payoutNumero = TextEditingController();
  final _payoutTitulaire = TextEditingController();

  bool _envoi = false;
  String? _erreur;

  @override
  void dispose() {
    for (final c in [
      _nom, _description, _telephone, _commune, _quartier, _repere,
      _payoutNumero, _payoutTitulaire,
    ]) {
      c.dispose();
    }
    super.dispose();
  }

  bool get _estBoutique => _type == HbaUniverse.express;

  // ── Ce qui autorise à passer à l'étape suivante ─────────────────────────────
  //
  // CHAQUE ÉTAPE DIT CE QUI LUI MANQUE (voir `_manquants`). Un bouton
  // « Suivant » inerte sans motif fait relire tout le formulaire au hasard — et
  // `styleFrom(backgroundColor:)` peint le bouton désactivé comme un bouton actif,
  // ce qui le rend indistinguable d'une panne.
  List<String> get _manquants => switch (_etape) {
        0 => [if (_type == null) 'le type d\'activité'],
        1 => [
            if (_nom.text.trim().length < 2) 'un nom',
            if (_telephone.text.trim().length < 8) 'un téléphone',
            if (_commune.text.trim().isEmpty) 'une commune',
            if (_quartier.text.trim().isEmpty) 'un quartier',
          ],
        // L'ÉTAPE 3 N'EST JAMAIS BLOQUANTE : documents et versement relèvent du
        // dossier vendeur et peuvent se compléter plus tard. Les exiger ici
        // empêcherait de créer une boutique un dimanche soir faute de scanner.
        _ => const [],
      };

  bool get _peutAvancer => !_envoi && _manquants.isEmpty;

  // ── Médias ──────────────────────────────────────────────────────────────────

  Future<void> _choisirLogo() async {
    final f = await ImagePicker().pickImage(
      source: ImageSource.gallery,
      imageQuality: 90,
      // Le logo est affiché petit (avatar 40 px, vignette d'activité). 1024 suffit
      // largement, et le serveur refuse au-delà de 5 Mo.
      maxWidth: 1024,
      maxHeight: 1024,
    );
    if (f == null) return;
    final octets = await File(f.path).readAsBytes();
    if (!mounted) return;
    setState(() {
      _logo = octets;
      _logoNom = f.name;
    });
  }

  Future<void> _choisirHeure({required bool ouverture}) async {
    final choisie = await showTimePicker(
      context: context,
      initialTime: ouverture ? _ouverture : _fermeture,
    );
    if (choisie == null || !mounted) return;
    setState(() {
      if (ouverture) {
        _ouverture = choisie;
      } else {
        _fermeture = choisie;
      }
    });
  }

  String _hhmm(TimeOfDay t) =>
      '${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}';

  /// La plage unique, développée sur les sept jours.
  ///
  /// NOMS DE JOURS EN ANGLAIS INVARIANT : le serveur fait un
  /// `Enum.TryParse<DayOfWeek>`. « Lundi » rend 400 `food.restaurant.day_invalid`.
  List<Map<String, String>> get _grilleHoraire => [
        for (final jour in const [
          'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday',
        ])
          {'day': jour, 'opensAt': _hhmm(_ouverture), 'closesAt': _hhmm(_fermeture)},
      ];

  // ── La création ─────────────────────────────────────────────────────────────

  Future<void> _creer(String? sellerId) async {
    setState(() {
      _envoi = true;
      _erreur = null;
    });

    final api = ref.read(activitiesApiProvider);
    final medias = ref.read(mediaApiProvider);
    final nom = _nom.text.trim();
    final telephone = _telephone.text.trim();
    final description = _description.text.trim();

    // Ce qui n'a PAS pu être appliqué. Le résumé final le cite nommément : « créée,
    // mais sans logo » se répare en deux touches ; « échec » fait tout recommencer.
    final rates = <String>[];

    try {
      // ── 1. L'activité elle-même. Le seul appel irremplaçable. ─────────────
      final String activiteId;
      if (_estBoutique) {
        activiteId = await api.createStore(
          sellerId!,
          name: nom,
          contactPhone: telephone,
        );
      } else {
        activiteId = await api.registerRestaurant(
          name: nom,
          description: description.isEmpty ? null : description,
          phone: telephone,
        );
      }

      // ── 2. Le dossier de reversement, AVANT le lieu. ──────────────────────
      //
      // L'ORDRE EST IMPOSÉ PAR LE SERVEUR : `AttachRestaurantLocationAsync`
      // relit le lieu dans Inventory et vérifie qu'il appartient au vendeur de
      // reversement. Sans ce rattachement d'abord, le lieu est refusé — avec un
      // message qui parle du lieu, alors que la cause est ailleurs.
      if (!_estBoutique && sellerId != null) {
        try {
          await api.attachPayoutSeller(activiteId, sellerId: sellerId);
        } catch (_) {
          rates.add('le rattachement au dossier vendeur');
        }
      }

      // ── 3. Le lieu, puis son rattachement. ────────────────────────────────
      try {
        final lieuId = await api.createLocation(
          commune: _commune.text.trim(),
          quartier: _quartier.text.trim(),
          landmark: _repere.text.trim().isEmpty ? null : _repere.text.trim(),
          contactPhone: telephone,
        );
        if (_estBoutique) {
          await api.attachStoreLocation(sellerId!, storeId: activiteId, locationId: lieuId);
        } else {
          await api.attachRestaurantLocation(activiteId, locationId: lieuId);
        }
      } catch (_) {
        rates.add('l\'adresse');
      }

      // ── 4. Les horaires. ──────────────────────────────────────────────────
      try {
        if (_estBoutique) {
          await api.setStoreOpeningHours(
            sellerId!, storeId: activiteId, hours: _grilleHoraire);
        } else {
          await api.setRestaurantServiceHours(activiteId, hours: _grilleHoraire);
        }
      } catch (_) {
        rates.add('les horaires');
      }

      // ── 5. Le logo, s'il y en a un. ───────────────────────────────────────
      if (_logo != null) {
        try {
          final depose = await medias.uploadBytes(
            bytes: _logo!,
            fileName: _logoNom ?? 'logo.jpg',
            ownerType: _estBoutique ? MediaOwner.store : MediaOwner.restaurant,
            ownerId: activiteId,
            mediaType: _estBoutique ? MediaKind.storeMedia : MediaKind.restaurantMedia,
          );
          if (_estBoutique) {
            // LE NOM EST RENVOYÉ AVEC LE LOGO. `StoreProfileRequest` porte les
            // deux : omettre le nom le remplacerait par une chaîne vide.
            await api.setStoreProfile(
              sellerId!,
              storeId: activiteId,
              name: nom,
              logoUrl: depose.url,
              description: description.isEmpty ? null : description,
            );
          } else {
            await api.setRestaurantLogo(
              activiteId, mediaId: depose.mediaId, url: depose.url);
          }
        } catch (_) {
          rates.add('le logo');
        }
      }

      // ── 6. Le compte de versement, s'il a été renseigné. ──────────────────
      if (sellerId != null &&
          _payoutProvider != null &&
          _payoutNumero.text.trim().isNotEmpty) {
        try {
          await ref.read(shopApiProvider).setPayoutAccount(
                sellerId,
                provider: _payoutProvider!,
                accountNumber: _payoutNumero.text.trim(),
                accountName: _payoutTitulaire.text.trim().isEmpty
                    ? nom
                    : _payoutTitulaire.text.trim(),
              );
          ref.invalidate(shopProvider);
        } catch (_) {
          rates.add('le compte de versement');
        }
      }

      ref.invalidate(activitiesProvider);
      if (!mounted) return;

      // ON NOTIFIE AVANT DE QUITTER L'ÉCRAN, et le toast d'`AppNotify` est
      // `floating` avec une marge basse fixe : depuis un écran plein il est visible,
      // depuis une feuille clavier ouvert il ne l'était pas. C'est ce qui rendait la
      // version précédente de cet écran entièrement muette.
      AppNotify.success(
        context,
        rates.isEmpty
            ? (_estBoutique
                ? '« $nom » est créée.'
                : '« $nom » est déposé. Il sera vérifié avant sa mise en service.')
            : '« $nom » est créée, mais ${rates.join(', ')} n\'a pas pu être '
                'enregistré. À reprendre dans les réglages de l\'activité.',
      );
      context.pop();
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _envoi = false;
        _erreur = e.toString();
      });
    }
  }

  // ── Rendu ───────────────────────────────────────────────────────────────────

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final activites = ref.watch(activitiesProvider);
    final sellerId = ref.watch(sellerIdentityProvider).valueOrNull?.sellerId;

    return Scaffold(
      backgroundColor: colors.bg,
      appBar: AppBar(
        title: const Text('Nouvelle activité'),
        actions: [
          Padding(
            padding: const EdgeInsets.only(right: 18),
            child: Center(
              child: Text(
                '${_etape + 1} / $_nbEtapes',
                style: TextStyle(
                  fontSize: 13.5,
                  fontWeight: FontWeight.w700,
                  color: colors.subtle,
                ),
              ),
            ),
          ),
        ],
        bottom: PreferredSize(
          preferredSize: const Size.fromHeight(3),
          child: LinearProgressIndicator(
            value: (_etape + 1) / _nbEtapes,
            minHeight: 3,
            backgroundColor: colors.line,
            valueColor: AlwaysStoppedAnimation(_type?.accent ?? AppTheme.brandGreen),
          ),
        ),
      ),
      body: activites.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(
          message: e.toString(),
          onRetry: () => ref.invalidate(activitiesProvider),
        ),
        data: (resultat) {
          final aDejaUnRestaurant =
              resultat.data.any((a) => a.universe == HbaUniverse.food);

          return Column(
            children: [
              Expanded(
                child: ListView(
                  padding: const EdgeInsets.fromLTRB(20, 18, 20, 20),
                  children: [
                    switch (_etape) {
                      0 => _EtapeType(
                          choisi: _type,
                          restaurantIndisponible: aDejaUnRestaurant,
                          boutiqueIndisponible: sellerId == null,
                          onChoisir: (u) => setState(() => _type = u),
                        ),
                      1 => _EtapeInformations(
                          estBoutique: _estBoutique,
                          nom: _nom,
                          description: _description,
                          telephone: _telephone,
                          commune: _commune,
                          quartier: _quartier,
                          repere: _repere,
                          logo: _logo,
                          onLogo: _choisirLogo,
                          ouverture: _hhmm(_ouverture),
                          fermeture: _hhmm(_fermeture),
                          onHeure: (o) => _choisirHeure(ouverture: o),
                          onChanged: () => setState(() {}),
                        ),
                      2 => _EtapeDossier(
                          providerChoisi: _payoutProvider,
                          numero: _payoutNumero,
                          titulaire: _payoutTitulaire,
                          onProvider: (p) => setState(() => _payoutProvider = p),
                          onChanged: () => setState(() {}),
                        ),
                      _ => _EtapeResume(
                          estBoutique: _estBoutique,
                          nom: _nom.text.trim(),
                          logo: _logo,
                          commune: _commune.text.trim(),
                          quartier: _quartier.text.trim(),
                          horaires: '${_hhmm(_ouverture)} – ${_hhmm(_fermeture)}',
                          versement: _payoutProvider,
                        ),
                    },
                    if (_erreur != null) ...[
                      const SizedBox(height: 16),
                      _Bandeau(texte: _erreur!),
                    ],
                  ],
                ),
              ),
              _BarreNavigation(
                etape: _etape,
                dernier: _etape == _nbEtapes - 1,
                accent: _type?.accent ?? AppTheme.brandGreen,
                envoi: _envoi,
                manquants: _manquants,
                peutAvancer: _peutAvancer,
                onRetour: () => _etape == 0
                    ? context.pop()
                    : setState(() {
                        _etape--;
                        _erreur = null;
                      }),
                onSuivant: () => _etape == _nbEtapes - 1
                    ? _creer(sellerId)
                    : setState(() => _etape++),
              ),
            ],
          );
        },
      ),
    );
  }
}

// ═════════════════════════════════════════════════════════════════════════════
// ÉTAPE 1 — le type
// ═════════════════════════════════════════════════════════════════════════════

class _EtapeType extends StatelessWidget {
  const _EtapeType({
    required this.choisi,
    required this.restaurantIndisponible,
    required this.boutiqueIndisponible,
    required this.onChoisir,
  });

  final HbaUniverse? choisi;
  final bool restaurantIndisponible;
  final bool boutiqueIndisponible;
  final ValueChanged<HbaUniverse> onChoisir;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const _TitreEtape(
          titre: 'Type d\'activité',
          sous: 'Le choix détermine l\'univers HBA, les écrans disponibles et le '
              'mode de commande.',
        ),
        const SizedBox(height: 16),

        _CarteType(
          universe: HbaUniverse.express,
          titre: 'Boutique',
          detail: 'Produits, variantes, stock et livraison. Idéal pour un commerce '
              'physique ou en ligne.',
          selectionne: choisi == HbaUniverse.express,
          // SANS DOSSIER VENDEUR, LA ROUTE N'A PAS D'URL — elle est scopée
          // vendeur. Le dire vaut mieux qu'un 404 sur `/api/merchants/null/stores`.
          indisponible: boutiqueIndisponible
              ? 'Votre dossier vendeur n\'est pas encore résolu.'
              : null,
          onTap: () => onChoisir(HbaUniverse.express),
        ),
        const SizedBox(height: 12),

        _CarteType(
          universe: HbaUniverse.food,
          titre: 'Restaurant',
          detail: 'Menu, cuisine temps réel et temps de préparation. Pour un '
              'restaurant ou un maquis.',
          selectionne: choisi == HbaUniverse.food,
          // APPLIQUÉ AVANT L'APPEL, PAS APRÈS : le serveur répond 409
          // `food.restaurant.already_registered`. Laisser remplir quatre étapes
          // pour l'apprendre à la fin serait cruel.
          indisponible: restaurantIndisponible
              ? 'Ce compte a déjà un établissement. Un compte ne peut en gérer qu\'un.'
              : null,
          onTap: () => onChoisir(HbaUniverse.food),
        ),
        const SizedBox(height: 16),

        _Note(
          texte: 'Le type ne pourra plus être modifié après validation. '
              'Une même entreprise peut avoir plusieurs activités.',
          couleur: AppTheme.info,
          fond: const Color(0xFFEAF1FE),
          colors: colors,
        ),
      ],
    );
  }
}

class _CarteType extends StatelessWidget {
  const _CarteType({
    required this.universe,
    required this.titre,
    required this.detail,
    required this.selectionne,
    required this.indisponible,
    required this.onTap,
  });

  final HbaUniverse universe;
  final String titre;
  final String detail;
  final bool selectionne;
  final String? indisponible;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final bloque = indisponible != null;

    return Opacity(
      opacity: bloque ? 0.6 : 1,
      child: InkWell(
        onTap: bloque ? null : onTap,
        borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        child: PartnerCard(
          borderColor: selectionne ? universe.accent : null,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Container(
                    width: 40,
                    height: 40,
                    alignment: Alignment.center,
                    decoration: BoxDecoration(
                      color: universe.soft,
                      borderRadius: BorderRadius.circular(12),
                    ),
                    child: Icon(universe.tabIcon, size: 20, color: universe.accent),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(titre,
                            style: const TextStyle(
                                fontSize: 15.5, fontWeight: FontWeight.w800)),
                        const SizedBox(height: 2),
                        Text(
                          universe.badge,
                          style: TextStyle(
                            fontSize: 11,
                            fontWeight: FontWeight.w800,
                            letterSpacing: 0.5,
                            color: universe.accent,
                          ),
                        ),
                      ],
                    ),
                  ),
                  Icon(
                    selectionne
                        ? Icons.radio_button_checked
                        : Icons.radio_button_unchecked,
                    color: selectionne ? universe.accent : colors.line,
                  ),
                ],
              ),
              const SizedBox(height: 10),
              Text(
                bloque ? indisponible! : detail,
                style: TextStyle(
                  fontSize: 12.5,
                  height: 1.45,
                  fontWeight: bloque ? FontWeight.w600 : FontWeight.w400,
                  color: bloque ? AppTheme.slate : colors.subtle,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

// ═════════════════════════════════════════════════════════════════════════════
// ÉTAPE 2 — les informations
// ═════════════════════════════════════════════════════════════════════════════

class _EtapeInformations extends StatelessWidget {
  const _EtapeInformations({
    required this.estBoutique,
    required this.nom,
    required this.description,
    required this.telephone,
    required this.commune,
    required this.quartier,
    required this.repere,
    required this.logo,
    required this.onLogo,
    required this.ouverture,
    required this.fermeture,
    required this.onHeure,
    required this.onChanged,
  });

  final bool estBoutique;
  final TextEditingController nom;
  final TextEditingController description;
  final TextEditingController telephone;
  final TextEditingController commune;
  final TextEditingController quartier;
  final TextEditingController repere;
  final Uint8List? logo;
  final VoidCallback onLogo;
  final String ouverture;
  final String fermeture;
  final void Function(bool ouverture) onHeure;
  final VoidCallback onChanged;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _TitreEtape(
          titre: 'Informations',
          sous: estBoutique
              ? 'Ce que les clients verront dans HBAExpress.'
              : 'Ce que les clients verront dans HBA Food.',
        ),
        const SizedBox(height: 16),

        Row(
          children: [
            InkWell(
              onTap: onLogo,
              borderRadius: BorderRadius.circular(12),
              child: Container(
                width: 64,
                height: 64,
                clipBehavior: Clip.antiAlias,
                decoration: BoxDecoration(
                  color: colors.bg,
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(color: colors.line),
                ),
                child: logo == null
                    ? Icon(Icons.add_photo_alternate_outlined,
                        size: 22, color: colors.subtle)
                    : Image.memory(logo!, fit: BoxFit.cover),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Text(
                // « FACULTATIF » EST DIT, contrairement à la photo d'un plat qui
                // est obligatoire. Un logo absent n'empêche aucune vente : le
                // sélecteur d'activité se rabat sur les initiales.
                'Logo carré, 512 × 512 px minimum. Facultatif — les initiales '
                'servent de repli.',
                style: TextStyle(fontSize: 12.5, height: 1.4, color: colors.subtle),
              ),
            ),
          ],
        ),
        const SizedBox(height: 18),

        TextField(
          controller: nom,
          textCapitalization: TextCapitalization.words,
          onChanged: (_) => onChanged(),
          decoration: InputDecoration(
            labelText: estBoutique ? 'Nom de la boutique' : 'Nom du restaurant',
            hintText: estBoutique ? 'Fatou Commerce — Calavi' : 'Maquis du Plateau',
          ),
        ),

        // LA DESCRIPTION N'EXISTE QU'À LA CRÉATION D'UN RESTAURANT.
        // `RegisterRestaurantRequest(Name, Description, Phone)` la porte ;
        // `CreateStoreRequest(Name, ContactPhone, ContactEmail)` non — côté
        // boutique elle passe par le profil, donc seulement si un logo est déposé.
        // La proposer partout ferait saisir un texte parfois jeté.
        if (!estBoutique) ...[
          const SizedBox(height: 14),
          TextField(
            controller: description,
            maxLines: 2,
            textCapitalization: TextCapitalization.sentences,
            decoration: const InputDecoration(
              labelText: 'Description (facultatif)',
              hintText: 'Cuisine béninoise, plats du jour, service rapide',
            ),
          ),
        ],
        const SizedBox(height: 14),

        TextField(
          controller: telephone,
          keyboardType: TextInputType.phone,
          inputFormatters: [FilteringTextInputFormatter.allow(RegExp(r'[0-9+ ]'))],
          onChanged: (_) => onChanged(),
          decoration: const InputDecoration(
            labelText: 'Téléphone de l\'activité',
            hintText: '+229 01 97 00 00 00',
          ),
        ),
        const SizedBox(height: 22),

        PartnerSectionTitle('Adresse'),
        const SizedBox(height: 6),
        Text(
          // ON EXPLIQUE POURQUOI CE N'EST PAS UNE LIGNE LIBRE. Sinon la
          // décomposition passe pour une lourdeur administrative.
          'C\'est de là que partent vos commandes. Le livreur suivra ces repères.',
          style: TextStyle(fontSize: 12.5, height: 1.4, color: colors.subtle),
        ),
        const SizedBox(height: 12),

        TextField(
          controller: commune,
          textCapitalization: TextCapitalization.words,
          onChanged: (_) => onChanged(),
          decoration: const InputDecoration(
            labelText: 'Commune',
            hintText: 'Abomey-Calavi',
          ),
        ),
        const SizedBox(height: 14),
        TextField(
          controller: quartier,
          textCapitalization: TextCapitalization.words,
          onChanged: (_) => onChanged(),
          decoration: const InputDecoration(
            labelText: 'Quartier',
            hintText: 'Tankpè',
          ),
        ),
        const SizedBox(height: 14),
        TextField(
          controller: repere,
          textCapitalization: TextCapitalization.sentences,
          decoration: const InputDecoration(
            labelText: 'Repère (facultatif)',
            hintText: 'Près du carrefour Aïtchédji, en face de la pharmacie',
          ),
        ),
        const SizedBox(height: 22),

        PartnerSectionTitle('Horaires d\'ouverture'),
        const SizedBox(height: 6),
        Text(
          // ON ANNONCE LE DÉVELOPPEMENT SUR SEPT JOURS, parce que le serveur
          // traite un jour absent de la grille comme FERMÉ. Ne rien dire ferait
          // découvrir un mardi fermé une semaine plus tard.
          'Appliqués aux sept jours. Les horaires par jour se règlent ensuite.',
          style: TextStyle(fontSize: 12.5, height: 1.4, color: colors.subtle),
        ),
        const SizedBox(height: 12),
        Row(
          children: [
            Expanded(child: _ChampHeure(label: 'Ouverture', valeur: ouverture, onTap: () => onHeure(true))),
            const SizedBox(width: 12),
            Expanded(child: _ChampHeure(label: 'Fermeture', valeur: fermeture, onTap: () => onHeure(false))),
          ],
        ),
      ],
    );
  }
}

class _ChampHeure extends StatelessWidget {
  const _ChampHeure({required this.label, required this.valeur, required this.onTap});

  final String label;
  final String valeur;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(12),
      child: InputDecorator(
        decoration: InputDecoration(labelText: label),
        child: Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(valeur, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600)),
            Icon(Icons.schedule, size: 18, color: colors.subtle),
          ],
        ),
      ),
    );
  }
}

// ═════════════════════════════════════════════════════════════════════════════
// ÉTAPE 3 — documents & versement
// ═════════════════════════════════════════════════════════════════════════════

class _EtapeDossier extends ConsumerWidget {
  const _EtapeDossier({
    required this.providerChoisi,
    required this.numero,
    required this.titulaire,
    required this.onProvider,
    required this.onChanged,
  });

  final String? providerChoisi;
  final TextEditingController numero;
  final TextEditingController titulaire;
  final ValueChanged<String> onProvider;
  final VoidCallback onChanged;

  /// LES VALEURS EXACTES DE `PayoutProvider`, ET C'EST LE BÉNIN.
  ///
  /// `MtnMomo · MoovMoney · Wave · BankAccount · Celtis`. Orange Money n'y est pas :
  /// il n'opère pas au Bénin, et l'inscrire produirait un 400 après que le
  /// partenaire a saisi son numéro. Le serveur parse le nom tel quel.
  static const _providers = <({String code, String label})>[
    (code: 'MtnMomo', label: 'MTN MoMo'),
    (code: 'MoovMoney', label: 'Moov Money'),
    (code: 'Celtis', label: 'Celtiis Cash'),
    (code: 'Wave', label: 'Wave'),
    (code: 'BankAccount', label: 'Virement bancaire'),
  ];

  /// Les quatre types du domaine (`KybDocumentType`), avec leur nom béninois.
  static const _documents = <({String code, String label, String detail})>[
    (code: 'BusinessRegistry', label: 'RCCM', detail: 'Registre du commerce'),
    (code: 'IdCard', label: 'Pièce d\'identité', detail: 'CNI ou passeport du gérant'),
    (code: 'TaxId', label: 'IFU', detail: 'Identifiant fiscal unique'),
    (code: 'ProofOfAddress', label: 'Justificatif d\'adresse', detail: 'Facture de moins de 3 mois'),
  ];

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final boutique = ref.watch(shopProvider);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const _TitreEtape(
          titre: 'Documents & paiement',
          sous: 'L\'activité est vérifiée par HBA sous 48 h. Vous pouvez la créer '
              'avant validation.',
        ),
        const SizedBox(height: 12),

        // LE PLUS IMPORTANT DE CET ÉCRAN, ET IL TIENT EN UNE PHRASE.
        _Note(
          texte: 'Ces pièces et ce compte appartiennent à votre dossier vendeur : '
              'ils valent pour TOUTES vos activités. Rien à redéposer par boutique.',
          couleur: AppTheme.info,
          fond: const Color(0xFFEAF1FE),
          colors: colors,
        ),
        const SizedBox(height: 18),

        boutique.when(
          loading: () => const Padding(
            padding: EdgeInsets.symmetric(vertical: 24),
            child: Center(child: CircularProgressIndicator(strokeWidth: 2)),
          ),
          // UN DOSSIER ILLISIBLE NE BLOQUE PAS LA CRÉATION. L'étape est
          // facultative ; échouer ici empêcherait de créer une activité pour une
          // information qu'on ne fait qu'AFFICHER.
          error: (_, __) => Text(
            'Vos pièces ne sont pas consultables pour le moment. '
            'Vous pourrez les vérifier depuis votre boutique.',
            style: TextStyle(fontSize: 12.5, height: 1.45, color: colors.subtle),
          ),
          data: (shop) {
            final fournis = shop.documents.map((d) => d.type.toLowerCase()).toSet();

            return PartnerCard(
              child: Column(
                children: [
                  for (var i = 0; i < _documents.length; i++) ...[
                    if (i > 0) Divider(height: 20, color: colors.line),
                    Row(
                      children: [
                        Icon(Icons.description_outlined, size: 20, color: colors.subtle),
                        const SizedBox(width: 12),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(_documents[i].label,
                                  style: const TextStyle(
                                      fontSize: 14, fontWeight: FontWeight.w700)),
                              const SizedBox(height: 2),
                              Text(_documents[i].detail,
                                  style: TextStyle(fontSize: 12, color: colors.subtle)),
                            ],
                          ),
                        ),
                        fournis.contains(_documents[i].code.toLowerCase())
                            ? PartnerStatusDot(
                                label: 'Fourni',
                                color: AppTheme.brandGreen,
                                background: AppTheme.brandGreenSoft,
                              )
                            : PartnerStatusDot(
                                label: 'À fournir',
                                color: AppTheme.promoOrange,
                                background: AppTheme.foodAmberSoft,
                              ),
                      ],
                    ),
                  ],
                ],
              ),
            );
          },
        ),
        const SizedBox(height: 8),
        Text(
          // ON DIT OÙ SE FAIT LE DÉPÔT PLUTÔT QUE DE LE REFAIRE ICI.
          // L'écran boutique le porte déjà, avec la suppression et la relecture
          // d'une pièce. Le dupliquer dans un assistant donnerait un second endroit
          // à maintenir, et un seul des deux serait corrigé.
          'Le dépôt et le remplacement des pièces se font depuis votre boutique.',
          style: TextStyle(fontSize: 12, height: 1.4, color: colors.subtle),
        ),
        const SizedBox(height: 22),

        PartnerSectionTitle('Compte de versement'),
        const SizedBox(height: 10),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final p in _providers)
              ChoiceChip(
                label: Text(p.label),
                selected: providerChoisi == p.code,
                onSelected: (_) => onProvider(p.code),
              ),
          ],
        ),

        if (providerChoisi != null) ...[
          const SizedBox(height: 14),
          TextField(
            controller: numero,
            keyboardType: providerChoisi == 'BankAccount'
                ? TextInputType.text
                : TextInputType.phone,
            onChanged: (_) => onChanged(),
            decoration: InputDecoration(
              labelText: providerChoisi == 'BankAccount'
                  ? 'Numéro de compte / IBAN'
                  : 'Numéro mobile money',
              hintText: providerChoisi == 'BankAccount' ? 'BJ66 …' : '+229 01 97 00 00 00',
            ),
          ),
          const SizedBox(height: 14),
          TextField(
            controller: titulaire,
            textCapitalization: TextCapitalization.words,
            decoration: const InputDecoration(
              labelText: 'Titulaire du compte',
              helperText: 'À défaut, le nom de l\'activité sera utilisé.',
            ),
          ),
        ],

        const SizedBox(height: 12),
        Text(
          // IL N'Y A QU'UN COMPTE DE VERSEMENT PAR VENDEUR, et la maquette en
          // montrait trois au choix. En renseigner un ici REMPLACE le précédent :
          // le taire ferait perdre un compte sans avertissement.
          'Un seul compte de versement par dossier vendeur : celui-ci remplacera '
          'le précédent. Laissez vide pour n\'y rien changer.',
          style: TextStyle(fontSize: 12, height: 1.4, color: colors.subtle),
        ),
      ],
    );
  }
}

// ═════════════════════════════════════════════════════════════════════════════
// ÉTAPE 4 — le résumé
// ═════════════════════════════════════════════════════════════════════════════

class _EtapeResume extends StatelessWidget {
  const _EtapeResume({
    required this.estBoutique,
    required this.nom,
    required this.logo,
    required this.commune,
    required this.quartier,
    required this.horaires,
    required this.versement,
  });

  final bool estBoutique;
  final String nom;
  final Uint8List? logo;
  final String commune;
  final String quartier;
  final String horaires;
  final String? versement;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final universe = estBoutique ? HbaUniverse.express : HbaUniverse.food;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const _TitreEtape(
          titre: 'Résumé',
          sous: 'Vérifiez avant de créer l\'activité.',
        ),
        const SizedBox(height: 16),

        PartnerCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Container(
                    width: 46,
                    height: 46,
                    clipBehavior: Clip.antiAlias,
                    decoration: BoxDecoration(
                      color: colors.bg,
                      borderRadius: BorderRadius.circular(12),
                    ),
                    child: logo == null
                        ? Icon(Icons.storefront_outlined, size: 20, color: colors.subtle)
                        : Image.memory(logo!, fit: BoxFit.cover),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(nom,
                            style: const TextStyle(
                                fontSize: 15.5, fontWeight: FontWeight.w800)),
                        const SizedBox(height: 4),
                        PartnerStatusDot(
                          label: universe.badge,
                          color: universe.accent,
                          background: universe.soft,
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              Divider(height: 24, color: colors.line),
              _Ligne(cle: 'Type', valeur: estBoutique ? 'Boutique' : 'Restaurant'),
              _Ligne(cle: 'Adresse', valeur: '$quartier, $commune'),
              _Ligne(cle: 'Horaires', valeur: horaires),
              _Ligne(
                cle: 'Versement',
                valeur: versement == null ? 'Inchangé' : _libelle(versement!),
              ),
            ],
          ),
        ),
        const SizedBox(height: 16),

        _Note(
          texte: estBoutique
              ? 'La boutique sera créée immédiatement. Elle ne vendra qu\'une fois '
                  'ouverte et son catalogue rempli.'
              : 'L\'établissement sera créé en statut « En attente de vérification ». '
                  'Vous pourrez préparer votre carte, mais pas recevoir de commandes '
                  'avant validation.',
          couleur: AppTheme.promoOrange,
          fond: AppTheme.foodAmberSoft,
          colors: colors,
        ),
      ],
    );
  }

  static String _libelle(String code) => switch (code) {
        'MtnMomo' => 'MTN MoMo',
        'MoovMoney' => 'Moov Money',
        'Celtis' => 'Celtiis Cash',
        'Wave' => 'Wave',
        'BankAccount' => 'Virement bancaire',
        _ => code,
      };
}

class _Ligne extends StatelessWidget {
  const _Ligne({required this.cle, required this.valeur});

  final String cle;
  final String valeur;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Text(cle, style: TextStyle(fontSize: 13, color: colors.subtle)),
          ),
          const SizedBox(width: 16),
          Expanded(
            flex: 2,
            child: Text(
              valeur,
              textAlign: TextAlign.right,
              style: const TextStyle(fontSize: 13.5, fontWeight: FontWeight.w700),
            ),
          ),
        ],
      ),
    );
  }
}

// ═════════════════════════════════════════════════════════════════════════════
// Éléments partagés
// ═════════════════════════════════════════════════════════════════════════════

class _TitreEtape extends StatelessWidget {
  const _TitreEtape({required this.titre, required this.sous});

  final String titre;
  final String sous;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(titre, style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w800)),
        const SizedBox(height: 6),
        Text(sous, style: TextStyle(fontSize: 13, height: 1.45, color: colors.subtle)),
      ],
    );
  }
}

class _Note extends StatelessWidget {
  const _Note({
    required this.texte,
    required this.couleur,
    required this.fond,
    required this.colors,
  });

  final String texte;
  final Color couleur;
  final Color fond;
  final AppColors colors;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
        decoration: BoxDecoration(
          color: fond,
          borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        ),
        child: Text(
          texte,
          style: TextStyle(fontSize: 12.5, height: 1.45, color: couleur),
        ),
      );
}

class _Bandeau extends StatelessWidget {
  const _Bandeau({required this.texte});

  final String texte;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
        decoration: BoxDecoration(
          color: AppTheme.dangerSoft,
          borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Icon(Icons.error_outline, size: 18, color: AppTheme.danger),
            const SizedBox(width: 10),
            Expanded(
              child: Text(
                texte,
                style: const TextStyle(
                  fontSize: 12.5,
                  height: 1.45,
                  fontWeight: FontWeight.w600,
                  color: AppTheme.danger,
                ),
              ),
            ),
          ],
        ),
      );
}

/// La barre du bas : retour, motif du blocage, et le bouton d'avancement.
class _BarreNavigation extends StatelessWidget {
  const _BarreNavigation({
    required this.etape,
    required this.dernier,
    required this.accent,
    required this.envoi,
    required this.manquants,
    required this.peutAvancer,
    required this.onRetour,
    required this.onSuivant,
  });

  final int etape;
  final bool dernier;
  final Color accent;
  final bool envoi;
  final List<String> manquants;
  final bool peutAvancer;
  final VoidCallback onRetour;
  final VoidCallback onSuivant;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    final texte = manquants.isEmpty
        ? null
        : manquants.length == 1
            ? manquants.first
            : '${manquants.sublist(0, manquants.length - 1).join(', ')} '
                'et ${manquants.last}';

    return Container(
      padding: const EdgeInsets.fromLTRB(20, 12, 20, 20),
      decoration: BoxDecoration(
        color: colors.surface,
        border: Border(top: BorderSide(color: colors.line)),
      ),
      child: SafeArea(
        top: false,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            // LE MOTIF EST ÉCRIT AU-DESSUS DU BOUTON QUI REFUSE. Un « Suivant »
            // inerte peint en couleur pleine — ce que fait `styleFrom` sans
            // `disabledBackgroundColor` — est indistinguable d'une panne.
            if (texte != null && !envoi) ...[
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Icon(Icons.error_outline, size: 16, color: AppTheme.danger),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      'Il manque $texte.',
                      style: const TextStyle(
                        fontSize: 12.5,
                        height: 1.4,
                        fontWeight: FontWeight.w600,
                        color: AppTheme.danger,
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 10),
            ],
            Row(
              children: [
                Expanded(
                  child: OutlinedButton(
                    onPressed: envoi ? null : onRetour,
                    style: OutlinedButton.styleFrom(
                      minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
                      side: BorderSide(color: colors.line),
                    ),
                    child: const Text('Retour'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  flex: 2,
                  child: FilledButton(
                    onPressed: peutAvancer ? onSuivant : null,
                    style: FilledButton.styleFrom(
                      backgroundColor: accent,
                      disabledBackgroundColor: colors.line,
                      disabledForegroundColor: colors.subtle,
                      minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
                    ),
                    child: envoi
                        ? const SizedBox(
                            width: 20,
                            height: 20,
                            child: CircularProgressIndicator(
                                strokeWidth: 2, color: Colors.white),
                          )
                        : Text(dernier ? 'Créer l\'activité' : 'Suivant'),
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
