# ---------------------------------------------------------------------------
# Compilación
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Con gestión centralizada de versiones, el restore no funciona sin estos dos
# ficheros: Directory.Packages.props es donde están todas las versiones, y
# NuGet.config restringe los orígenes a nuget.org para que la restauración dentro
# del contenedor sea igual que la de cualquier máquina.
COPY Directory.Packages.props NuGet.config ./

# Los csproj se copian antes que el código para que la capa de restauración se
# reutilice mientras no cambien las dependencias. Tienen que estar los seis: el
# csproj de Web referencia a los demás, y el restore falla si alguno no existe.
COPY src/LegacyLens.Domain/LegacyLens.Domain.csproj                 src/LegacyLens.Domain/
COPY src/LegacyLens.Application/LegacyLens.Application.csproj       src/LegacyLens.Application/
COPY src/LegacyLens.Persistence.EF/LegacyLens.Persistence.EF.csproj src/LegacyLens.Persistence.EF/
COPY src/LegacyLens.Analysis/LegacyLens.Analysis.csproj             src/LegacyLens.Analysis/
COPY src/LegacyLens.Ai/LegacyLens.Ai.csproj                         src/LegacyLens.Ai/
COPY src/LegacyLens.Web/LegacyLens.Web.csproj                       src/LegacyLens.Web/

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

# El proceso no corre como root. Ya no hace falta preparar ningún directorio
# escribible: los datos viven en SQL Server, no en un fichero dentro de la
# imagen.
USER $APP_UID

ENTRYPOINT ["dotnet", "LegacyLens.Web.dll"]
