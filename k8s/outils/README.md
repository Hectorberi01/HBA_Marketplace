# Outils d'exploitation — hors du build Kustomize, et délibérément

Rien de ce dossier n'est référencé par `k8s/base/kustomization.yaml`. Ce n'est pas
un oubli : ces objets s'appliquent **à la main**, sur le cluster où on en a besoin.

**POURQUOI PAS DANS LA BASE.** Une console de cluster déployée par le même
`kubectl apply -k` que les services arriverait aussi en production, où personne ne
l'a demandée. Un objet qu'on n'a pas décidé d'installer est un objet que personne
ne surveille.

## Ce qu'il y a ici

| Fichier | Ce qu'il pose |
|---|---|
| `acces-lecture.yaml` | un namespace, un compte de service, et un droit de LECTURE sur tout le cluster |
| `headlamp-valeurs.yaml` | les valeurs Helm de la console — dont celles qui l'empêchent de se lier à `cluster-admin` |

## La règle qui compte : aucune console n'est exposée

**NE JAMAIS POSER D'INGRESS SUR UNE CONSOLE DE CLUSTER.** C'est le vecteur qui a
servi à compromettre des clusters entiers — un tableau de bord Kubernetes joignable
depuis Internet donne, selon ses droits, la lecture des secrets ou l'exécution
dans les pods. Le fait qu'il demande un jeton ne suffit pas : la surface est
publique, et les versions anciennes en ont laissé passer.

L'accès se fait par `kubectl port-forward`, donc **à travers le kubeconfig**. Celui
qui n'a pas le kubeconfig n'atteint rien, et le kubeconfig est déjà traité comme
un mot de passe (`infra/ansible/.gitignore`).

```bash
kubectl -n hba-outils port-forward svc/<console> 8080:80
# puis http://localhost:8080 dans le navigateur
```

## Le droit posé ici est la LECTURE, et c'est un choix

`acces-lecture.yaml` lie le compte au `ClusterRole` intégré **`view`**. Il donne
les pods, les journaux, les événements, les Deployments, les Services — tout ce
qu'il faut pour « visualiser ».

**IL NE DONNE PAS LES SECRETS.** `view` exclut délibérément `secrets` : la console
affichera l'existence de `hba-platform` et non ses valeurs. C'est exactement ce
qu'on veut d'un outil d'observation — les quatorze mots de passe de base n'ont
aucune raison de transiter par un navigateur.

Pour agir depuis la console — redémarrer un Deployment, éditer un objet — il faut
`edit` ou `cluster-admin` à la place de `view`. **À faire sciemment, pas par
défaut** : une console en lecture seule ne peut pas casser la production par un
clic mal placé.

## La console retenue : Headlamp

**POURQUOI PAS LE TABLEAU DE BORD OFFICIEL.** Kubernetes Dashboard v7 ne s'installe
plus qu'en Helm, et sa charte déploie plusieurs composants — l'API, l'interface,
le collecteur de métriques et une passerelle Kong. Sur un VPS unique qui porte
déjà huit pods applicatifs, deux magasins de données, un broker et huit pods
système, c'est une brique lourde pour une fonction d'observation.

Headlamp tient en **un pod**, se pilote au `port-forward`, et accepte un jeton de
compte de service — exactement le mode d'accès que ce dossier impose déjà.

```bash
helm repo add headlamp https://kubernetes-sigs.github.io/headlamp/
helm repo update
```

### La charte se lie à `cluster-admin` par défaut — le fichier de valeurs le désarme

`helm show values headlamp/headlamp` rend, tel quel :

```yaml
clusterRoleBinding:
  create: true
  clusterRoleName: cluster-admin
```

Installée sans valeurs, la console peut **tout** faire sur le cluster, y compris
lire les quatorze mots de passe de base du Secret `hba-platform` — l'inverse exact
de ce que `acces-lecture.yaml` a posé deux paragraphes plus haut. Rien n'avertit :
l'installation réussit, la console s'ouvre, tout fonctionne. Le défaut ne se voit
qu'en relisant les objets RBAC après coup.

`headlamp-valeurs.yaml`, dans ce dossier, pose trois choses :

| Valeur | Effet |
|---|---|
| `serviceAccount.create: false` + `name: console` | le pod tourne sous le compte que `acces-lecture.yaml` lie à `view` |
| `clusterRoleBinding.create: false` | la charte ne crée aucune liaison — le seul droit vit dans un fichier versionné |
| `service.type: ClusterIP` | posé explicitement, même si c'est le défaut : un défaut n'est pas une décision |

