# Compilation des images

CE FICHIER ETAIT `infra/docker/README.md`.

Il y voisinait une pile compose que plus rien ne lisait — le `Makefile` ne
connait que `docker-compose.dev.yml` — et il en portait le titre, « Images »,
alors qu'il ne parle pas de cette pile : il documente les `Dockerfile` des
services, qui eux sont bien construits tous les jours. Le dossier a ete retire
(`_to_delete/2026-08-26-pile-compose-serveur/`) ; ce texte ne devait pas partir
avec lui.

CE QU'IL NE COUVRE PAS : la pile de developpement elle-meme. Pour la lancer,
voir `infra/README.md` et `make up`.

---

Dockerfiles communs et images de base.

Une image par service, construite indépendamment : c'est la condition pour déployer une correction sans reconstruire les douze autres.

## Architecture de compilation

Les 23 Dockerfiles de service portent :

```dockerfile
ARG DOTNET_BUILD_PLATFORM=linux/amd64
FROM --platform=$DOTNET_BUILD_PLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
```

**L'étage de compilation est en amd64, et c'est `protoc` qui l'impose.**

### Pourquoi, exactement

La ligne n'a longtemps porté aucune explication. Elle a donc été mise à
`$BUILDPLATFORM` — natif arm64 sur un Mac Apple Silicon — au motif que la sortie
n'en dépend pas. La construction, elle, en dépend :

```
error MSB6006: ".../grpc.tools/2.71.0/tools/linux_arm64/protoc" exited with code 139
```

`139 = 128 + SIGSEGV`. Les 22 projets `*.Contracts.Grpc` de `shared/contracts/`
sont copiés dans **toutes** les images : le binaire `protoc` arm64 livré par
Grpc.Tools plante, et plus aucune image ne se construit.

### Ce qui restait vrai de l'analyse, et ce qui ne l'était pas

| Affirmation | Verdict |
|---|---|
| Aucun projet ne fixe de `RuntimeIdentifier` ; la publication est portable | vrai |
| La CI construit sur `ubuntu-latest`, donc déjà en amd64 | vrai |
| L'étage final n'est pas épinglé — on compile en amd64 pour exécuter sur un `aspnet:9.0` arm64 | vrai, et sans effet sur du managé pur |
| **Donc la plateforme de compilation est indifférente** | **faux** |

L'erreur de raisonnement tient en une phrase : la plateforme ne décide pas que
de l'**artefact**, elle décide aussi de l'**outillage qui tourne dans le
conteneur**. `protoc` est un exécutable natif, invoqué par MSBuild pendant le
`publish`. Le signal était d'ailleurs déjà passé, quelques jours plus tôt, sous
une autre forme : `protoc … No such file or directory` sur la passerelle, qui
était un binaire glibc dans une image musl. Deux fois le même outil, deux fois la
même leçon.

### Si la construction est lente, le levier est dans Docker Desktop

amd64 sur Apple Silicon passe par QEMU, et `dotnet restore` y tient **une heure**
sur « Determining projects to restore… ». Docker Desktop sait faire cette
émulation par Rosetta plutôt que par QEMU :

> Réglages → General → **« Use Rosetta for x86_64/amd64 emulation on Apple
> Silicon »**

On passe de l'heure à quelques minutes, sans toucher au dépôt. C'est le réglage
à vérifier avant de suspecter les Dockerfiles.

À cela s'ajoute un bridage volontaire de la publication — `/m:1`,
`-p:UseSharedCompilation=false`, `--disable-build-servers` — documenté dans
`apps/api-gateway/Dockerfile` : sans lui, quatorze images construites en
parallèle épuisent la mémoire de Docker et BuildKit rend « cannot allocate
memory », une erreur qui ne désigne aucun fichier.

### Ne jamais lancer `--build` sans nommer les services

`docker compose up -d --build` reconstruit **tout ce qui porte une section
`build`** — les treize services .NET *et* `rembg`. Or `rembg` n'a pas une ligne
de code dans ce dépôt : son image ne dépend que de son Dockerfile, et son
`pip install "rembg[cli]"` tient une dizaine de minutes en tirant onnxruntime,
OpenCV, NumPy et SciPy. Le reconstruire après une correction C# est du temps
dépensé pour rien, et c'est le plus lourd des quatorze cibles.

Nommer les services concernés suffit — `depends_on` démarre le reste sans le
reconstruire :

```bash
docker compose -f docker-compose.dev.yml up -d --build \
  payment-service review-service return-refund-service
```

### « error reading from server: EOF » : le démon est mort, pas la compilation

```
target rembg: failed to receive status:
  rpc error: code = Unavailable desc = error reading from server: EOF
```

CE N'EST PAS LE MESSAGE ANNONCÉ PLUS HAUT. L'encadré précédent prépare à lire
« cannot allocate memory » — BuildKit qui constate qu'il ne peut plus allouer et
le dit proprement. Ici, BuildKit n'a rien dit du tout : le client a perdu la
connexion parce que le démon a été tué. C'est le même épuisement mémoire, vu
depuis l'autre bout, et il ne se cherche pas au même endroit — aucune cible n'est
en cause, la dernière nommée dans le message est seulement celle qui tenait le
micro.

Ce qui l'a provoqué : treize compilations .NET **sous émulation amd64** en
parallèle, plus un `pip install` natif de plusieurs centaines de mégaoctets. La
conduite à tenir, dans cet ordre :

1. **Nommer les services** plutôt que tout reconstruire (voir ci-dessus).
2. **Sérialiser** si la liste est longue — une image à la fois, jamais treize :

   ```bash
   for s in payment-service review-service return-refund-service; do
     docker compose -f docker-compose.dev.yml build "$s" || break
   done
   docker compose -f docker-compose.dev.yml up -d
   ```

3. **Vérifier Rosetta** (section précédente) : il remplace QEMU, et divise
   autant l'empreinte mémoire que la durée.
4. **Relever la mémoire de Docker Desktop** — Réglages → Resources. En dessous
   de 8 Gio, une reconstruction large de ce dépôt n'a pas de marge.

### Tenter le natif

Le jour où le `protoc` arm64 de Grpc.Tools sera réparé — ou après une montée de
version du paquet — l'ARG permet de le vérifier sans toucher aux 23 fichiers :

```bash
docker build --build-arg DOTNET_BUILD_PLATFORM=linux/arm64 \
  -f services/common/identity-service/Dockerfile -t hba/identity-service .
```

Le test le plus court, avant même de reconstruire une image :

```bash
docker run --rm --platform linux/arm64 \
  -v ~/.nuget/packages/grpc.tools/2.71.0/tools/linux_arm64:/t:ro \
  mcr.microsoft.com/dotnet/sdk:9.0 sh -c 'getconf PAGESIZE; /t/protoc --version; echo sortie=$?'
```

Un `PAGESIZE` de 16384 en face d'un binaire aligné sur 4096 explique le SIGSEGV
sans rien deviner ; `protoc --version` qui répond normalement innocente le
binaire et déplace la recherche ailleurs.

`apps/api-gateway/Dockerfile` n'a jamais porté ce pinning : il construit en
natif, et c'est pourquoi il est le seul à être allé vite — jusqu'à ce que
`protoc` l'arrête, lui aussi.
