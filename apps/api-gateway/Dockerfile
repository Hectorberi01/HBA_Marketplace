# ═════════════════════════════════════════════════════════════════════════════
# HBA Gateway — image de production.
#
# LE CONTEXTE EST `HBA/`, PAS LE DOSSIER `bff/`.
#
#   docker build -f apps/api-gateway/Dockerfile -t hba/gateway .
#
# Ce fichier attendait `bff/` comme contexte, et la restauration échouait sur
# « The TargetFramework value '' was not recognized » — un message qui ne
# désigne ni le fichier manquant ni la raison.
#
# `HBA.Gateway.Api.csproj` ne déclare NI cadre cible NI version de paquet : les
# deux viennent de `Directory.Build.props` et `Directory.Packages.props`, posés à
# la racine `HBA/`. MSBuild les cherche en remontant les dossiers depuis le
# projet ; hors du contexte, ils n'existent pas dans l'image, et chaque propriété
# qu'ils fournissent devient vide.
#
# Les treize services suivent déjà cette convention (cf. services/*/Dockerfile).
# ═════════════════════════════════════════════════════════════════════════════

# ═════════════════════════════════════════════════════════════════════════════
# ÉTAPE DE CONSTRUCTION SUR L'IMAGE glibc, ET NON SUR ALPINE.
#
# CE FICHIER ÉTAIT LE SEUL DES VINGT-QUATRE À BÂTIR SUR ALPINE, ET C'EST CE QUI
# LE CASSAIT.
#
# `HBA.Gateway.Api` référence `HBA.Identity.Contracts.Grpc`, dont la compilation
# lance `protoc` fourni par le paquet `Grpc.Tools`. Ces exécutables sont liés à
# la GLIBC ; Alpine utilise la MUSL. Le chargeur ELF attendu
# (`/lib64/ld-linux-*.so`) n'existe donc pas dans l'image, et `execve` échoue —
# en rendant `ENOENT`, c'est-à-dire :
#
#     MSB6003: The specified task executable
#     ".../grpc.tools/2.71.0/tools/linux_arm64/protoc" could not be run.
#     ... No such file or directory
#
# Le message désigne un fichier QUI EXISTE. C'est l'interpréteur qui manque, pas
# le binaire — d'où une heure passée à vérifier le chemin.
#
# POURQUOI PAS `apk add protobuf` PLUTÔT.
#
# Parce que `protoc` n'est que la moitié du travail : la génération C# passe
# aussi par `grpc_csharp_plugin`, que le paquet `Grpc.Tools` fournit dans la même
# forme glibc et dont Alpine n'a AUCUN équivalent. On installerait donc un demi
# outillage pour un échec déplacé d'un cran.
#
# L'ÉTAPE FINALE, ELLE, RESTE SUR ALPINE.
#
# La publication est portable — ni `-r` ni auto-contenue — et le point d'entrée
# est `dotnet HBA.Gateway.Api.dll`, qui ignore l'hôte natif engendré par le SDK.
# L'image de production garde donc sa taille, et son `wget` de busybox dont
# dépend le healthcheck de `compose.gateway.yml`.
# ═════════════════════════════════════════════════════════════════════════════
# ═════════════════════════════════════════════════════════════════════════════
# ET L'ÉTAGE DE COMPILATION EST EN amd64, POUR LA MÊME RAISON.
#
# Passer d'Alpine à Debian a réglé la moitié glibc/musl du problème `protoc` ; il
# reste la moitié architecture. Le binaire `linux_arm64` livré par Grpc.Tools
# 2.71.0 plante — `exited with code 139`, donc SIGSEGV — sur les projets
# `*.Contracts.Grpc`, que cette image compile comme les autres.
#
# Cette image était la SEULE des vingt-quatre à ne pas épingler sa plateforme,
# et c'est pourquoi elle était la seule à construire vite. Elle serait aussi la
# première à retomber sur `protoc` en natif.
#
# Si la construction est lente, le levier est dans Docker Desktop :
#   Réglages → General → « Use Rosetta for x86_64/amd64 emulation »
#
# Voir infra/docker/README.md.
# ═════════════════════════════════════════════════════════════════════════════
ARG DOTNET_BUILD_PLATFORM=linux/amd64
FROM --platform=$DOTNET_BUILD_PLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source

# ── Couche 1 : la restauration, isolée des sources ──────────────────────────
#
# COPIER LES .csproj SEULS AVANT LE RESTE N'EST PAS UNE COQUETTERIE.
#
# Docker invalide une couche dès que l'un de ses fichiers change. Copier tout le
# dépôt d'un coup ferait retélécharger l'intégralité des paquets NuGet à CHAQUE
# modification d'une ligne de C#. En isolant les fichiers projet, la restauration
# n'est refaite que si une dépendance a réellement changé.
#
# Les deux `Directory.*.props` font partie de cette couche : ce sont eux qui
# portent le cadre cible et les versions, donc les changer DOIT invalider la
# restauration.
COPY Directory.Build.props Directory.Packages.props ./

