# syntax=docker/dockerfile:1

# ═════════════════════════════════════════════════════════════════════════════
# L'ÉTAGE DE COMPILATION EST EN amd64, ET C'EST protoc QUI L'IMPOSE.
#
# Ce service compile lui-même `merchant.proto` (client). Le binaire protoc arm64
# livré par Grpc.Tools plante en SIGSEGV :
#
#   error MSB6006: ".../grpc.tools/2.71.0/tools/linux_arm64/protoc"
#                  exited with code 139
#
# L'ARTEFACT, lui, ne dépend pas de l'architecture de compilation : aucun projet
# ne fixe de `RuntimeIdentifier`, la publication est portable, et la CI construit
# sur `ubuntu-latest`. Ce qui dépend de la plateforme, c'est l'OUTILLAGE qui
# tourne DANS le conteneur.
#
# SI LA CONSTRUCTION EST LENTE SUR UN MAC APPLE SILICON, le levier n'est pas ici :
#   Réglages Docker Desktop → General → « Use Rosetta for x86_64/amd64 emulation »
#
# POUR TENTER LE NATIF LE JOUR OÙ protoc arm64 SERA RÉPARÉ :
#   docker build --build-arg DOTNET_BUILD_PLATFORM=linux/arm64 ...
#
# Voir infra/docker/README.md et le Dockerfile de cart-service, qui porte
# l'argumentaire complet.
# ═════════════════════════════════════════════════════════════════════════════
ARG DOTNET_BUILD_PLATFORM=linux/amd64
FROM --platform=$DOTNET_BUILD_PLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY shared ./shared

# LES QUATRE CONTRATS QUE CE SERVICE CONSOMME NE SONT PAS DANS `shared/`.
#
# Ils ont rejoint leur propriétaire, et `COPY shared` ne les embarque plus. Sans
# ces lignes, `dotnet restore` échoue A L'INTERIEUR DE L'IMAGE, sur un message qui
# nomme un chemin et pas la fonctionnalité qui en dépend — le pire endroit pour
# découvrir un déplacement de fichiers.
#
# Une source par ligne `COPY` : le contrôle `dockerfiles` ne lit qu'une source par
# instruction, et une ligne à deux sources lui ferait rater la seconde.
COPY services/marketplace/order-service/src/HBA.Order.Contracts ./services/marketplace/order-service/src/HBA.Order.Contracts
COPY services/marketplace/seller-service/src/HBA.Merchants.Contracts ./services/marketplace/seller-service/src/HBA.Merchants.Contracts
COPY services/common/identity-service/src/HBA.Identity.Contracts ./services/common/identity-service/src/HBA.Identity.Contracts
COPY services/common/payment-service/src/HBA.Financial.Payments.Contracts ./services/common/payment-service/src/HBA.Financial.Payments.Contracts

COPY services/common/analytics-service ./services/common/analytics-service

RUN dotnet restore services/common/analytics-service/src/HBA.Analytics.Api/HBA.Analytics.Api.csproj
RUN dotnet publish services/common/analytics-service/src/HBA.Analytics.Api/HBA.Analytics.Api.csproj -c Release --disable-build-servers -p:UseSharedCompilation=false /m:1 -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# ═════════════════════════════════════════════════════════════════════════════
# NON-ROOT DÈS LE PREMIER JOUR.
#
# Huit des trente Dockerfiles du dépôt tournaient en root, et rien ne le
# signalait : une image root démarre, sert, et passe tous les tests. Le §19 du
# cahier Infrastructure exige des pods non-root, et le §7 un `SecurityContext` qui
# le fasse respecter — posé sur une image root, il ferait planter le pod au
# premier déploiement.
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

ENTRYPOINT ["dotnet", "HBA.Analytics.Api.dll"]
