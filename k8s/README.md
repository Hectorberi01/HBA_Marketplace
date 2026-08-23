# Kubernetes

Manifests Kustomize du cahier Infrastructure. Cible : **VPS OVH auto-hébergé**,
manifests **Kustomize** — voir `docs/DECISIONS.md`, D9.

**Les sondes de vivacité et de disponibilité ne sont pas la même chose.** Un
service qui a perdu son broker n'est pas mort — il ne doit plus recevoir de
trafic, mais le redémarrer n'y changera rien. `check-k8s.py` refuse une liveness
qui sonde `/health/ready`, et une readiness qui sonde `/health/live`.

## Ce qui est posé

```
k8s/
├── base/
│   ├── namespaces/   le Namespace, dont le nom est remplacé par chaque overlay
│   ├── common/       ConfigMap partagé + contrat de Secret (vide, et il le reste)
│   ├── services/     _service/ = le gabarit ; un dossier par service qui le reprend
│   ├── policies/     NetworkPolicies deny-by-default (§5)
│   ├── apps/         la passerelle : Ingress, TLS cert-manager, 2 replicas
│   ├── data/         Postgres (CloudNativePG), Redis, MinIO
│   ├── kafka/        le cluster Strimzi ; les topics sont dans les overlays
│   └── observability/ vide : brique tierce (kube-prometheus-stack), par Helm
└── overlays/{dev,staging,prod}/
    └── kafka-topics.yaml   généré depuis les [HbaEvent] du code
```

## Le gabarit, et pourquoi il n'y a pas quatorze copies

`base/services/_service/` porte un Deployment nommé `service`. Chaque dossier
voisin le reprend avec `namePrefix: identity-`, ce qui donne `identity-service` —
sur les cinq objets d'un coup, références comprises : Kustomize réécrit tout seul
le `serviceAccountName` et le `scaleTargetRef` du HPA.

La raison est opérationnelle. Quatorze copies divergent : le jour où l'on corrige
un seuil de sonde ou une capacité Linux, on le corrige quatorze fois — et on en
oublie deux, qui sont précisément celles qui poseront problème plus tard.

Ajouter un service tient donc en un dossier de trente lignes.

## Vérifier avant d'appliquer

```bash
kustomize build k8s/overlays/dev
python3 scripts/check-k8s.py          # inclus dans ./scripts/check-all.sh
```

`check-k8s.py` **construit** les trois overlays et vérifie le résultat : non-root,
les trois sondes, requests/limits, pas de `latest` en production, deny-by-default
présent, aucun secret en clair. Lire `base/` ne prouverait rien — c'est l'overlay
qui décide, et un patch peut défaire en silence ce que la base garantissait.

## Trois choses à savoir avant de déployer

### 1. Les NetworkPolicies ne font rien sans un CNI qui les applique

C'est le piège de ce dossier. Sur un cluster dont le CNI ne les gère pas — k3s
avec flannel, par défaut — les objets sont créés, `kubectl get netpol` les
affiche, et **aucun paquet n'est filtré**. Tout paraît en place.

À vérifier une fois, en essayant de joindre Postgres depuis un pod quelconque. Si
la connexion s'ouvre, ces fichiers sont décoratifs. Installer Calico ou Cilium, ou
démarrer k3s avec son contrôleur de politiques réseau.

### 2. Le Secret est vide, et un déploiement échouera dessus

Le §12 interdit tout secret dans Git. `base/common/secret.yaml` ne déclare que le
NOM que les Deployments montent et la liste des clés attendues. Sans lui,
`envFrom` échouerait sur un secret absent, sans dire lesquelles fournir.

Reste à trancher comment le remplir :

- **External Secrets Operator / Vault Agent** — l'objet est écrasé au runtime
  depuis le vrai coffre, le fichier de Git reste vide. Le cluster demeure
  reconstructible depuis Git, ce qu'exige le §25.
- **`kubectl create secret` hors GitOps** — plus simple, mais le cluster cesse
  d'être reconstructible depuis Git seul.

