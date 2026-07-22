# Build context = repo kökü (BuildingBlocks ve root config'lere erişim için)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY src/BuildingBlocks/Marketplace.BuildingBlocks/*.csproj src/BuildingBlocks/Marketplace.BuildingBlocks/
COPY src/Services/Cms/Marketplace.Cms.Api/*.csproj src/Services/Cms/Marketplace.Cms.Api/
RUN dotnet restore src/Services/Cms/Marketplace.Cms.Api/Marketplace.Cms.Api.csproj

COPY src/ src/
RUN dotnet publish src/Services/Cms/Marketplace.Cms.Api/Marketplace.Cms.Api.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Marketplace.Cms.Api.dll"]
