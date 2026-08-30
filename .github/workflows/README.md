# Workflows GitHub Actions

Ce dossier porte la CI/CD du backend HBAExpress.

## Workflows

- `ci.yml` — contrôles dépôt, compilation .NET, tests, build Docker, SBOM, scan
  Trivy, signature cosign et publication GHCR.
- `deploy-branches.yml` — déploiement automatique après une CI verte, selon la
  branche poussée.
- `cd.yml` — promotion manuelle GitOps, conservée pour poser et committer un tag
  d'image dans un overlay sans appliquer directement au cluster.

## Mapping Des Branches

| Branche poussée | Environnement GitHub | Namespace Kubernetes | Overlay appliqué | Migrations |
|---|---|---|---|---|
| `dev` | `dev` | `hba-dev` | `k8s/overlays/dev` | non |
| `staging` | `staging` | `hba-staging` | `k8s/overlays/staging` | non |
| `develop` | `prod` | `hba-prod` | `k8s/overlays/prod` | oui, via `k8s/overlays/migrations-prod` |

Sur `dev`, `staging` et `develop`, la CI reconstruit toutes les images. C'est
volontaire : le déploiement pose le SHA du commit sur tout l'overlay, donc chaque
image référencée doit exister avec ce SHA.

## Prérequis GitHub

1. Aller dans le dépôt GitHub.
2. Ouvrir `Settings` → `Actions` → `General`.
3. Dans `Workflow permissions`, choisir `Read and write permissions`.
4. Cocher `Allow GitHub Actions to create and approve pull requests` seulement si
   des workflows futurs doivent ouvrir des PR. Les workflows actuels n'en ont pas
   besoin.
5. Sauvegarder.

Ces permissions permettent à `ci.yml` de publier les images dans GHCR avec
`GITHUB_TOKEN`. Le workflow de déploiement lit ensuite ces images depuis le même
registre.

## Environnements GitHub

Créer trois environnements dans `Settings` → `Environments` :

1. `dev`
2. `staging`
3. `prod`

Dans `prod`, ajouter une protection :

1. Ouvrir l'environnement `prod`.
2. Activer `Required reviewers`.
3. Ajouter au moins un validateur humain.
4. Sauvegarder.

Sans cette protection, un push sur `develop` déclenche directement un déploiement
production après CI verte.

## Secrets À Créer

Dans chaque environnement GitHub (`dev`, `staging`, `prod`), créer le secret :

| Secret | Contenu |
|---|---|
| `KUBECONFIG_B64` | kubeconfig du cluster cible, encodé en base64 sur une seule ligne |

Le secret est par environnement, pas global au dépôt. Cela évite qu'un workflow
staging puisse utiliser le kubeconfig de production.

### Générer `KUBECONFIG_B64`

Linux :

```bash
base64 -w0 kubeconfig-dev.yaml
```

macOS :

```bash
base64 < kubeconfig-dev.yaml | tr -d '\n'
```

Répéter avec le bon fichier pour chaque environnement :

```bash
base64 < kubeconfig-dev.yaml | tr -d '\n'
base64 < kubeconfig-staging.yaml | tr -d '\n'
base64 < kubeconfig-prod.yaml | tr -d '\n'
```

Coller la sortie dans `Settings` → `Environments` → `<env>` → `Secrets` →
`Add secret`.

## Droits Kubernetes Du Kubeconfig

Le kubeconfig donné à GitHub doit pouvoir faire au minimum :

- lire le namespace cible ;
- lire les Secrets attendus par `scripts/check-secrets-cluster.sh` pour staging et
  prod ;
- faire `kubectl apply --dry-run=server -k ...` ;
- créer ou modifier les objets de l'overlay ;
- lire les Jobs de migration en production ;
- lire l'état des Deployments pour `rollout status`.

Pour un premier déploiement, un kubeconfig admin du cluster fonctionne. Pour un
durcissement production, remplacer ensuite par un ServiceAccount limité au
namespace cible.

## Préparer Les Branches

Créer ou pousser les branches attendues :

