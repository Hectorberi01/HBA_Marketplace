# Prompts de design — Application mobile client (Marketplace)

Ce fichier contient un **prompt de design par écran** de l'application Flutter `client_mp_mobile`.
Chaque prompt est autonome et peut être collé dans un outil de design génératif (Figma AI, v0,
Galileo, Uizard, Midjourney UI…) ou remis à un designer. Tous les écrans partagent le **système
de design** décrit ci-dessous : rappelle-le en tête de chaque génération pour garder la cohérence.

---

## 0. Système de design (à injecter dans chaque prompt)

> Application mobile de marketplace e‑commerce (Afrique de l'Ouest, paiement Mobile Money).
> Style **Material 3**, mode clair, épuré et moderne, orienté confiance et achat rapide.
>
> **Couleurs**
> - Primaire (vert marketplace) : `#1F8A4C` — boutons d'action, sélection, accents positifs.
> - Secondaire / promo (orange) : `#F2A03D` — badges promo, prix barrés, étiquettes "‑X%".
> - Fond d'écran : `#F7F8F8` (gris très clair).
> - Surfaces / cartes : blanc `#FFFFFF`.
> - Texte principal : gris anthracite ; texte secondaire : gris moyen.
>
> **Composants**
> - Cartes : coins arrondis 16 px, bordure fine 1 px gris clair, **pas d'ombre** (flat).
> - Champs de saisie : fond blanc, coins 12 px, bordure fine.
> - Boutons primaires : pleins (FilledButton), hauteur 50 px, coins 12 px, texte semi‑gras.
> - AppBar : fond surface, sans élévation, titre aligné à gauche, gras (18 px / w700).
> - Barre de navigation basse : 5 onglets, fond blanc, indicateur vert clair.
> - Puces (chips) : fond gris clair, coins 8 px.
> - Typo : sans‑serif system (Roboto / SF), titres w700‑w800, corps w400‑w600.
>
> **Devise** : Franc CFA, format `12 500 FCFA` (espace milliers, symbole après le montant).
>
> **Navigation basse (5 onglets)** : Accueil · Recherche · Panier · Commandes · Compte.
> Les écrans secondaires (détail produit, checkout, chat…) s'ouvrent en plein écran par‑dessus.
>
> **États à toujours prévoir** pour chaque écran connecté : *chargement* (skeletons / spinner
> centré), *vide* (illustration + message + éventuel bouton), *erreur* (message + bouton "Réessayer").

---

## 1. Splash / Démarrage — `/splash`

Écran d'amorçage affiché au lancement pendant la restauration de la session (lecture du token
sécurisé). Plein écran sur fond vert primaire `#1F8A4C` : logo de la marketplace centré (monogramme
ou nom), éventuel léger indicateur de chargement sous le logo. Aucune action utilisateur. Transition
douce vers `/home` si l'utilisateur est connecté, sinon vers `/login`. Durée minimale ~1 s pour
éviter un flash. Soigner le rendu sur encoches et bords arrondis (SafeArea).

## 2. Connexion — `/login`

Objectif : authentifier un client existant. Mise en page centrée verticalement, généreuse en
espaces blancs. De haut en bas : logo + titre "Se connecter", sous‑titre court ("Accédez à vos
commandes et favoris"). Formulaire : champ **Email** (clavier email), champ **Mot de passe** (avec
icône œil pour afficher/masquer), lien discret "Mot de passe oublié ?" aligné à droite. Bouton
primaire pleine largeur "Se connecter" (hauteur 50). Sous le bouton : séparateur "ou", puis lien
"Pas de compte ? **Créer un compte**" vers `/register`. Gérer l'état d'erreur (bandeau rouge clair
"Identifiants invalides") et l'état de chargement (bouton avec spinner, champs désactivés). Design
rassurant, professionnel.

## 3. Inscription — `/register`

Objectif : créer un compte client. Même grammaire visuelle que la connexion. Titre "Créer un
compte". Champs : **Nom complet**, **Email**, **Téléphone** (préfixe indicatif pays, important pour
Mobile Money), **Mot de passe** (avec indicateur de robustesse simple), case à cocher
"J'accepte les conditions d'utilisation" avec liens. Bouton primaire "Créer mon compte". Bas
d'écran : "Déjà inscrit ? **Se connecter**". Prévoir messages de validation en ligne sous chaque
champ (email invalide, mot de passe trop court) et l'état de chargement.

## 4. Accueil — `/home` (onglet 1)

Écran vitrine, premier contact. Structure verticale scrollable :
1. **Barre de recherche** factice en haut (champ blanc arrondi 12, icône loupe, placeholder
   "Rechercher un produit…") qui ouvre l'écran Recherche au tap.
2. **Bandeau de catégories** : liste horizontale de puces (chips) cliquables.
3. **Sections de produits** titrées (ex. "Mises en avant", "Recommandés pour vous") : chaque section
   a un titre gras (18 px) et une **grille de vignettes produit** (2 colonnes, format vertical,
   ratio image ~1:1 en haut).
   - **Vignette produit** : image carrée en haut (coins arrondis), nom sur 2 lignes max, prix en
     vert gras "à partir de X FCFA", petite note en étoiles si disponible. Carte flat bordée.
