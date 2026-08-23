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
COPY services/marketplace/order-service/src/HBA.Order.Contracts ./services/marketplace/order-service/src/HBA.Order.Contracts

# /!\ AJOUTE AVEC LA GARDE DE CAPACITE SUR LA REPONSE AUX AVIS (lot D2).
#
# `ReplyToReviewCommand` ne demande plus « ce compte a-t-il un dossier vendeur »
# mais « ce compte a-t-il REVIEW_REPLY sur le vendeur de cet avis » -- donc
# `IMerchantAccessApi`, donc `HBA.Merchants.Contracts`. La reference a ete ajoutee
# au csproj sans que ce Dockerfile suive : la solution compilait sur l'hote et
# l'image tombait en CS0234.
COPY services/marketplace/seller-service/src/HBA.Merchants.Contracts ./services/marketplace/seller-service/src/HBA.Merchants.Contracts
# CET HÔTE N'EMBARQUE PAS QUE REVIEW-SERVICE.
#
# `HBA.Engagement.Api` compile TROIS modules dans un seul processus : reviews,
# recommendations et wishlist. Les deux dossiers voisins sont des morceaux de ce
# service, pas des dépendances de contrat — donc copiés entiers.
#
# Sans ces deux lignes, `dotnet restore` ne trouve pas quatre `.csproj` et
# l'image ne se construit pas, alors que `make build` passe sur le poste.
COPY services/common/recommendation-service ./services/common/recommendation-service
COPY services/common/wishlist-service ./services/common/wishlist-service

COPY services/common/review-service ./services/common/review-service

RUN dotnet restore services/common/review-service/src/HBA.Engagement.Api/HBA.Engagement.Api.csproj
RUN dotnet publish services/common/review-service/src/HBA.Engagement.Api/HBA.Engagement.Api.csproj -c Release --disable-build-servers -p:UseSharedCompilation=false /m:1 -o /app/publish --no-restore

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

ENTRYPOINT ["dotnet", "HBA.Engagement.Api.dll"]
