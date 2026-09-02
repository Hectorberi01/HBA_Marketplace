# Les secrets GitHub — ce qu'il faut créer, et où

> Dérivé des workflows et de `docker-compose.prod.yml`. Régénérer la liste après
> tout changement de workflow : `git grep -oh "secrets\.[A-Z_]*" .github/workflows`.

---

## AVANT DE COLLER QUOI QUE CE SOIT : ROTATION

**Les vingt-quatre secrets de production ont circulé en clair dans une
conversation d'assistance**, le 1er septembre 2026, ainsi qu'un jeton GHCR tapé
sur une ligne de commande. Ils doivent être considérés comme connus.

Configurer les secrets GitHub est le moment de poser des valeurs **neuves**, pas
de recopier celles-là. Une valeur compromise rangée dans un coffre reste
compromise : le coffre protège ce qui n'est pas encore sorti.

Deux exceptions, et elles vont dans des sens opposés :

- `SECURITY__SECRETPROTECTION__KEY` **ne se régénère pas** : ce qu'elle a
  chiffré ne se déchiffre pas avec la suivante. La changer demande de
  rechiffrer les données existantes, ou de les perdre.
- `AUTHENTICATION__SIGNINGKEY` et `JWT__SIGNINGKEY` doivent porter la **même**
  valeur, et la changer invalide tous les jetons en cours — les utilisateurs
  connectés sont déconnectés. C'est acceptable, mais ce n'est pas silencieux.

---

## 1. Environnement `production`

Settings → Environments → `production`. **Poser une règle de révision
obligatoire** : sans elle, quiconque peut lancer le workflow met en ligne.

### Secrets

| Nom | Contenu | Comment l'obtenir |
|---|---|---|
| `VPS_SSH_KEY` | clé privée OpenSSH, **sans phrase de passe** | `ssh-keygen -t ed25519 -f deploiement -N ""` puis pousser la publique dans `~ubuntu/.ssh/authorized_keys` du VPS |
| `VPS_KNOWN_HOSTS` | l'empreinte du VPS | `ssh-keyscan -p 8022 79.137.35.129` |
| `PROD_ENV_FILE` | le fichier d'environnement **entier**, 46 variables | voir §3 |
| `GHCR_TOKEN` | jeton GitHub, portée `read:packages` seule | Settings → Developer settings → Personal access tokens |

**`VPS_KNOWN_HOSTS` n'est pas un confort.** Le workflow envoie au VPS quatorze
mots de passe de base, les clés de signature et la clé FedaPay. Sans empreinte,
`StrictHostKeyChecking=no` les enverrait à qui répond à cette adresse. Le job
refuse de démarrer sans elle.

### Variables

| Nom | Valeur |
|---|---|
| `VPS_HOST` | `79.137.35.129` |
| `VPS_USER` | `ubuntu` |
| `VPS_PORT` | `8022` |
| `HBA_DOMAINE` | `api.hba-express.com` |

---

## 2. Ce qui n'est PAS à créer pour le déploiement Compose

| Nom | Où | Pourquoi |
|---|---|---|
| `KUBECONFIG_B64` | environnements `dev`, `staging`, `prod` | sert à `deploy-branches.yml`, le déploiement **Kubernetes**, en pause |
| `GITHUB_TOKEN` | — | fourni par GitHub, jamais créé à la main |

**`KUBECONFIG_B64` mérite une vérification quand k3s reprendra** : les trois
environnements doivent en porter **trois valeurs différentes**. S'ils partagent
la même, les trois branches déploient sur le même cluster dans trois espaces de
noms, et rien ne le dit — la seule garde vérifie que le secret n'est pas vide.

---

## 3. `PROD_ENV_FILE` — les 46 variables

Le contenu est le fichier tel quel, une ligne `CLÉ=valeur` par variable. Le
workflow le contrôle avant tout envoi : variables absentes, variables vides,
`$` non échappés, et l'égalité du couple de clés de signature.

**Le `$` dans une valeur doit être doublé.** Compose interpole aussi le fichier
d'environnement : `mot$depasse` y est lu comme une référence de variable, et le
service part avec un mot de passe **tronqué** — l'erreur parlera
d'authentification, pas de `$`. Écrire `mot$$depasse`.

`HBA_TAG` n'est **pas** à mettre : le workflow l'ajoute depuis son entrée.

### Mots de passe de base (14)

```
HBA_CATALOG_PASSWORD
HBA_COMMERCE_PASSWORD
HBA_DELIVERY_PASSWORD
HBA_ENGAGEMENT_PASSWORD
HBA_FINANCIAL_PASSWORD
HBA_FOOD_PASSWORD
HBA_IDENTITY_PASSWORD
HBA_INVENTORY_PASSWORD
HBA_MEDIA_PASSWORD
HBA_MERCHANT_PASSWORD
HBA_ORDER_PASSWORD
HBA_PROMOTION_PASSWORD
HBA_USER_PASSWORD
MINIO_ROOT_PASSWORD
```

### Identités gRPC internes (19)

Engendrées par `scripts/generer-identites-internes.sh`. Une absente et le
service concerné ne peut plus se faire reconnaître des autres.

```
INTERNAL_KEY_HBA_CATALOG_API
INTERNAL_KEY_HBA_COMMERCE_API
INTERNAL_KEY_HBA_DELIVERY_CORE_API
INTERNAL_KEY_HBA_DELIVERY_DRIVER_API
INTERNAL_KEY_HBA_DELIVERY_PRICING_API
INTERNAL_KEY_HBA_DELIVERY_ROUTE_API
INTERNAL_KEY_HBA_ENGAGEMENT_API
INTERNAL_KEY_HBA_FINANCIAL_API
INTERNAL_KEY_HBA_FOOD_CART_API
INTERNAL_KEY_HBA_FOOD_ORDER_API
INTERNAL_KEY_HBA_FOOD_RESTAURANT_API
INTERNAL_KEY_HBA_GATEWAY_API
INTERNAL_KEY_HBA_IDENTITY_API
INTERNAL_KEY_HBA_INVENTORY_API
INTERNAL_KEY_HBA_MEDIA_API
INTERNAL_KEY_HBA_MERCHANTS_API
INTERNAL_KEY_HBA_ORDER_API
INTERNAL_KEY_HBA_PROMOTIONS_API
INTERNAL_KEY_HBA_USERS_API
```

### Le reste (13)

```
ADMIN__PASSWORD
AUTHENTICATION__SIGNINGKEY
HBA_ACME_EMAIL
HBA_DOMAINE
INTERNAL_PUBLIC_KEYS
INTERNAL__APIKEY
JWT__SIGNINGKEY
MEDIA__STORAGE__ACCESSKEYID
MEDIA__STORAGE__SECRETACCESSKEY
MINIO_ROOT_USER
PAYMENTS__FEDAPAY__APIKEY
PAYMENTS__FEDAPAY__WEBHOOKSECRET
SECURITY__SECRETPROTECTION__KEY
```

---

## 4. Ce que ce document ne couvre pas

- **Les valeurs elles-mêmes.** Aucune n'est écrite ici, et aucune ne doit l'être.
- **Que les valeurs soient les BONNES.** Un mot de passe présent mais faux passe
  tous les contrôles et échoue à la connexion.
- **Les secrets du dépôt Terraform** (identifiants OVH, état S3) : le
  déploiement Compose ne les demande pas, et `infra/terraform/` n'est branché à
  aucune CI — voir la décision en attente.
