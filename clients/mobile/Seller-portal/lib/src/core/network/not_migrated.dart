import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../theme/app_theme.dart';
import 'api_exception.dart';

/// Marque ce dont l'amont n'existe pas encore sur la passerelle HBA.
///
/// ═══════════════════════════════════════════════════════════════════════════
/// POURQUOI UN ÉCRAN QUI L'ANNONCE PLUTÔT QU'UNE MAQUETTE QUI CONTINUE.
///
/// Plusieurs écrans de cette application n'ont pas encore d'amont — la liste
/// exacte est `pendingModules`, plus bas, et elle seule fait foi.
/// Ils affichaient jusqu'ici des données inventées — un chiffre d'affaires du
/// jour, une file d'expéditions, un donut de statuts — bâties pour la maquette.
///
/// La tentation est de les laisser tourner sur ces données : ça compile, ça se
/// démontre, et « on rebranchera ». Mais dès que les écrans VOISINS parleront au
/// vrai serveur, rien ne distinguera plus un chiffre réel d'un chiffre fabriqué.
/// Un vendeur qui lit « 534 500 F CFA aujourd'hui » sur un tableau de bord n'a
/// aucun moyen de savoir que ce montant n'existe pas. Une maquette assumée est
/// moins nuisible qu'un mensonge plausible.
///
/// D'où deux outils dans ce fichier, l'un pour la couche de données, l'autre
/// pour l'écran :
///
///   • [NotMigrated.call]   — lève depuis un appel d'API qui n'a pas d'amont ;
///   • [NotMigratedScreen]  — remplace l'écran entier quand c'est TOUT l'écran
///                            qui n'a pas d'amont.
///
/// CE N'EST PAS UN `TODO`. Un TODO se contourne du regard ; ceci s'affiche.
///
/// Et cela reste greppable : `grep -rn NotMigrated` donne la liste exacte de ce
/// qui reste à rebrancher, sans refaire l'enquête.
///
/// UN WIDGET DANS `core/network/`, ET C'EST DÉLIBÉRÉ.
///
/// L'app Client porte le même fichier au même endroit
/// (`lib/src/core/network/not_migrated.dart`). Le sujet n'est pas « du réseau »
/// ni « de l'interface » : c'est l'inventaire, en un seul endroit, de ce que la
/// plateforme ne sait pas encore faire. Le scinder en deux fichiers ferait
/// diverger deux listes qui doivent rester une seule.
/// ═══════════════════════════════════════════════════════════════════════════
class NotMigrated {
  const NotMigrated._();

  /// Lève systématiquement. [domain] nomme le module manquant, [screen] l'écran
  /// qui en dépend.
  static Never call(String domain, {required String screen}) {
    throw ApiException(
      'Cette fonctionnalité n\'est pas encore disponible : le module « $domain » '
      'n\'a pas encore été repris sur la nouvelle plateforme.',
      code: 'not_migrated',
    );
  }

  /// Ce qui manque, et POURQUOI — la liste vit ICI plutôt que dispersée en
  /// commentaires, sans quoi deux inventaires finiraient par se contredire.
  ///
  /// Les raisons ne sont pas les mêmes, et le jour du rebranchement elles ne
  /// coûteront pas le même travail :
  ///
  ///   • `shipping`   — module jamais extrait du monolithe. Aucune route
  ///                    d'expédition dans `ReverseProxy:Routes` de la
  ///                    passerelle. Écrans : `/shipments`.
  ///   • `returns`    — module jamais extrait. Écrans : `/returns`.
  ///   • `disputes`   — module jamais extrait. Écrans : `/dispute/:id`.
  ///   • `offers`     — RETIRÉ EN PHASE 3. Les mises en vente ne sont pas parties
  ///                    dans un `products-service` : elles ont été GREFFÉES dans
  ///                    catalog-service, sous `/api/catalog/seller/offers`. Le
  ///                    diagnostic « aucune route `/api/offers` » restait vrai à
  ///                    la lettre et faux sur le fond — il n'y en aura jamais.
  ///
  ///                    `/locations` NE FIGURE PLUS ICI, ET C'ÉTAIT UNE
  ///                    ERREUR DE DIAGNOSTIC. Les lieux d'expédition ne sont pas
  ///                    des objets d'Offers : ce sont des `FulfillmentLocation`
  ///                    d'inventory-service, et la lecture y allait déjà. Seul
  ///                    le droit d'ÉCRIRE manquait — ouvert par VEN11. L'écran
  ///                    est rebranché.
  ///
  ///                    Ce qui dépend réellement d'Offers, c'est le LIEN entre
  ///                    un lieu et une offre (`shipFromLocationId`) : on peut
  ///                    déclarer ses lieux et y poser du stock, pas encore dire
  ///                    « cette offre part de celui-là ».
  ///   • `analytics`  — AUCUNE agrégation côté serveur : la série temporelle
  ///                    des ventes était calculée par le BFF vendeur du
  ///                    monolithe. Ni service HBA, ni route BFF ne la rendent.
  ///                    Écrans : `/analytics`.
  ///   • `merchantConsolidated`
  ///                  — `bffMerchant/activities` rend la LISTE des activités,
  ///                    pas leurs totaux. Personne n'additionne boutiques et
  ///                    restaurants. Écrans : la vue consolidée de `/home` et
  ///                    celle de `/orders`.
  ///   • `appUpdate`  — RETIRÉ. Le constat « aucun endpoint de version côté HBA »
  ///                    était juste, et incomplet : `AppVersionController` existait
  ///                    déjà sur la PASSERELLE, entièrement écrit. Il manquait la
  ///                    section `AppVersions` d'`appsettings.json` et l'appel côté
  ///                    application. `GET /api/app/seller/version`, anonyme.

