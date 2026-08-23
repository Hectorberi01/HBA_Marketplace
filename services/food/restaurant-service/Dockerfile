# food-service — image de production.
#
# Le contexte de construction est `HBA/`, pas le dossier du service.

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
# TIRÉS PAR LES CLIENTS gRPC, PAS PAR LE MODULE FOOD LUI-MÊME.
#
# `COPY shared` embarque les clients Ordering, Inventory et Delivery, mais
# chacun référence les contrats du service qu'il appelle — et ceux-ci vivent
# DANS ces services. La restauration réussit sans eux ; c'est la compilation qui
# tombe, sur un message de namespace introuvable qui ne désigne pas la cause.
COPY services/marketplace/order-service/src/HBA.Order.Contracts ./services/marketplace/order-service/src/HBA.Order.Contracts
COPY services/marketplace/inventory-service/src/HBA.Inventory.Contracts ./services/marketplace/inventory-service/src/HBA.Inventory.Contracts
COPY services/delivery/delivery-service/src/HBA.Deliveries.Contracts ./services/delivery/delivery-service/src/HBA.Deliveries.Contracts
# Idem pour le client Merchants, ajouté avec la route de rattachement du dossier
# de reversement : `HBA.Merchants.Contracts.Grpc` vit dans shared, mais le
# contrat qu'il implémente (`ISellerModuleApi`) vit dans merchant-service.
COPY services/marketplace/seller-service/src/HBA.Merchants.Contracts ./services/marketplace/seller-service/src/HBA.Merchants.Contracts

# LE CONTRAT DE LA COMMANDE DE REPAS, TIRÉ PAR LE PONT QUI OUVRE LE TICKET.
#
# `ReceiveFoodOrderOnMealOrderConfirmedHandler` écoute `MealOrderConfirmed`, dont
# le contrat vit chez son publieur, food-order-service. Même piège que les quatre
# lignes ci-dessus : la restauration réussit sans lui, et c'est la compilation qui
# tombe sur un CS0234 de namespace qui ne désigne pas la cause.
COPY services/food/food-order-service/src/HBA.Food.Order.Contracts ./services/food/food-order-service/src/HBA.Food.Order.Contracts

COPY services/food/restaurant-service/src/ services/food/restaurant-service/src/

RUN dotnet restore services/food/restaurant-service/src/HBA.Food.Restaurant.Api/HBA.Food.Restaurant.Api.csproj

RUN dotnet publish services/food/restaurant-service/src/HBA.Food.Restaurant.Api/HBA.Food.Restaurant.Api.csproj \
        --configuration Release --no-restore --disable-build-servers -p:UseSharedCompilation=false /m:1 --output /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

COPY --from=build /app .

USER $APP_UID

ENV DOTNET_EnableDiagnostics=0

EXPOSE 8080

ENTRYPOINT ["dotnet", "HBA.Food.Restaurant.Api.dll"]
