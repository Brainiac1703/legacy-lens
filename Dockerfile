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
COPY src/LegacyLens.Mcp.Tools/LegacyLens.Mcp.Tools.csproj           src/LegacyLens.Mcp.Tools/
COPY src/LegacyLens.Web/LegacyLens.Web.csproj                       src/LegacyLens.Web/

RUN dotnet restore src/LegacyLens.Web/LegacyLens.Web.csproj

# El script de ejemplo se referencia desde el csproj de la web, así que samples
# tiene que estar presente en el contexto de compilación.
COPY src/ src/
COPY samples/ samples/

# assets/ contiene el logo, que el csproj enlaza a wwwroot con un glob. Sin esta
# línea el glob no encuentra nada dentro del contenedor y el logo desaparece de
# la imagen SIN NINGÚN ERROR: el publish termina bien, el CI pasa y el despliegue
# sale verde. Solo se nota abriendo la aplicación y viendo un 404.
#
# Es la segunda vez que este Dockerfile se queda corto tras añadir algo al
# repositorio, y las dos veces por lo mismo: copia carpetas concretas en lugar
# del árbol entero. Se mantiene así a propósito, porque es lo que permite que la
# capa de restauración se reutilice, pero cada carpeta nueva hay que añadirla
# aquí y hay una comprobación en el CI que lo detecta si se olvida.
COPY assets/ assets/

# Sin --no-restore, y no es un descuido.
#
# El restore de arriba se ejecuta con solo los csproj presentes, que es lo que
# permite reutilizar la capa. Publicar después con --no-restore reutiliza ese
# resultado incompleto y el publish sale **sin wwwroot/_framework**, es decir sin
# blazor.web.js. La aplicación arranca, responde 200 y parece correcta, pero
# ningún botón funciona porque no hay runtime de Blazor en el navegador.
#
# Dejando que publish haga su propio restore, los paquetes ya están en la caché
# del contenedor, así que cuesta segundos y la capa de restore sigue sirviendo.
RUN dotnet publish src/LegacyLens.Web/LegacyLens.Web.csproj \
    -c Release \
    -o /app/publish

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
