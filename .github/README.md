# Intégration et déploiement continus

Workflows GitHub Actions (§13, §14 du cahier Infrastructure).

## Deux fichiers, pas quinze

`workflows/ci.yml` — contrôles, compilation, tests, puis image OCI par service
affecté : SBOM, scan, signature, publication.
`workflows/cd.yml` — promotion manuelle d'une image DÉJÀ construite vers un
environnement.
`workflows/deploy-branches.yml` — déploiement automatique après CI verte selon
la branche poussée.

**Un pipeline par service, pas un pipeline pour tout** reste la règle, mais elle
est tenue autrement que par quinze fichiers. Sa raison — ne pas reconstruire
treize services à chaque commit, pour pouvoir corriger la restauration sans
redéployer les paiements — est satisfaite par `tools/HBA.Controls`, qui
calcule les images réellement touchées.

Quinze fichiers quasi identiques auraient satisfait la lettre et créé le défaut
que ce dépôt combat partout ailleurs : des copies qui divergent, dont on en oublie
deux le jour où l'on change une étape.

## Les services affectés sont calculés, pas listés

```bash
dotnet run --project tools/HBA.Controls -- images-affectees origin/main  # ce qu'un diff reconstruit
dotnet run --project tools/HBA.Controls -- images-affectees --liste      # les images et leur fermeture
```

Le calcul suit le **graphe de références des `.csproj`**, transitivement — le même
que `check-dockerfiles.py`. Une liste de chemins tenue à la main deviendrait fausse
au premier `<ProjectReference>` ajouté, et le défaut serait silencieux dans le
mauvais sens : le service n'est pas reconstruit, l'image publiée reste l'ancienne,
et la correction qu'on croit déployée ne l'est pas.

Trois fichiers reconstruisent tout : `Directory.Build.props`,
`Directory.Packages.props` et `HBA.sln` — ils changent le cadre cible ou les
versions de paquets de chaque projet.

À l'inverse, `docs/`, `k8s/`, `infra/`, `tests/`, `scripts/` et `.github/`
n'affectent aucune image.

*Détail vérifié : `api-gateway` ne référence aucun projet de `shared/`. Un
changement dans `HBA.Shared.Domain` reconstruit 29 images sur 30, et l'exclusion de
la passerelle est correcte, pas un oubli.*

## Où les portes se ferment

| Étape | Sur PR | Sur `main` | Avant production |
|---|---|---|---|
| `check-all.sh` | bloque | bloque | bloque |
| tests | bloque | bloque | bloque |
| scan dépendances | signale | signale | — |
| scan image (Trivy) | — | signale | **bloque** (§23) |
| signature cosign | — | signe | **vérifie** |

**Le scan de vulnérabilité ne bloque pas la publication, et c'est délibéré.**
Une CVE publiée dans une dépendance transitive bloquerait toutes les PR d'un coup,
y compris celle qui la corrige. Le §23 demande « aucune criticité bloquante » avant
la **production** — la porte est dans `cd.yml`.

## Déploiement automatique par branche

`deploy-branches.yml` démarre uniquement après une exécution CI réussie :

| Branche | Environnement GitHub | Namespace | Overlay |
|---|---|---|---|
| `dev` | `dev` | `hba-dev` | `k8s/overlays/dev` |
| `staging` | `staging` | `hba-staging` | `k8s/overlays/staging` |
| `develop` | `prod` | `hba-prod` | `k8s/overlays/prod` |

Chaque environnement GitHub doit porter un secret `KUBECONFIG_B64`, qui contient
le kubeconfig du cluster encodé en base64 :

```bash
base64 -w0 kubeconfig-staging.yaml
```

Sur macOS :

```bash
base64 < kubeconfig-staging.yaml | tr -d '\n'
```

Le workflow pose le SHA du commit sur les images de l'overlay dans le runner, sans
committer cette modification. En production, il applique d'abord
`k8s/overlays/migrations-prod`, puis `k8s/overlays/prod`.

## Ce que `cd.yml` garde

`cd.yml` écrit le tag dans l'overlay Kustomize, le commite, et peut encore servir
à une promotion GitOps manuelle. La réconciliation automatique par branche passe
désormais par `deploy-branches.yml`.

Le choix entre les deux modes reste opérationnel :

- **Argo CD / Flux** réconcilient depuis Git — le commit suffit, et le cluster
  reste reconstructible depuis le dépôt, ce qu'exige le §25 ;
- **`kubectl apply` depuis le workflow** — plus direct, mais il faut confier un
  kubeconfig de production à GitHub Actions, et l'état du cluster cesse d'être
  déductible du dépôt.

## Le contrôle qui manquait

`scripts/check-workflows.py`, dans `check-all.sh`.

**Un workflow au YAML invalide ne se plaint pas — il ne tourne pas.** Aucune
exécution n'apparaît, aucune notification ne part, aucun statut ne remonte sur la
PR. On croit la CI verte alors qu'elle n'a jamais démarré, et on s'en aperçoit en
cherchant pourquoi une régression est passée.

Le défaut qui a motivé ce contrôle a été commis en écrivant `ci.yml` : un
`- name:` contenant « : » sans guillemets, que YAML lit comme un mapping.
