# communication-service — image de production.
#
# Le contexte de construction est `HBA/`, pas le dossier du service : le service
# référence `shared/common/*`, qui vit hors de son dossier.
#
#   docker build -f services/common/notification-service/Dockerfile -t hba/communication-service .

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

# CINQ CONTRATS D'AUTRES SERVICES — REQUIS PAR LA TRANCHE NOTIFICATIONS.
#
# Ce module réagit à ce qui se passe partout : commande payée, vendeur suspendu,
# restaurant validé, stock épuisé, reversement effectué. Il référence donc les
# `*.Contracts` — les enregistrements d'événements d'intégration — de cinq
# autres services. Rien d'autre : ni Domain, ni Application, ni Infrastructure.
#
# Sans ces cinq lignes, la restauration réussit et la COMPILATION échoue sur un
# message de namespace introuvable, comme pour commerce-service.
COPY services/marketplace/seller-service/src/HBA.Merchants.Contracts ./services/marketplace/seller-service/src/HBA.Merchants.Contracts

# TIRÉ PAR `HBA.Catalog.Contracts.Grpc`, PAS PAR LE MODULE LUI-MÊME.
#
# Le client gRPC référencé par le projet Api dépend à son tour des contrats de
# catalog-service. C'est le genre de dépendance qu'on n'écrit jamais et qui
# casse quand même la compilation — d'où la vérification de la fermeture
# TRANSITIVE, et non des seules références directes.
COPY services/marketplace/catalog-service/src/HBA.Catalog.Contracts ./services/marketplace/catalog-service/src/HBA.Catalog.Contracts
COPY services/food/restaurant-service/src/HBA.Food.Restaurant.Contracts ./services/food/restaurant-service/src/HBA.Food.Restaurant.Contracts
# Les COMMANDES de repas : `AcheteurDuTicket` y resout l acheteur d un ticket
# de cuisine ne du parcours food (lot 6.4).
COPY services/food/food-order-service/src/HBA.Food.Order.Contracts ./services/food/food-order-service/src/HBA.Food.Order.Contracts
COPY services/marketplace/inventory-service/src/HBA.Inventory.Contracts ./services/marketplace/inventory-service/src/HBA.Inventory.Contracts
COPY services/common/review-service/src/HBA.Engagement.Reviews.Contracts ./services/common/review-service/src/HBA.Engagement.Reviews.Contracts
COPY services/common/wallet-service/src/HBA.Financial.Wallet.Contracts ./services/common/wallet-service/src/HBA.Financial.Wallet.Contracts

# Même mécanisme, ajouté avec le client gRPC d'Ordering : quatre gestionnaires
# de notification remontent d'une commande à son acheteur pour savoir à qui
# écrire. `HBA.Order.Contracts.Grpc` vit dans `shared`, mais sa propre
# dépendance est ici.
COPY services/marketplace/order-service/src/HBA.Order.Contracts ./services/marketplace/order-service/src/HBA.Order.Contracts

# CONTRAT DES ÉVÉNEMENTS DE PAIEMENT — chez leur propriétaire.
#
# Le duplicata partagé (`HBA.Payments.Contracts`) a été supprimé : deux
# déclarations d'un même fait faisaient résoudre au consommateur Kafka un type
# au hasard de l'ordre de chargement des assemblies.
COPY services/common/payment-service/src/HBA.Financial.Payments.Contracts ./services/common/payment-service/src/HBA.Financial.Payments.Contracts

# Le livreur doit être prévenu de la course qu'on lui propose : contrat de
# delivery-service, tiré par son client gRPC.
COPY services/delivery/delivery-service/src/HBA.Deliveries.Contracts ./services/delivery/delivery-service/src/HBA.Deliveries.Contracts

COPY services/common/notification-service/src/ services/common/notification-service/src/

RUN dotnet restore services/common/notification-service/src/HBA.Communication.Api/HBA.Communication.Api.csproj

RUN dotnet publish services/common/notification-service/src/HBA.Communication.Api/HBA.Communication.Api.csproj \
        --configuration Release --no-restore --disable-build-servers -p:UseSharedCompilation=false /m:1 --output /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

COPY --from=build /app .

USER $APP_UID

ENV DOTNET_EnableDiagnostics=0

EXPOSE 8080

ENTRYPOINT ["dotnet", "HBA.Communication.Api.dll"]
