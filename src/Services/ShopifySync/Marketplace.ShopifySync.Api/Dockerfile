# Build context = repo kökü
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY src/BuildingBlocks/Marketplace.BuildingBlocks/*.csproj src/BuildingBlocks/Marketplace.BuildingBlocks/
COPY src/Contracts/Marketplace.Contracts/*.csproj src/Contracts/Marketplace.Contracts/
COPY src/Services/ShopifySync/Marketplace.ShopifySync.Api/*.csproj src/Services/ShopifySync/Marketplace.ShopifySync.Api/
RUN dotnet restore src/Services/ShopifySync/Marketplace.ShopifySync.Api/Marketplace.ShopifySync.Api.csproj

COPY src/ src/
RUN dotnet publish src/Services/ShopifySync/Marketplace.ShopifySync.Api/Marketplace.ShopifySync.Api.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Marketplace.ShopifySync.Api.dll"]