**`create: false` PLUTÔT QUE `clusterRoleName: view`**, qui marcherait aussi. La
différence tient à qui possède le droit : une liaison créée par Helm est recréée à
chaque `helm upgrade`, et une valeur oubliée lors d'une mise à jour la ramène
silencieusement à `cluster-admin`. Hors de Helm, elle ne bouge que si on la
modifie.

### Vérifier AVANT d'installer, pas après

```bash
helm template console headlamp/headlamp \
  --namespace hba-outils \
  --values k8s/outils/headlamp-valeurs.yaml \
  | grep -E '^kind:|serviceAccountName:|  name:'
```

Ce qu'on veut lire : **aucun `kind: ClusterRoleBinding`**, aucun
`kind: ServiceAccount`, et un `serviceAccountName: console` dans le Deployment. Si
un `ClusterRoleBinding` apparaît, la charte a changé ses clés — ne pas installer,
relire `helm show values`.

### Installer DEPUIS LE VPS, et non depuis le poste

**C'EST LA VOIE À PRÉFÉRER, ET LA RAISON N'EST PAS LE CONFORT.**

Depuis un poste de développement, `kubectl` et `helm` visent le contexte courant.
Si ce contexte est celui d'un cluster local — Docker Desktop, OrbStack, kind — les
commandes **réussissent** sur la mauvaise machine : namespace créé, release
installée, aucun message d'erreur. On cherche ensuite le pod sur le serveur, où il
n'a jamais été.

Le contexte de k3s s'appelle `default`, un nom trop générique pour se distinguer
dans une invite. Depuis le VPS, la question ne se pose pas : un seul cluster est
joignable.

```bash
# 1. porter les deux fichiers sur la machine
scp k8s/outils/acces-lecture.yaml k8s/outils/headlamp-valeurs.yaml \
    root@<ip-du-vps>:/root/

# 2. sur le VPS
ssh root@<ip-du-vps>

export KUBECONFIG=/etc/rancher/k3s/k3s.yaml

# helm n'est pas dans les dépôts Debian ; le script officiel est la voie amont
curl -fsSL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash
helm version

helm repo add headlamp https://kubernetes-sigs.github.io/headlamp/
helm repo update

kubectl apply -f /root/acces-lecture.yaml
helm install console headlamp/headlamp \
  --namespace hba-outils \
  --version "$(cat /root/VERSION-CONSOLE)" \
  --values /root/headlamp-valeurs.yaml

kubectl -n hba-outils get pod
kubectl get clusterrolebinding | grep -i headlamp     # doit être VIDE
```

**`curl … | bash` FAIT TOURNER UN SCRIPT DISTANT EN ROOT.** C'est la méthode que
recommande le projet Helm, et elle vaut ce que vaut la confiance accordée à ce
dépôt. Pour éviter le tube, télécharger le script, le lire, puis l'exécuter.

## La version de la console est figée, et enregistrée

`helm install console headlamp/headlamp` sans `--version` prend **la dernière
publiée au moment où on tape la commande**. Deux conséquences, aucune visible le
jour de l'installation :

- réinstaller six mois plus tard ne redonne pas la même console, et rien ne le
  dit ;
- une version amont peut réintroduire un défaut que `headlamp-valeurs.yaml`
  désarme aujourd'hui — le `clusterRoleName: cluster-admin` par défaut de la
  charte en est l'exemple vivant. Une valeur renommée en amont cesse
  silencieusement de s'appliquer.

La version retenue vit donc dans `k8s/outils/VERSION-CONSOLE`, un fichier d'une
ligne, versionné avec le reste.

**Choisir une version** — les commandes ci-dessus ne marchent pas sans ce
fichier, c'est délibéré :

```bash
helm repo add headlamp https://kubernetes-sigs.github.io/headlamp/
helm repo update
helm search repo headlamp/headlamp --versions | head
```

Retenir une version publiée, l'écrire dans le fichier, et la commiter :

```bash
echo "<version>" > k8s/outils/VERSION-CONSOLE
```

**Monter de version** se fait en changeant ce fichier, puis :