Tant que rien n'est décidé, un déploiement s'arrête au démarrage sur une clé de
signature JWT vide. C'est le bon échec : bruyant, immédiat, impossible à confondre
avec un service en bonne santé.

### 3. Le tag de production est un placeholder

`overlays/prod` porte `REMPLACE-PAR-LA-PROMOTION`. Le §13 interdit `latest` et
impose des images immuables identifiées par SHA ou semver ; la promotion pose le
tag (`kustomize edit set image`) au lieu de reconstruire. Un tag mouvant committé
ici ferait d'un `kubectl apply` rejoué un déploiement différent du précédent.

### 4. Les domaines sont des placeholders réservés

Les trois overlays servent `api[.dev|.staging].hba-express.**example**`.
`.example` est réservé par la RFC 2606 : il ne résoudra jamais. C'est délibéré —
un placeholder qui résout est un placeholder qu'on oublie de remplacer.

`check-k8s.py` les signale sans bloquer, et refuse en revanche que deux
environnements partagent un hôte : c'est le défaut qu'un copier-coller d'overlay
produit, et il fait entrer du trafic de production dans un cluster de validation
sans que rien ne le signale — les deux répondent 200.

## Le data plane

Postgres passe par **CloudNativePG**, pas par un StatefulSet écrit à la main. D9
a acté que l'auto-hébergement met le §18 entièrement à notre charge — WAL/PITR,
rétention 30 jours, test de restauration mensuel. Écrit à la main, cela devient un
CronJob `pg_basebackup` que personne n'a jamais restauré, et « une sauvegarde
n'est valide qu'après un test de restauration » est la phrase exacte du §18.

**Les deux opérateurs doivent être installés avant tout.** CloudNativePG et
Strimzi. Sans eux, les `Cluster` et `Kafka` sont créés et **rien ne se passe** :
aucun pod, aucune erreur. Un CRD sans contrôleur est un objet inerte dans etcd, et
ça ne ressemble pas à une panne — ça ressemble à l'attente.

**Les sauvegardes Postgres ne vont PAS dans MinIO.** MinIO tourne à côté, sur
les mêmes nœuds. Y archiver les WAL donnerait un PITR parfaitement fonctionnel
jusqu'au jour où l'on en a besoin — c'est-à-dire le jour où le cluster est perdu.
`destinationPath` pointe OVH Object Storage, hors cluster. C'est le seul endroit
de l'infrastructure où l'on paie délibérément une dépendance externe.

### Les topics sont générés, pas écrits

Le §19.2 met l'environnement dans le nom du topic, donc les trois listes diffèrent
et Kustomize ne sait pas réécrire un segment au milieu d'un nom.

```bash
python3 scripts/k8s-kafka-topics.py     # après tout ajout d'événement
```

`check-k8s.py` refuse un fichier périmé. La raison : un topic manquant n'échoue
pas — le broker le crée à la volée avec une partition et sans réplication. Le
service publie, tout paraît fonctionner, et la perte d'un broker perd des
messages. C'est le défaut le plus silencieux de tout le data plane.

## Ce qui n'est pas encore là

- Les trois BFF (client, vendeur, livreur). Leur `Program.cs` ne sert que les
  sondes : déployer un pod qui ne rend rien derrière un Ingress produit des 502
  qu'on cherche du mauvais côté.
- Le data plane : Postgres, Redis, Kafka, MinIO **dans** le cluster (D9). Avec la
  conséquence lourde du §18 — snapshots, WAL/PITR, rétention 30 jours, test de
  restauration mensuel — que personne ne fournit ici.
- Les NetworkPolicies par paire de services. La version actuelle ferme l'entrée et
  la sortie du namespace ; le resserrement viendra quand le graphe d'appels sera
  stable, `check-di.py` le connaissant déjà.
- Terraform (§20) : réseau, DNS, VMs OVH, et Ansible pour poser k3s.
