# Build context = repo kökü (BuildingBlocks ve root config'lere erişim için)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY src/BuildingBlocks/Marketplace.BuildingBlocks/*.csproj src/BuildingBlocks/Marketplace.BuildingBlocks/
COPY src/Services/Mobile/Marketplace.Mobile.Api/*.csproj src/Services/Mobile/Marketplace.Mobile.Api/
RUN dotnet restore src/Services/Mobile/Marketplace.Mobile.Api/Marketplace.Mobile.Api.csproj

COPY src/ src/
RUN dotnet publish src/Services/Mobile/Marketplace.Mobile.Api/Marketplace.Mobile.Api.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Marketplace.Mobile.Api.dll"]