```bash
helm upgrade console headlamp/headlamp \
  --namespace hba-outils \
  --version "$(cat k8s/outils/VERSION-CONSOLE)" \
  --values k8s/outils/headlamp-valeurs.yaml

kubectl get clusterrolebinding | grep -i headlamp     # doit rester VIDE
```

Le contrôle de la ligne suivante n'est pas décoratif : `helm upgrade` réapplique
les valeurs de la charte, et c'est exactement le moment où un défaut amont
revient. Le vérifier après CHAQUE montée de version, pas seulement à
l'installation.

**Ce que ce fichier ne couvre pas.** Il fige la version de la *charte*, pas
l'image du conteneur — la charte décide quelle image elle tire. Il n'installe
rien tout seul : personne ne vérifie qu'une console déjà en place correspond
encore à ce qui est écrit là.

### Ouvrir la console installée depuis le VPS

Le `port-forward` tourne sur le VPS, un tunnel SSH l'amène jusqu'au navigateur :

```bash
# sur le VPS, à laisser tourner
kubectl -n hba-outils port-forward svc/console-headlamp 8080:80

# sur le poste, dans un autre terminal
ssh -N -L 8080:127.0.0.1:8080 root@<ip-du-vps>
```

Puis `http://localhost:8080`. **Aucun kubeconfig n'est nécessaire sur le poste**
par cette voie — et donc aucun contexte à vérifier.

Le jeton se crée sur le VPS :

```bash
kubectl -n hba-outils create token console --duration=8h
```

### Variante : installer depuis le poste

À réserver au cas où `helm` n'est pas souhaité sur le serveur. **Vérifier le
contexte avant chaque commande**, sans exception :

```bash
kubectl config current-context      # default = k3s ; orbstack = votre machine
```



```bash
kubectl apply -f k8s/outils/acces-lecture.yaml      # si ce n'est pas déjà fait
helm install console headlamp/headlamp \
  --namespace hba-outils \
  --version "$(cat k8s/outils/VERSION-CONSOLE)" \
  --values k8s/outils/headlamp-valeurs.yaml
```

Ne pas installer dans `kube-system`, que la documentation amont propose : c'est le
namespace du plan de contrôle, et un outil d'exploitation y hériterait de
tolérances et d'une priorité de planification qui ne sont pas les siennes.

**Contrôler le résultat une seconde fois, sur le cluster :**

```bash
kubectl get clusterrolebinding | grep -i headlamp     # doit être VIDE
kubectl -n hba-outils get pod -o jsonpath='{.items[*].spec.serviceAccountName}'
```

### Ouvrir la console

**LE NOM DU SERVICE N'EST PAS `headlamp`.** La charte le compose à partir du nom
de release : `helm install console …` produit `console-headlamp`. Écrire
`svc/headlamp` en dur donne `Error from server (NotFound)` — un message qui
ressemble à une installation ratée alors que tout est en place.

On ne le devine pas, on le relève :

```bash
kubectl -n hba-outils get svc
```

Puis, sans dépendre du nom :

```bash
SVC=$(kubectl -n hba-outils get svc -o name | head -1)
kubectl -n hba-outils port-forward "$SVC" 8080:80
```

Et le jeton à coller dans l'écran de connexion :

```bash
kubectl -n hba-outils create token console --duration=8h
```

**`--duration` n'est pas décoratif** : sans lui, le jeton vit une heure par défaut sur la plupart des
clusters, mais rien n'empêche d'en demander un très long — un jeton de console qui
traîne dans un gestionnaire de mots de passe est une clé d'accès au cluster que
personne ne fait tourner.

## Ce que ce dossier ne fait pas

- **Il n'installe toujours aucune console.** La section ci-dessus donne les
  commandes ; elle ne verse aucun manifeste tiers dans ce dépôt. Le recopier en
  ferait une copie périmée le jour de la première mise à jour amont — et c'est
  précisément la charte de Headlamp qui change ses clés d'une version à l'autre.
- **Il ne fige pas la version de la console.** `helm install` prend la dernière
  publiée. Pour un outil d'observation qu'on réinstalle rarement, c'est tenable ;
  pour tout ce qui touche aux charges applicatives, le §13 exige l'inverse.
- **Il ne remplace pas la supervision.** Prometheus, Loki et Grafana sont une autre
  brique (`k8s/base/observability/`) et répondent à une autre question : ce
  dossier montre l'ÉTAT du cluster, pas l'historique des métriques.
