# ═════════════════════════════════════════════════════════════════════════════
# promotion-service — image de production.
#
# LE CONTEXTE DE CONSTRUCTION EST LA RACINE DU DEPOT, PAS LE DOSSIER DU SERVICE.
#
# Le service reference `shared/common/*`, qui vit HORS de son dossier. Un
# contexte limite au service rendrait ces projets invisibles a `dotnet restore`.
#
#   docker build -f services/common/promotion-service/Dockerfile -t hba/promotion-service .
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

COPY Directory.Build.props Directory.Packages.props ./
COPY shared/ shared/

# LES CONTRATS DES DEUX SERVICES DE COMMANDE, PARCE QU'ON ECOUTE LEURS FAITS.
#
# Le §10.16 fait consommer `marketplace.order.cancelled` et
# `food.order.cancelled` : `HBA.Promotions.Api` reference donc les deux projets
# de contrats, qui vivent chez order-service et restaurant-service.
#
# L'oubli ne se voit pas sur le poste, ou la solution voit tout le depot :
# `make build` passe, et c'est `docker build` qui tombe sur un avertissement
# MSB9008 suivi d'une volee de CS0234.
COPY services/marketplace/order-service/src/HBA.Order.Contracts services/marketplace/order-service/src/HBA.Order.Contracts
COPY services/food/restaurant-service/src/HBA.Food.Restaurant.Contracts services/food/restaurant-service/src/HBA.Food.Restaurant.Contracts

# ET LES CONTRATS VENDEUR, POUR LA GARDE D'APPARTENANCE (lot D28).
#
# `/api/v1/merchant/promotions` est ouverte au vendeur PROPRIETAIRE :
# `HBA.Promotions.Api` reference `HBA.Merchants.Contracts.Grpc` (dans `shared/`,
# deja copie plus haut), qui reference a son tour `HBA.Merchants.Contracts` —
# lequel vit chez seller-service, hors de `shared/`.
#
# Une reference TRANSITIVE, donc, et c'est ce qui la rend facile a oublier :
# rien dans le `.csproj` de ce service ne la nomme. `make build` passe sur le
# poste, ou la solution voit tout le depot ; c'est `docker build` qui tombe sur
# MSB9008 puis une volee de CS0234.
COPY services/marketplace/seller-service/src/HBA.Merchants.Contracts services/marketplace/seller-service/src/HBA.Merchants.Contracts

COPY services/common/promotion-service/src/ services/common/promotion-service/src/

RUN dotnet restore services/common/promotion-service/src/HBA.Promotions.Api/HBA.Promotions.Api.csproj

RUN dotnet publish services/common/promotion-service/src/HBA.Promotions.Api/HBA.Promotions.Api.csproj \
        --configuration Release --no-restore --disable-build-servers -p:UseSharedCompilation=false /m:1 --output /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV DOTNET_EnableDiagnostics=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "HBA.Promotions.Api.dll"]