4. Pull‑to‑refresh (tirer pour rafraîchir). Pas de carrousel — préférer des grilles.
État vide : illustration "boutique" + "Aucun produit pour le moment." Le design doit donner envie de
parcourir, mettre la photo produit en valeur.

## 5. Recherche — `/search` (onglet 2)

Objectif : trouver un produit. En haut, AppBar avec **champ de recherche actif** (autofocus, icône
effacer). Deux modes :
- **Suggestions** (pendant la frappe) : liste verticale de lignes (icône loupe + libellé suggéré +
  flèche), tap → résultat ou produit.
- **Résultats** (après validation) : **grille 2 colonnes** de vignettes produit identiques à
  l'accueil. En option, une barre de filtres/tri en haut (puces : Pertinence, Prix croissant,
  Mieux notés, Catégorie).
États : initial ("Saisissez un terme pour rechercher" + icône), vide ("Aucun résultat" + icône
barrée), chargement (skeletons de grille). Interaction fluide, sans rechargement brutal.

## 6. Détail produit — `/product/:id`

Écran clé de conversion, plein écran scrollable.
- **Galerie d'images** en haut (carrousel d'images du produit, pagination par points), bouton retour
  et bouton **favori (cœur)** en superposition.
- **Bloc infos** : nom du produit (titre gras), note moyenne + nombre d'avis, **prix** en grand
  (vert), prix barré + badge promo orange si applicable.
- **Sélecteur de variantes** si présent (taille / couleur en puces sélectionnables).
- **Vendeur** : ligne cliquable "Vendu par **[Boutique]**" → ouvre la vitrine `/shop/:id`, avec
  mini note vendeur.
- **Description** repliable ("Voir plus").
- **Avis** : résumé (moyenne + barres de répartition) et 2‑3 avis récents, lien "Voir tous les avis".
- **Produits similaires** : rangée horizontale de vignettes.
- **Barre d'action fixe en bas** (sticky) : prix à gauche, boutons "Ajouter au panier" (plein vert)
  et "Acheter". Les boutons d'une même rangée ne doivent pas s'étirer en pleine largeur chacun.
États : chargement (skeleton image + lignes), erreur produit introuvable.

## 7. Panier — `/cart` (onglet 3)

Objectif : réviser avant paiement. Liste verticale de **lignes panier** : miniature produit, nom,
variante, prix unitaire, **sélecteur de quantité** (− / valeur / +), bouton supprimer (swipe ou
icône corbeille). En bas, **récapitulatif** dans une carte : sous‑total, livraison estimée,
remises/coupon, **Total** en gras. Champ "Code promo" + bouton appliquer. **Barre fixe en bas** :
total + bouton primaire "Passer la commande" → `/checkout`. État vide : illustration panier + "Votre
panier est vide" + bouton "Découvrir des produits". Mettre à jour les totaux en direct au changement
de quantité.

## 8. Checkout / Paiement — `/checkout`

Tunnel d'achat, écran structuré en sections (style accordéon ou étapes verticales) :
1. **Adresse de livraison** : adresse sélectionnée + lien "Modifier / Ajouter".
2. **Mode de livraison** : options radio (Standard, Express) avec délai et prix.
3. **Paiement** : choix radio **Mobile Money (MTN, Moov)** avec champ numéro de téléphone payeur,
   carte bancaire, etc. Mettre en avant Mobile Money.
4. **Récapitulatif commande** : lignes produits compactes + totaux (sous‑total, livraison, taxes,
   total à payer en gras).
Barre fixe en bas : "Payer X FCFA". Après validation, **écran/overlay d'attente de confirmation**
(animation + "Confirmation du paiement en cours…", polling) puis succès (coche verte +
"Commande confirmée" + bouton "Voir ma commande") ou échec (croix + "Paiement échoué" + "Réessayer").
Design clair et rassurant, réduire la charge cognitive.

## 9. Mes commandes — `/orders` (onglet 4)

Liste de l'historique d'achats. Onglets/filtres en haut par statut (Toutes, En cours, Livrées,
Annulées). Chaque **carte commande** : numéro/réf, date, miniatures empilées des produits, statut en
**badge coloré** (ex. orange "Expédiée", vert "Livrée", gris "En attente"), montant total, flèche →
`/order/:id`. État vide : "Aucune commande" + bouton "Commencer mes achats". Tri du plus récent au
plus ancien.

## 10. Détail commande — `/order/:id`

Suivi d'une commande. De haut en bas : en‑tête (réf + date + badge statut), **timeline de suivi**
verticale (Confirmée → Préparée → Expédiée → Livrée) avec étapes cochées/actives. **Adresse de
livraison**, **mode de paiement** utilisé. **Liste des articles** (miniature, nom, qté, prix).
**Récapitulatif** des montants. Actions contextuelles selon statut : "Suivre le colis", "Contacter
le vendeur" (→ messagerie), "Demander un retour", "Ouvrir un litige", "Laisser un avis" (si livrée).
Design type "reçu" lisible.

