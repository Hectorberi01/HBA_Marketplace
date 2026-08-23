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
COPY services/marketplace/inventory-service/src/HBA.Inventory.Contracts ./services/marketplace/inventory-service/src/HBA.Inventory.Contracts
COPY services/marketplace/cart-service/src/HBA.Commerce.Contracts ./services/marketplace/cart-service/src/HBA.Commerce.Contracts

# La clôture de commande écoute DeliveryCompletedIntegrationEvent : sans ce
# projet, `dotnet restore` échoue sur une référence introuvable — et l'erreur
# parle d'un chemin, pas de la fonctionnalité qui en dépend.
COPY services/delivery/delivery-service/src/HBA.Deliveries.Contracts ./services/delivery/delivery-service/src/HBA.Deliveries.Contracts

# CONTRAT DES ÉVÉNEMENTS DE PAIEMENT — chez leur propriétaire.
#
# Le duplicata partagé (`HBA.Payments.Contracts`) a été supprimé : deux
# déclarations d'un même fait faisaient résoudre au consommateur Kafka un type
# au hasard de l'ordre de chargement des assemblies.
COPY services/common/payment-service/src/HBA.Financial.Payments.Contracts ./services/common/payment-service/src/HBA.Financial.Payments.Contracts

# Le refus du restaurant annule la commande, et la remise du repas la CLÔT :
# contrat de food-service. Sans `FoodOrderDelivered`, une commande de repas
# restait « confirmée » à vie et le restaurateur n'était jamais payé.
COPY services/food/restaurant-service/src/HBA.Food.Restaurant.Contracts ./services/food/restaurant-service/src/HBA.Food.Restaurant.Contracts

# COPIÉ POUR UNE AUTORISATION, ET L'OUBLI NE SE VOIT QU'ICI.
#
# `GET /api/sellers/{sellerId}/orders` doit prouver que l'appelant EST ce
# vendeur ; seul merchant-service connaît la correspondance utilisateur →
# vendeur. order-service référence donc `HBA.Merchants.Contracts.Grpc`, qui
# référence à son tour `HBA.Merchants.Contracts` — et ce projet-là vit hors de
# `shared`, donc hors de la copie large de la ligne 6.
#
# CE DÉFAUT EST INVISIBLE À `dotnet build` SUR LE POSTE : la solution voit
# tous les projets. Il n'apparaît qu'à la construction de l'image, où le
# contexte est réduit — et il commence par un simple AVERTISSEMENT (MSB9008,
# « the referenced project does not exist ») avant de se transformer en une
# volée de CS0234 sur des types « qui n'existent pas dans l'espace de noms ».
# Le message ne nomme jamais le COPY manquant.
COPY services/marketplace/seller-service/src/HBA.Merchants.Contracts ./services/marketplace/seller-service/src/HBA.Merchants.Contracts

COPY services/marketplace/order-service ./services/marketplace/order-service
# Le contrat du catalogue : le checkout relit prix, statut publie et achetabilite
# de chaque offre (lot 7.4, ISSUE-048).
COPY services/marketplace/catalog-service/src/HBA.Catalog.Contracts ./services/marketplace/catalog-service/src/HBA.Catalog.Contracts

RUN dotnet restore services/marketplace/order-service/src/HBA.Order.Api/HBA.Order.Api.csproj
RUN dotnet publish services/marketplace/order-service/src/HBA.Order.Api/HBA.Order.Api.csproj -c Release --disable-build-servers -p:UseSharedCompilation=false /m:1 -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
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

ENTRYPOINT ["dotnet", "HBA.Order.Api.dll"]
