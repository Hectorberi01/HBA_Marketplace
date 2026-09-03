# Intégration et déploiement continus

Workflows GitHub Actions (§13, §14 du cahier Infrastructure).

## Deux fichiers, pas quinze

`workflows/ci.yml` — contrôles, compilation, tests, puis image OCI par service
affecté : SBOM, scan, signature, publication.

`workflows/deploy-compose.yml` — déploiement **manuel** d'un tag donné sur le
VPS de production, par Ansible, en Docker Compose derrière Traefik.

Le détail de chaque étape et de ses raisons vit dans
[`workflows/README.md`](workflows/README.md).

**Un pipeline par service, pas un pipeline pour tout** reste la règle, mais elle
est tenue autrement que par quinze fichiers. Sa raison — ne pas reconstruire
vingt services à chaque commit, pour pouvoir corriger la restauration sans
redéployer les paiements — est satisfaite par `tools/HBA.Controls`, qui calcule
les images réellement touchées.

Quinze fichiers quasi identiques auraient satisfait la lettre et créé le défaut
que ce dépôt combat partout ailleurs : des copies qui divergent, dont on en
oublie deux le jour où l'on change une étape.

## Les services affectés sont calculés, pas listés

```bash
dotnet run --project tools/HBA.Controls -- images-affectees origin/main  # ce qu'un diff reconstruit
dotnet run --project tools/HBA.Controls -- images-affectees --tous       # tout, pour une branche qui déploie
```

Le calcul suit le **graphe de références des `.csproj`**, transitivement. Une
liste de chemins tenue à la main deviendrait fausse au premier
`<ProjectReference>` ajouté, et le défaut serait silencieux dans le mauvais sens :
le service n'est pas reconstruit, l'image publiée reste l'ancienne, et la
correction qu'on croit déployée ne l'est pas.

Trois fichiers reconstruisent tout : `Directory.Build.props`,
`Directory.Packages.props` et `HBA.sln` — ils changent le cadre cible ou les
versions de paquets de chaque projet.

À l'inverse, `docs/`, `tests/`, `scripts/`, `ansible/` et `.github/` n'affectent
aucune image.

*Détail vérifié : `api-gateway` ne référence aucun projet de `shared/`. Un
changement dans `HBA.Shared.Domain` reconstruit la quasi-totalité des images, et
l'exclusion de la passerelle est correcte, pas un oubli.*

## Où les portes se ferment

| Étape | Sur PR | Sur une branche qui déploie | Avant production |
|---|---|---|---|
| Vingt contrôles du dépôt | oui | oui | — |
| Compilation et tests | oui | oui | — |
| Scan Trivy des images | — | oui, **sans bloquer** | non porté (voir plus bas) |
| Signature cosign | — | oui | — |
| **Vérification** des signatures | — | — | **oui**, avant tout transfert |
| Les 46 variables du compose | — | — | oui, listées d'un coup |
| Empreinte SSH du VPS | — | — | oui, sur `[hôte]:port` |

Le scan Trivy ne bloque pas la publication — une CVE dans une dépendance
transitive arrêterait toutes les PR, y compris celle qui la corrige. La porte du
§23 (« aucune criticité bloquante avant la production ») était portée par
`cd.yml`, supprimé avec le chemin Kubernetes : **elle n'est pas encore portée
dans le déploiement Compose.** C'est la dette la plus visible de ce dossier.

## Ce qui a été retiré, et pourquoi c'est écrit ici

Le 3 septembre 2026, le chemin Kubernetes est sorti du dépôt : `k8s/`,
`infra/ansible/`, `infra/terraform/`, `cd.yml`, `deploy-branches.yml`, et les
quatre scripts `*-k8s.sh`. La production tourne sur Docker Compose.

`deploy-branches.yml` se déclenchait après chaque CI verte. Depuis que `develop`
est la branche par défaut, il tentait un déploiement Kubernetes à chaque commit
et échouait sur un secret absent. Un travail rouge à chaque commit apprend à
ignorer le rouge — et c'est ainsi qu'une vraie panne finit par passer inaperçue.