ARG BFF=apps/api-gateway

# `shared/` EST DEVENU NÉCESSAIRE, ET SON ABSENCE NE SE SERAIT VUE QU'EN CI.
#
# La passerelle a longtemps été le seul projet du dépôt à ne dépendre d'aucun
# projet partagé : trois csproj, aucune référence hors de `apps/api-gateway`. Le
# contrôle de révocation (ISSUE-022) lui donne sa première — `HBA.Identity.Contracts.Grpc`,
# qui vit sous `shared/contracts/` et entraîne `HBA.Shared.Hosting` derrière lui.
#
# Sans cette copie, `dotnet restore` échoue sur un csproj introuvable. Rien ne
# l'aurait signalé avant : `dotnet build` local voit tout le dépôt, et
# le contrôle `dockerfiles` ne parcourt que `services/*`.
#
# Le dossier entier plutôt que les trois csproj concernés : c'est la convention
# des treize services (cf. `services/*/Dockerfile`), et `Grpc.Tools` a de toute
# façon besoin de `shared/proto/identity/v1/identity.proto` à la compilation.
COPY shared/ shared/

COPY ${BFF}/src/HBA.Gateway.Api/HBA.Gateway.Api.csproj                       ${BFF}/src/HBA.Gateway.Api/
COPY ${BFF}/src/HBA.Gateway.Application/HBA.Gateway.Application.csproj       ${BFF}/src/HBA.Gateway.Application/
COPY ${BFF}/src/HBA.Gateway.Infrastructure/HBA.Gateway.Infrastructure.csproj ${BFF}/src/HBA.Gateway.Infrastructure/

RUN dotnet restore ${BFF}/src/HBA.Gateway.Api/HBA.Gateway.Api.csproj

# ── Couche 2 : compilation ──────────────────────────────────────────────────
COPY ${BFF}/src/ ${BFF}/src/

# `--no-restore` : la restauration a eu lieu ci-dessus. Sans ce drapeau, elle
# serait refaite ici et la couche précédente n'aurait servi à rien.
# LES TROIS BRIDES DES TREIZE SERVICES, QUI MANQUAIENT ICI.
#
# Sans elles, MSBuild lance un processus de compilation par cœur et laisse
# tourner un serveur Roslyn résident. Quatorze images construites en parallèle,
# chacune avec sa dizaine de processus, épuisent la mémoire allouée à Docker :
# le noyau tue le processus et BuildKit rend « cannot allocate memory » — une
# erreur qui ne désigne aucun fichier et fait chercher un problème de code.
RUN dotnet publish ${BFF}/src/HBA.Gateway.Api/HBA.Gateway.Api.csproj \
        --configuration Release \
        --no-restore \
        --disable-build-servers \
        -p:UseSharedCompilation=false \
        /m:1 \
        --output /app

# ═════════════════════════════════════════════════════════════════════════════
# Image finale : runtime ASP.NET seul, sans SDK ni sources.
# ═════════════════════════════════════════════════════════════════════════════
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
WORKDIR /app

# `wget` sert au healthcheck de compose.gateway.yml. Il est présent dans
# l'image Alpine de base ; la ligne suivante n'installe donc rien mais documente
# la dépendance — la retirer casserait le healthcheck sans autre symptôme qu'un
# conteneur perpétuellement « unhealthy ».
# (aucune installation requise : busybox fournit wget)

COPY --from=build /app .

# Certains fichiers de configuration peuvent arriver avec un mode 600 depuis
# l'hôte ou la couche build. Le gateway s'exécute en utilisateur non-root, il
# doit pouvoir lire sa configuration au démarrage.
RUN chmod a+r /app/appsettings*.json

# ═════════════════════════════════════════════════════════════════════════════
# EXÉCUTION SANS PRIVILÈGES.
#
# Les images .NET 8+ fournissent un utilisateur `app` (UID 1654) prêt à l'emploi.
# En root, une exécution de code arbitraire dans le processus devient une
# exécution root DANS le conteneur — et, en cas d'évasion, sur l'hôte.
#
# Conséquence à connaître : un utilisateur non privilégié ne peut pas ouvrir un
# port inférieur à 1024. C'est pour cela que le service écoute sur 8080 et non
# sur 80, ici comme dans compose.gateway.yml.
# ═════════════════════════════════════════════════════════════════════════════
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

ENTRYPOINT ["dotnet", "HBA.Gateway.Api.dll"]
