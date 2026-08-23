# ═════════════════════════════════════════════════════════════════════════════
# return-refund-service — image de production.
#
# LE CONTEXTE DE CONSTRUCTION EST LA RACINE DU DEPOT, PAS LE DOSSIER DU SERVICE.
#
# Le service reference `shared/common/*`, qui vit HORS de son dossier. Un
# contexte limite au service rendrait ces projets invisibles a `dotnet restore`.
#
#   docker build -f services/marketplace/return-refund-service/Dockerfile -t hba/return-refund-service .
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
COPY services/marketplace/order-service/src/HBA.Order.Contracts/ services/marketplace/order-service/src/HBA.Order.Contracts/

# ENTRAÎNÉ PAR `HBA.Merchants.Contracts.Grpc`, QUI EST DANS `shared/`.
#
# Ce service a acquis le client gRPC des marchands en vague 1 (garde
# d'appartenance sur les routes vendeur). Le projet de transport est sous
# `shared/contracts/`, donc déjà copié — mais il référence
# `HBA.Merchants.Contracts`, qui vit dans l'arborescence de seller-service et ne
# l'était pas. `dotnet restore` échouait sur un csproj introuvable, uniquement
# dans l'image : la compilation locale voit tout le dépôt.
COPY services/marketplace/seller-service/src/HBA.Merchants.Contracts/ services/marketplace/seller-service/src/HBA.Merchants.Contracts/
COPY services/marketplace/return-refund-service/src/ services/marketplace/return-refund-service/src/
COPY services/marketplace/return-refund-service/contracts/ services/marketplace/return-refund-service/contracts/

RUN dotnet restore services/marketplace/return-refund-service/src/HBA.Marketplace.ReturnRefund.Api/HBA.Marketplace.ReturnRefund.Api.csproj

RUN dotnet publish services/marketplace/return-refund-service/src/HBA.Marketplace.ReturnRefund.Api/HBA.Marketplace.ReturnRefund.Api.csproj \
        --configuration Release --no-restore --disable-build-servers -p:UseSharedCompilation=false /m:1 --output /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV DOTNET_EnableDiagnostics=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "HBA.Marketplace.ReturnRefund.Api.dll"]
