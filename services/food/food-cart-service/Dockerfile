# syntax=docker/dockerfile:1
# ═════════════════════════════════════════════════════════════════════════════
# food-cart-service — image de production.
#
# LE CONTEXTE DE CONSTRUCTION EST LA RACINE DU DÉPÔT, PAS LE DOSSIER DU SERVICE.
#
# Le service référence `shared/common/*` et deux projets de contrats voisins,
# qui vivent HORS de son dossier. Un contexte limité au service rendrait ces
# projets invisibles à `dotnet restore`.
#
#   docker build -f services/food/food-cart-service/Dockerfile -t hba/food-cart-service .
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
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY shared ./shared

# LA CARTE DU RESTAURANT — référencée par l'Application ET par le client gRPC
# de Food, que `COPY shared` embarque SANS sa dépendance.
#
# L'oubli ne se voit pas à la restauration, qui réussit. C'est la COMPILATION qui
# tombe, sur un message trompeur : MSB9008 « the referenced project does not
# exist » n'est qu'un avertissement, suivi d'un CS0234 sur un namespace qui
# compile parfaitement ailleurs.
COPY services/food/restaurant-service/src/HBA.Food.Restaurant.Contracts ./services/food/restaurant-service/src/HBA.Food.Restaurant.Contracts

# Le contrat de la commande de repas : événement écouté, et client gRPC lu.
COPY services/food/food-order-service/src/HBA.Food.Order.Contracts ./services/food/food-order-service/src/HBA.Food.Order.Contracts

COPY services/food/food-cart-service ./services/food/food-cart-service

RUN dotnet restore services/food/food-cart-service/src/HBA.Food.Cart.Api/HBA.Food.Cart.Api.csproj
RUN dotnet publish services/food/food-cart-service/src/HBA.Food.Cart.Api/HBA.Food.Cart.Api.csproj -c Release --disable-build-servers -p:UseSharedCompilation=false /m:1 -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# En root, une exécution de code arbitraire dans le processus devient une
# exécution root dans le conteneur. Corollaire : aucun port < 1024 — les deux
# ports du service, 8080 (REST) et 9090 (gRPC), sont au-dessus.
USER $APP_UID

ENV DOTNET_EnableDiagnostics=0

EXPOSE 8080

ENTRYPOINT ["dotnet", "HBA.Food.Cart.Api.dll"]
