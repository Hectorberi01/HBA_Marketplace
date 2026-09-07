# syntax=docker/dockerfile:1

ARG DOTNET_BUILD_PLATFORM=linux/amd64
FROM --platform=$DOTNET_BUILD_PLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY shared ./shared
COPY services/marketplace/order-service/src/HBA.Order.Contracts ./services/marketplace/order-service/src/HBA.Order.Contracts
COPY services/marketplace/seller-service/src/HBA.Merchants.Contracts ./services/marketplace/seller-service/src/HBA.Merchants.Contracts
COPY services/common/identity-service/src/HBA.Identity.Contracts ./services/common/identity-service/src/HBA.Identity.Contracts
COPY services/common/payment-service/src/HBA.Financial.Payments.Contracts ./services/common/payment-service/src/HBA.Financial.Payments.Contracts
COPY services/delivery/driver-service/src/HBA.Drivers.Contracts ./services/delivery/driver-service/src/HBA.Drivers.Contracts
COPY services/common/analytics-service ./services/common/analytics-service

RUN dotnet restore services/common/analytics-service/src/HBA.Analytics.Api/HBA.Analytics.Api.csproj
RUN dotnet publish services/common/analytics-service/src/HBA.Analytics.Api/HBA.Analytics.Api.csproj -c Release --disable-build-servers -p:UseSharedCompilation=false /m:1 -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID

ENV DOTNET_EnableDiagnostics=0

EXPOSE 8080

ENTRYPOINT ["dotnet", "HBA.Analytics.Api.dll"]