  ///
  ///   • `foodMenuEdit` — douze routes ont été ouvertes dans `FoodEndpoints`.
  ///     Renommer, masquer, réordonner et supprimer une carte, une section ou un
  ///     plat ; et surtout `PUT .../items/{id}/availability`, qui porte les trois
  ///     états `available` / `sold_out_today` / `unavailable`.
  ///
  ///     RESTENT FERMÉES, ET C'EST UN CHOIX, PAS UN OUBLI : déplacer une
  ///     section ou un plat vers une autre carte (`MoveCategory`,
  ///     `MoveMenuItem`), les créneaux de service (`SetMenuWindow`), la photo
  ///     d'un plat (`SetMenuItemImage`) et le retrait d'options. Les commandes
  ///     existent et sont testées ; aucun écran ne les demande, et une route sans
  ///     appelant est une surface d'attaque entretenue pour rien. Le jour où un
  ///     écran en a besoin, c'est une ligne.
  ///
  ///   • `foodServiceToggle` — `POST .../pause` et `.../resume` sont ouvertes.
  ///     La pause est BORNÉE : `{ "minutes": n }` est obligatoire, parce
  ///     qu'une fermeture sans échéance qu'on oublie de lever retire
  ///     l'établissement de la vitrine pour la soirée sans que personne le voie.
  ///
  /// ── Retiré de cette liste par VEN3 ────────────────────────────────────────
  ///
  ///   • `sellerOrders` — la route `seller-orders` a été ajoutée à
  ///     `ReverseProxy:Routes` : `GET /api/sellers/{sellerId}/orders` est
  ///     désormais joignable, et l'écran Commandes est branché dessus. Il reste
  ///     que le service n'offre ni pagination, ni filtre, ni période — mais
  ///     c'est une limite, pas une absence.
  static const List<String> pendingModules = [
    'shipping',
    'returns',
    'disputes',
    'analytics',
    'merchantConsolidated',
    'reviewReport',
  ];
}

/// L'écran qu'on affiche à la place d'un écran sans amont.
///
/// ═══════════════════════════════════════════════════════════════════════════
/// IL DIT CE QUI MANQUE À UN VENDEUR, PAS À UN DÉVELOPPEUR.
///
/// « Le suivi de vos expéditions arrive bientôt », et non « le module Shipping
/// n'est pas extrait du monolithe ». Un vendeur de Cotonou n'a que faire de nos
/// modules ; il a besoin de savoir s'il doit attendre ou faire autrement. La
/// raison technique, elle, est dans le commentaire de chaque écran appelant —
/// c'est là qu'on la cherchera le jour du rebranchement.
///
/// TEXTES EN DUR, PAS DE CLÉS `l10n`.
///
/// Ajouter onze clés aux fichiers ARB pour un état transitoire les y laisserait
/// longtemps après le rebranchement. Les écrans de la maquette (tableaux de
/// bord, commandes) écrivent déjà leurs libellés en français dans le code : on
/// suit la même règle, et on la retire avec l'état qu'elle décrit.
///
/// AUCUN BOUTON « RÉESSAYER ». Il n'y a rien à réessayer : ce n'est pas une
/// panne, c'est une absence. Le proposer ferait croire à un incident passager,
/// et le vendeur y reviendrait dix fois.
/// ═══════════════════════════════════════════════════════════════════════════
class NotMigratedScreen extends StatelessWidget {
  const NotMigratedScreen({
    super.key,
    required this.title,
    required this.message,
    this.detail,
    this.inShell = false,
  });

