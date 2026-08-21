# ---------------------------------------------------------------------------
# Compilación
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Los csproj se copian antes que el código para que la capa de restauración se
# reutilice mientras no cambien las dependencias.
COPY src/LegacyLens.Domain/LegacyLens.Domain.csproj    src/LegacyLens.Domain/
COPY src/LegacyLens.Analysis/LegacyLens.Analysis.csproj src/LegacyLens.Analysis/
COPY src/LegacyLens.Ai/LegacyLens.Ai.csproj            src/LegacyLens.Ai/
COPY src/LegacyLens.Web/LegacyLens.Web.csproj          src/LegacyLens.Web/

RUN dotnet restore src/LegacyLens.Web/LegacyLens.Web.csproj

# El script de ejemplo se referencia desde el csproj de la web, así que samples
# tiene que estar presente en el contexto de compilación.
COPY src/ src/
COPY samples/ samples/

RUN dotnet publish src/LegacyLens.Web/LegacyLens.Web.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ---------------------------------------------------------------------------
# Ejecución
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Container Apps enruta al 8080 y termina el TLS en su propio proxy.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

# SQLite necesita escribir en su directorio, y el proceso no corre como root.
RUN mkdir -p /app/Data && chown -R $APP_UID /app/Data
USER $APP_UID

ENTRYPOINT ["dotnet", "LegacyLens.Web.dll"]
