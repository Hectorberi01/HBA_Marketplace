# Les workflows du dépôt

Deux fichiers, et c'est tout. Le chemin Kubernetes — `cd.yml`,
`deploy-branches.yml`, les calques de `k8s/` — a été retiré le 3 septembre 2026 ;
la production tourne sur **Docker Compose + Traefik**.

| fichier | déclencheur | ce qu'il fait |
|---|---|---|
| `ci.yml` | `push` sur `main`, `dev`, `staging`, `develop` et toute PR | barrière, compilation, tests, images |
| `deploy-compose.yml` | **manuel** (`workflow_dispatch`) | déploie un tag donné sur le VPS de production |

---

## `ci.yml` — quatre travaux

### 1. Contrôles du dépôt

`scripts/check-all.sh`, qui n'est plus qu'un point d'entrée : il lance
`tools/HBA.Controls`, vingt contrôles écrits en C#. Chacun attrape une classe
d'erreurs que le compilateur ne voit pas — une dépendance injectée que personne
ne fournit, un `using` manquant sur un namespace frère, un projet non copié dans
une image, une table configurée sans migration.

**`dotnet` absent fait échouer ce travail.** Rendre 0 « pour ne pas bloquer »
transformerait la barrière en décoration — « les 0 contrôles passent ».

### 2. Compilation et tests

`dotnet build` sur la solution, puis **un processus par projet de test**. Ce
découpage n'est pas un confort : quatre harnais posent leur configuration en
variables d'environnement, au niveau du processus, et aucun ne les remet à leur
valeur d'origine. Dans un hôte partagé, le dernier écrivain gagne et une suite
lit la base d'une autre.

Un projet en échec est **nommé** : annotation `::error::`, bilan en fin de
boucle, et le détail de chaque cas — assertion, exception du serveur, haut de la
pile — dans le résumé de l'exécution, via `resume-tests`.

### 3. Services affectés

`tools/HBA.Controls images-affectees` calcule les images réellement touchées
depuis le **graphe de références des `.csproj`**, transitivement. Une liste de
chemins tenue à la main échouerait autrement, et en silence : le service n'est
pas reconstruit, l'image publiée reste l'ancienne, et la correction qu'on croit
déployée ne l'est pas.

Sur `dev`, `staging` et `develop`, la matrice contient **tout** : ces branches
déploient, et chaque image référencée doit exister avec ce SHA.

### 4. Image *<service>* (matrice)

Construction, publication sur `ghcr.io`, scan Trivy, **signature cosign sans
clé**. L'identité du signataire est le workflow lui-même, attesté par Sigstore :
aucune clé privée à stocker ni à faire tourner.

Le scan Trivy **ne fait pas échouer** la publication : une CVE publiée dans une
dépendance transitive bloquerait toutes les PR d'un coup, y compris celle qui la
corrige.

---

## `deploy-compose.yml` — la production

Lancement **manuel**, deux entrées :

| entrée | valeur |
|---|---|
| `tag` | le SHA publié par la CI — celui des images à déployer |
| `tags_ansible` | vide pour tout ; sinon `preparation`, `transfert`, `images`, `migration`, `sujets`, `demarrage`, `tls`, `verification` |

Le travail passe par l'environnement GitHub **`prod`** : c'est lui qui porte les
secrets. Un nom d'environnement inconnu n'est pas une erreur pour GitHub — il le
crée à la volée, **vide** — d'où un déploiement qui partirait sans un seul
secret.

### L'ordre des étapes, et pourquoi

1. **Le fichier d'environnement**, écrit depuis `PROD_ENV_FILE` en `0600`.
2. **`scripts/verifier-env-compose.sh`** liste d'un coup les quarante-six
   variables absentes, vides, ou portant un `$` non échappé. Sans lui,
   `${VAR:?…}` arrête l'interpolation à la **première** manquante : neuf clés
   oubliées font neuf allers-retours vers le VPS.
3. **SSH**, avec `known_hosts` vérifié par `ssh-keygen -F` sur `[hôte]:port`.
   Un port non standard s'écrit entre crochets ; une ligne au format du port 22
   ne correspond à rien et rend un « Host key verification failed » muet.
4. **Les signatures**, vérifiées pour les vingt images du compose avant tout
   transfert. La liste est **lue dans le compose**, pas tenue à la main.
5. **Ansible** — `ansible/deployer-prod.yml`, huit blocs étiquetés.

### Ce qui n'est PAS couvert

- **La porte du §23 sur les vulnérabilités** n'est pas portée ici. `cd.yml` la
  portait avant sa suppression. Le rescan des vingt images au registre coûte
  plusieurs minutes ; le faire sur un sous-ensemble ou en ignorant le code de
  sortie serait pire que de l'annoncer absente.
- **Les images tierces** — Traefik, Kafka, MinIO, Redis — ne sont pas signées
  par nous et ne sont pas vérifiées.
- **Le VPS de base de données** (`10.20.0.2`, derrière WireGuard) n'entre
  jamais dans l'inventaire Ansible : les rôles de préparation y appliqueraient
  le durcissement nftables, qui fermerait 5432. Les quatorze bases
  deviendraient injoignables, et le symptôme serait un **délai d'attente** —
  indiscernable d'un mot de passe faux ou d'une route absente.

---

## Les secrets et variables

Environnement `prod` :

| nom | nature |
|---|---|
| `PROD_ENV_FILE` | secret — les quarante-six variables du compose |
| `VPS_SSH_KEY` | secret — clé privée de déploiement |
| `VPS_KNOWN_HOSTS` | secret — sortie de `ssh-keyscan -p <port> <hôte>` |
| `GHCR_TOKEN` | secret — lecture du registre depuis le VPS |
| `VPS_HOST`, `VPS_PORT`, `VPS_USER` | variables |

`VPS_KNOWN_HOSTS` doit contenir la **sortie** de `ssh-keyscan`, pas la commande.
Chaque ligne commence par `[<hôte>]:<port>`.

---

## Les actions tierces sont épinglées par empreinte

`trivy-action` et `cosign` sont figés par SHA ou par version, jamais par
`latest`. Trois pannes en une soirée l'ont imposé : une étiquette supprimée en
amont (`trivy-action@0.28.0`), puis l'étiquette qu'elle appelait elle-même
(`setup-trivy@v0.2.1`), puis un limiteur de débit.

**Épingler une action ne fige pas ce qu'elle appelle.** On hérite de la
discipline de son auteur.