## 11. Compte — `/account` (onglet 5)

Tableau de bord du profil. En haut, **carte profil** : avatar (initiales si pas de photo), nom,
email, bouton "Modifier" → `/account/edit`. En dessous, **liste de raccourcis** (lignes avec icône +
libellé + chevron) regroupés :
- Mes favoris → `/wishlist`
- Mes messages → `/conversations`
- Notifications → `/notifications`
- Mes commandes → `/orders`
- Adresses de livraison
- Moyens de paiement
- Aide & support, Conditions, Confidentialité
En bas, bouton **Déconnexion** (style discret/rouge). Design propre, type "réglages iOS/Material".

## 12. Modifier le profil — `/account/edit`

Formulaire d'édition des informations personnelles. Champs pré‑remplis : photo (bouton changer),
**Nom**, **Email**, **Téléphone**. Section optionnelle "Changer le mot de passe" (ancien / nouveau /
confirmation). Bouton primaire "Enregistrer" + retour. Validation en ligne, états de chargement et de
succès (snackbar "Profil mis à jour").

## 13. Favoris / Wishlist — `/wishlist`

Grille 2 colonnes de **vignettes produit favorites** (mêmes cartes que l'accueil) avec **cœur plein**
en superposition pour retirer. Tap → détail produit. Possibilité d'ajouter au panier directement
depuis la carte (petit bouton). État vide : icône cœur + "Aucun favori pour l'instant" + bouton
"Parcourir le catalogue". Chargement en skeletons.

## 14. Notifications — `/notifications`

Liste verticale chronologique de notifications. Chaque ligne : icône typée (commande, promo,
message, système), titre, court extrait, horodatage relatif ("il y a 2 h"). **Point/indicateur de
non‑lu** (pastille colorée) ; le tap marque comme lue (le point disparaît) et navigue vers la cible.
Distinguer visuellement lues / non‑lues (fond légèrement teinté pour non‑lues). En‑tête éventuel
"Tout marquer comme lu". État vide : cloche + "Aucune notification".

## 15. Vitrine vendeur — `/shop/:id`

Page boutique d'un vendeur. **En‑tête boutique** : bannière/couleur, logo/avatar du vendeur, nom de
la boutique, note moyenne + nombre d'avis, badges (vérifié, délai d'expédition), bouton "Contacter".
En dessous, **grille de produits du vendeur** (2 colonnes). Onglets optionnels : Produits · À propos ·
Avis. Donner une impression de marque/confiance. État vide produits : "Cette boutique n'a pas encore
de produits".

## 16. Conversations / Messagerie — `/conversations`

Liste des fils de discussion (avec vendeurs / support). Chaque ligne : avatar interlocuteur, nom,
**dernier message** tronqué, horodatage, **badge compteur de non‑lus** à droite, gras si non‑lu.
Tri par message le plus récent. Tap → `/chat/:id`. État vide : icône bulle + "Aucune conversation".
Style proche d'une messagerie (WhatsApp‑like épuré, aux couleurs de la marque).

## 17. Conversation / Chat — `/chat/:id`

Fil de discussion 1‑à‑1. AppBar : nom de l'interlocuteur (+ statut/boutique). **Bulles de messages**
alignées (envoyés à droite en vert clair, reçus à gauche en gris clair), horodatage discret,
regroupement par jour (séparateurs "Aujourd'hui", date). **Barre de saisie fixe en bas** : champ
texte extensible, bouton joindre (optionnel), bouton envoyer (rond vert). Auto‑scroll vers le dernier
message, indicateur d'envoi/lecture. État vide : "Démarrez la conversation". Optionnel : carte
produit/commande contextuelle épinglée en haut.

---

## Annexe — Récapitulatif des écrans

| # | Écran | Route | Onglet nav |
|---|-------|-------|------------|
| 1 | Splash | `/splash` | — |
| 2 | Connexion | `/login` | — |
| 3 | Inscription | `/register` | — |
| 4 | Accueil | `/home` | 1 · Accueil |
| 5 | Recherche | `/search` | 2 · Recherche |
| 6 | Détail produit | `/product/:id` | — |
| 7 | Panier | `/cart` | 3 · Panier |
| 8 | Checkout / Paiement | `/checkout` | — |
| 9 | Mes commandes | `/orders` | 4 · Commandes |
| 10 | Détail commande | `/order/:id` | — |
| 11 | Compte | `/account` | 5 · Compte |
| 12 | Modifier le profil | `/account/edit` | — |
| 13 | Favoris | `/wishlist` | — |
| 14 | Notifications | `/notifications` | — |
| 15 | Vitrine vendeur | `/shop/:id` | — |
| 16 | Conversations | `/conversations` | — |
| 17 | Chat | `/chat/:id` | — |

> Conseil d'usage : pour générer une maquette cohérente, préfixe chaque prompt d'écran par la
> section **0. Système de design**, puis ajoute le prompt de l'écran voulu. Génère d'abord
> l'Accueil et le Détail produit (ils fixent le style des vignettes et de la barre d'action), puis
> décline les autres écrans.
