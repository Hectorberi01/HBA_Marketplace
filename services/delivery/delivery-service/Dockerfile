# syntax=docker/dockerfile:1

# ═════════════════════════════════════════════════════════════════════════════
# L'ÉTAGE DE COMPILATION EST EN amd64, ET C'EST protoc QUI L'IMPOSE.
#
# Cette ligne n'a longtemps porté aucune explication, et elle a été mise à
# `$BUILDPLATFORM` — donc en arm64 natif sur un Mac Apple Silicon — au motif que
# la sortie n'en dépend pas. C'est vrai de la SORTIE et faux de la CONSTRUCTION :
#
#   error MSB6006: ".../grpc.tools/2.71.0/tools/linux_arm64/protoc"
#                  exited with code 139
#
# 139 = 128 + SIGSEGV. Les 22 projets `*.Contracts.Grpc` de `shared/contracts/`
# sont copiés dans TOUTES les images : le binaire protoc arm64 livré par
# Grpc.Tools plante, et aucune image ne se construit.
#
# CE QUI RESTE VRAI DE L'ANALYSE PRÉCÉDENTE, ET CE QUI NE L'ÉTAIT PAS.
#
# Vrai : aucun projet ne fixe de `RuntimeIdentifier`, la publication est
# portable, et la CI construit déjà sur `ubuntu-latest`. L'ARTEFACT ne dépend pas
# de l'architecture de compilation.
# Faux : en conclure que la plateforme est indifférente. Elle décide aussi de
# l'OUTILLAGE qui tourne DANS le conteneur — protoc ici, et c'est un exécutable
# natif. Le signal était déjà passé : l'erreur `protoc … No such file or
# directory` de la passerelle, qui était un binaire glibc dans une image musl.
#
# SI LA CONSTRUCTION EST LENTE, LE LEVIER N'EST PAS ICI.
#
# amd64 sur Apple Silicon passe par QEMU, et `dotnet restore` y tient une heure
# sur « Determining projects to restore… ». Docker Desktop sait faire cette
# émulation par Rosetta plutôt que par QEMU :
#   Réglages → General → « Use Rosetta for x86_64/amd64 emulation »
# On passe alors de l'heure à quelques minutes, sans rien changer ici.
#
# POUR TENTER LE NATIF LE JOUR OÙ protoc arm64 SERA RÉPARÉ :
#   docker build --build-arg DOTNET_BUILD_PLATFORM=linux/arm64 ...
#
# Voir infra/docker/README.md.
# ═════════════════════════════════════════════════════════════════════════════
ARG DOTNET_BUILD_PLATFORM=linux/amd64
FROM --platform=$DOTNET_BUILD_PLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY shared ./shared
# ═════════════════════════════════════════════════════════════════════════════
# CETTE IMAGE EMBARQUAIT LE DOMAINE DE DEUX AUTRES SERVICES.
#
# Deux `COPY` faisaient entrer ici `driver-service/.../HBA.Delivery.Driver.Domain`
# et `dispatch-service/.../HBA.Delivery.Dispatch.Domain` — non par choix, mais
# parce que le `.csproj` les référençait. L'image de livraison contenait donc le
# code de trois services, et une modification du domaine livreur reconstruisait
# celle-ci.
#
# Les références de projet sont tombées avec le lot 5.4 (ISSUE-069) : les quelques
# fichiers concernés étaient du code de delivery-service rangé chez le voisin, et
# ils sont revenus dans `HBA.Delivery.Core.Domain`. Ces deux lignes n'ont donc
# plus rien à copier.
#
# NE PAS LES RÉTABLIR pour faire passer une compilation : si un `COPY` d'un
# autre service redevient nécessaire ici, c'est qu'une `ProjectReference` de
# domaine est réapparue — c'est elle qu'il faut retirer.
# ═════════════════════════════════════════════════════════════════════════════
COPY services/delivery/delivery-service ./services/delivery/delivery-service

RUN dotnet restore services/delivery/delivery-service/src/HBA.Delivery.Core.Api/HBA.Delivery.Core.Api.csproj
RUN dotnet publish services/delivery/delivery-service/src/HBA.Delivery.Core.Api/HBA.Delivery.Core.Api.csproj -c Release --disable-build-servers -p:UseSharedCompilation=false /m:1 -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# ═════════════════════════════════════════════════════════════════════════════
# CETTE IMAGE TOURNAIT EN ROOT.
#
# Vingt-deux des trente Dockerfiles du dépôt posaient déjà `USER $APP_UID` ;
# celui-ci faisait partie des huit qui ne le faisaient pas. Rien ne le signalait :
# une image root démarre, sert, et passe tous les tests.
#
# Le §19 du cahier Infrastructure exige des pods non-root, et le §7 un
# `SecurityContext` qui le fait respecter. Poser ce SecurityContext sur une image
# root ferait planter le pod au premier déploiement — d'où cette correction AVANT
# les manifests, et non avec eux.
#
# Ce qui est vraiment en jeu : en root, une exécution de code arbitraire dans le
# processus devient une exécution root dans le conteneur.
#
# Corollaire : impossible d'ouvrir un port < 1024. Les deux ports du service,
# 8080 (REST) et 9090 (gRPC), sont au-dessus.
# ═════════════════════════════════════════════════════════════════════════════
USER $APP_UID

ENV DOTNET_EnableDiagnostics=0

EXPOSE 8080

ENTRYPOINT ["dotnet", "HBA.Delivery.Core.Api.dll"]