  /// Ce que le vendeur croyait ouvrir : « Expéditions », « Retours »…
  final String title;

  /// UNE phrase, écrite pour lui. Dit ce qui arrive, et si possible quoi faire
  /// en attendant.
  final String message;

  /// Complément facultatif : la référence de la commande qu'il venait de
  /// toucher, ou le chemin qui reste ouvert. Sans lui, un écran atteint depuis
  /// un bouton précis paraît avoir avalé le geste.
  final String? detail;

  /// L'écran occupe-t-il un ONGLET de la coquille ?
  ///
  /// CE N'EST PAS UN DÉTAIL DE STYLE.
  ///
  /// Dans la coquille, la barre du bas est déjà là et il n'y a rien à dépiler :
  /// une `AppBar` avec sa flèche de retour proposerait une sortie qui n'existe
  /// pas et lèverait au premier appui. Hors coquille, au contraire, l'absence de
  /// retour enfermerait le vendeur.
  final bool inShell;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Scaffold(
      backgroundColor: colors.bg,
      appBar: inShell ? null : AppBar(title: Text(title)),
      body: SafeArea(
        bottom: false,
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.fromLTRB(28, 24, 28, 32),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Container(
                  width: 72,
                  height: 72,
                  alignment: Alignment.center,
                  decoration: const BoxDecoration(
                    color: AppTheme.brandGreenSoft,
                    shape: BoxShape.circle,
                  ),
                  child: const Icon(
                    Icons.hourglass_empty_rounded,
                    size: 32,
                    color: AppTheme.brandGreen,
                  ),
                ),
                const SizedBox(height: 20),

                // En onglet, le titre n'est nulle part ailleurs : c'est ici
                // qu'il doit être écrit. Hors onglet, l'`AppBar` le porte déjà,
                // et le répéter donnerait deux fois le même mot à la suite.
                if (inShell) ...[
                  Text(
                    title,
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      fontSize: 20,
                      fontWeight: FontWeight.w800,
                      color: colors.ink,
                    ),
                  ),
                  const SizedBox(height: 8),
                ],

                Text(
                  'Bientôt disponible',
                  textAlign: TextAlign.center,
                  style: TextStyle(
                    fontSize: inShell ? 14 : 18,
                    fontWeight: FontWeight.w800,
                    color: inShell ? colors.subtle : colors.ink,
                  ),
                ),
                const SizedBox(height: 10),

                Text(
                  message,
                  textAlign: TextAlign.center,
                  style: TextStyle(
                    fontSize: 14.5,
                    height: 1.5,
                    color: colors.subtle,
                  ),
                ),

                if (detail != null) ...[
                  const SizedBox(height: 14),
                  Container(
                    padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                    decoration: BoxDecoration(
                      color: colors.surface,
                      borderRadius: BorderRadius.circular(AppTheme.radiusField),
                      border: Border.all(color: colors.line),
                    ),
                    child: Text(
                      detail!,
                      textAlign: TextAlign.center,
                      style: TextStyle(
                        fontSize: 13,
                        height: 1.4,
                        fontWeight: FontWeight.w600,
                        color: colors.ink,
                      ),
                    ),
                  ),
                ],

                if (!inShell) ...[
                  const SizedBox(height: 26),
                  SizedBox(
                    width: double.infinity,
                    child: OutlinedButton(
                      // Deux sorties possibles, et le choix se fait à
                      // l'exécution : `pop` s'il y a une pile (on vient d'un
                      // écran), l'accueil sinon (lien profond, notification).
                      // Appeler `pop` sans rien à dépiler lèverait.
                      onPressed: () {
                        if (Navigator.of(context).canPop()) {
                          context.pop();
                        } else {
                          context.go('/home');
                        }
                      },
                      style: OutlinedButton.styleFrom(
                        minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
                        side: BorderSide(color: colors.line),
                        foregroundColor: colors.ink,
                        textStyle: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700),
                      ),
                      child: const Text('Retour'),
                    ),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}
