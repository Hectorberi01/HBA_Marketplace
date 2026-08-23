# Refonte visuelle — carte des écrans

Ce dossier reçoit les maquettes et sert de plan de travail à la refonte
**écran par écran** des deux applications Flutter.

---

## 1. Les maquettes attendues ici

| Fichier | Application cible |
|---|---|
| `HBA App.dc.html` | `clients/mobile/Client` — application cliente |
| `HBA Partner.dc.html` | `clients/mobile/Seller-portal` — application vendeur |

> **Les fichiers ne sont pas encore là.** Les deux URL claude.ai redirigent
> vers la page de connexion, et aucun navigateur Claude in Chrome n'est connecté
> au compte. Tant que le style n'est pas lisible, aucune couleur ni police ne
> doit être écrite dans le code : une valeur inventée « en attendant » ne se
> distingue plus d'une valeur voulue, et se propage à 121 écrans.

---

## 2. Ce que « écran par écran » implique

**121 écrans au total.** Ce n'est pas un chantier d'un bloc.

| Application | Écrans | Fichiers |
|---|---|---|
| `mobile/Client` | 38 | 87 `.dart` |
| `mobile/Seller-portal` | 34 | + suite de tests |
| *(hors périmètre pour l'instant)* `seller-portal` Next.js | 19 routes | — |
| *(hors périmètre)* `mobile/Driver` | à inventorier | — |

L'ordre proposé ci-dessous n'est pas alphabétique : il part de ce qui **fixe le
vocabulaire visuel** — un bouton, une carte, un champ — vers ce qui n'en est que
l'assemblage. Refaire `product_detail` avant d'avoir arrêté l'aspect d'un bouton
oblige à y revenir.

---

## 3. Application cliente — `mobile/Client`

### Vague 1 — le vocabulaire (à faire en premier)

Ces écrans contiennent presque tous les composants de base. Une fois validés, le
reste devient de l'assemblage.

| Écran | Domaine | Ce qu'il fixe |
|---|---|---|
| `login`, `register` | auth | champs, boutons primaires, messages d'erreur |
| `home` | home | navigation principale, cartes, en-têtes de section |
| `product_detail` | catalog | images, prix, boutons d'action, onglets |
| `cart` | cart | lignes éditables, totaux, état vide |

### Vague 2 — les parcours qui rapportent

| Écran | Domaine |
|---|---|
| `express_home`, `food_home` | home — les deux univers doivent rester **visuellement distincts** |
| `category`, `search` | catalog |
| `checkout`, `payment_webview` | checkout |
| `order_confirmation`, `order_tracking`, `orders`, `order_detail` | orders |
| `restaurant_detail` | food |
| `shop` | shop |

### Vague 3 — le compte et le reste

| Écran | Domaine |
|---|---|
| `account`, `edit_profile`, `addresses`, `wishlist` | account |
| `notifications`, `notification_preferences`, `delete_account` | account |
| `conversations`, `chat` | messaging |
| `disputes`, `dispute_detail`, `returns`, `loyalty` | engagement |
| `product_reviews` | catalog |
| `faq` | support |
| `terms`, `privacy`, `consent` | legal |
| `splash`, `verify_email`, `update_required` | auth / app_update |

---

## 4. Application vendeur — `mobile/Seller-portal`

### Vague 1 — le vocabulaire

`login`, `register`, `home` (tableau de bord), `products`, `orders`.

Le tableau de bord fixe à lui seul les cartes de chiffres, les graphiques et les
badges d'état — c'est le plus structurant des cinq.

### Vague 2 — l'exploitation quotidienne

`order_detail`, `product_detail`, `product_preview`, `offers`, `inventory`,
`shipments`, `shipping_locations`, `returns`, `disputes`.

### Vague 3 — argent, boutique, compte

`wallet`, `finance`, `analytics`, `shop`, `reviews`, `account`, `profile`,
`settings`, `notifications`, `notification_preferences`.

### Vague 4 — le reste

`splash`, `forgot_password`, `reset_password`, `verify_code`, `terms`,
`privacy`, `consent`, `update_required`.

---

## 5. Points à trancher avant la première ligne de code

**Les deux applications partagent-elles un jeu de tokens ?**
Client et vendeur sont deux produits distincts — un vendeur n'a pas besoin de
l'ambiance d'une vitrine. Mais deux jeux totalement séparés dérivent : une
correction de contraste faite d'un côté ne l'est pas de l'autre. Un socle commun
(espacements, rayons, échelle typographique) avec des palettes distinctes est le
compromis habituel. À confirmer une fois les deux maquettes lisibles.

**Que devient `clients/merchant-portal` ?**
Le dossier est vide et son nom porte une coquille — « marchant » pour
« merchant ». Il fait doublon avec `seller-portal` (Next.js). À supprimer ou à
renommer, mais pas à laisser : un dossier vide au nom presque juste finit par
recevoir du code que personne ne cherchera au bon endroit.

**Les tests du vendeur suivent-ils ?**
`mobile/Seller-portal/test/` contient des tests d'écran (`orders_flow_test`,
`dashboard_data_test`…). Une refonte écran par écran les cassera au fur et à
mesure. Ils doivent être repris **dans la même passe** que l'écran concerné —
sinon la suite reste rouge pendant des semaines et cesse d'être lue.

---

## 6. Comment connecter le navigateur

L'extension **Claude in Chrome** utilise la session claude.ai déjà ouverte et
rend les deux maquettes lisibles directement.

À défaut, la voie la plus sûre reste de télécharger les deux fichiers
`.dc.html` depuis claude.ai et de les déposer dans ce dossier : le fichier exact
vaut mieux qu'une capture ou une description.
