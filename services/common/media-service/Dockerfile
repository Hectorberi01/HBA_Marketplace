# ═════════════════════════════════════════════════════════════════════════════
# media-service — image de production.
#
# LE CONTEXTE DE CONSTRUCTION EST `HBA/`, PAS LE DOSSIER DU SERVICE.
#
# Le service référence `shared/common/*` et `shared/contracts/*`, qui
# vivent HORS de son dossier. Un contexte limité au service rendrait ces projets
# invisibles à `dotnet restore` — l'erreur porterait alors sur un fichier
# introuvable, sans dire que le contexte est trop étroit.
#
#   docker build -f services/common/media-service/Dockerfile -t hba/media-service .
# ═════════════════════════════════════════════════════════════════════════════

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
WORKDIR /source

# Fichiers de projet seuls d'abord : la restauration n'est refaite que si une
# dépendance change, pas à chaque modification de C#.
COPY Directory.Build.props Directory.Packages.props ./
COPY shared/ shared/
# Le retrait d'une pièce KYB fait disparaître son fichier : media consomme
# l'événement de merchant-service, donc en référence les contrats.
COPY services/marketplace/seller-service/src/HBA.Merchants.Contracts ./services/marketplace/seller-service/src/HBA.Merchants.Contracts

COPY services/common/media-service/src/ services/common/media-service/src/

RUN dotnet restore services/common/media-service/src/HBA.Media.Api/HBA.Media.Api.csproj

RUN dotnet publish services/common/media-service/src/HBA.Media.Api/HBA.Media.Api.csproj \
        --configuration Release --no-restore --disable-build-servers -p:UseSharedCompilation=false /m:1 --output /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

COPY --from=build /app .

# Utilisateur non privilégié fourni par l'image .NET. En root, une exécution de
# code arbitraire dans le processus devient une exécution root dans le conteneur.
# Corollaire : impossible d'ouvrir un port < 1024, d'où le 8080.
USER $APP_UID

ENV DOTNET_EnableDiagnostics=0

EXPOSE 8080

ENTRYPOINT ["dotnet", "HBA.Media.Api.dll"]