```bash
git push origin dev
git push origin staging
git push origin develop
```

Le workflow `deploy-branches.yml` ne se lance pas directement sur `push`. Il se
lance après la fin du workflow `CI`, via `workflow_run`. Si la CI échoue, aucun
déploiement ne part.

## Premier Test Dev

1. Vérifier que l'environnement GitHub `dev` porte `KUBECONFIG_B64`.
2. Pousser un commit sur `dev`.
3. Aller dans `Actions` → `CI`.
4. Attendre que `CI` soit verte.
5. Aller dans `Actions` → `Deploy Branches`.
6. Vérifier que la cible affichée est `dev`.
7. Vérifier côté cluster :

```bash
kubectl -n hba-dev get pods
kubectl -n hba-dev rollout status deploy --timeout=15m
```

## Premier Test Staging

Avant de pousser sur `staging`, le DNS doit être correct :

```bash
./scripts/check-dns-ingress.sh staging
```

Puis :

```bash
git push origin staging
```

Après exécution :

```bash
kubectl -n hba-staging get pods
kubectl -n hba-staging get certificate
kubectl -n hba-staging rollout status deploy --timeout=15m
```

Si `check-dns-ingress.sh` échoue, le workflow échouera aussi au pré-vol. C'est
voulu : cert-manager ne peut pas émettre un certificat sur un domaine qui pointe
vers la mauvaise IP.

## Premier Test Production

Avant de pousser sur `develop` :

1. Vérifier que l'environnement GitHub `prod` porte `KUBECONFIG_B64`.
2. Vérifier que `prod` a un validateur humain obligatoire.
3. Vérifier le DNS :

```bash
./scripts/check-dns-ingress.sh prod
```

4. Pousser :

```bash
git push origin develop
```

5. Attendre la CI.
6. Ouvrir `Actions` → `Deploy Branches`.
7. Approuver l'environnement `prod`.

Le workflow production fait ensuite :

1. pose le SHA du commit dans `k8s/overlays/prod` dans le runner ;
2. pose le même SHA dans `k8s/overlays/migrations-prod` ;
3. vérifie les signatures cosign des images ;
4. lance `scripts/preflight-k8s.sh prod --cluster` ;
5. applique les migrations ;
6. attend que tous les Jobs de migration soient `Complete` ;
7. applique `k8s/overlays/prod` ;
8. attend le rollout des Deployments.

## Vérifications Après Déploiement

Dev :

```bash
kubectl -n hba-dev get pods
kubectl -n hba-dev get deploy
```

Staging :

```bash
kubectl -n hba-staging get pods
kubectl -n hba-staging get certificate
kubectl -n hba-staging logs deploy/gateway-service --tail=100
```

Production :

```bash
kubectl -n hba-prod get pods
kubectl -n hba-prod get job -l app.kubernetes.io/component=migration
kubectl -n hba-prod rollout status deploy --timeout=15m
```

## Rollback

Rollback staging :

```bash
./scripts/rollback-k8s.sh staging <service>
```

Rollback production :

```bash
./scripts/rollback-k8s.sh prod <service>
```

Après un rollback production, Git ne reflète plus ce qui tourne. Il faut ensuite
repromouvoir le SHA restauré avec `cd.yml` ou refaire un commit de déploiement,
sinon le prochain `kubectl apply` remettra la version fautive.

## Dépannage

`KUBECONFIG_B64 absent` :

- le secret n'est pas créé dans l'environnement GitHub ciblé ;
- vérifier que le nom est exactement `KUBECONFIG_B64`.

`ImagePullBackOff` :

- vérifier que la CI a publié l'image du service avec le SHA du commit ;
- vérifier que le secret Kubernetes `ghcr` existe dans le namespace.

`preflight-k8s.sh staging` échoue sur DNS :

- corriger l'enregistrement A de `backendapi.marketplace-staging.hba-marketplace.fr` ;
- attendre la propagation DNS ;
- relancer le workflow.

`prod` attend une approbation :

- c'est la protection de l'environnement GitHub `prod` ;
- approuver dans l'écran du workflow GitHub Actions.
