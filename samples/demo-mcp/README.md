# Demo del servidor MCP

Esta carpeta está **vacía a propósito**. Se abre un agente aquí —Claude Code, por ejemplo— y
lo único que tiene a su alcance es el servidor MCP de Legacy Lens: ni el código de la
aplicación, ni los scripts SQL de `samples/`, ni el repositorio. Todo lo que el agente
responda sobre el sistema heredado tiene que haber salido de las cuatro herramientas.

Esa es la demostración. Si el agente pudiera leer `legacy-erp.sql` no se estaría probando
nada.

## La forma recomendada: contra el entorno desplegado

No hay nada que instalar. El servidor está hospedado dentro de la aplicación desplegada, así
que basta con la configuración:

```bash
cp .mcp.json.example .mcp.json     # y pega el token
```

O, equivalente, sin editar ficheros:

```bash
claude mcp add --transport http legacy-lens \
  https://ca-legacylens-tfm.bluedesert-728dc156.francecentral.azurecontainerapps.io/mcp \
  --header "Authorization: Bearer <token>"
```

**El token no está en este repositorio**, y no es un descuido: publicarlo aquí equivaldría a
no tener ninguno. Se entrega junto al enlace del vídeo.

Ni SDK de .NET, ni Docker, ni base de datos, ni credenciales de Azure. La credencial que abre
la base de datos es la identidad administrada del contenedor y no sale de Azure; quien
consulta solo presenta un token.

## La alternativa: en local, contra tu propia base de datos

Solo tiene sentido si vas a trabajar sobre el código. Aquí sí hay tres pasos, y los tres se
olvidan:

1. **Compilar el ejecutable**, porque la configuración apunta a un binario y no al proyecto:
   `dotnet build src/LegacyLens.Mcp --configuration Release`
2. **Levantar la base de datos**, con `docker compose up -d` desde la raíz.
3. **Hacer al menos un análisis** con el usuario de `Mcp__OwnerEmail`: entra en
   http://localhost:8081 con `demo@legacylens.dev` y analiza los dos ejemplos.

```json
{
  "mcpServers": {
    "legacy-lens": {
      "command": "C:/ruta/al/repositorio/src/LegacyLens.Mcp/bin/Release/net10.0/legacy-lens-mcp.exe",
      "env": {
        "ConnectionStrings__DefaultConnection": "Server=localhost,14330;Database=LegacyLens;User Id=sa;Password=LA-DE-TU-.env;TrustServerCertificate=True;Encrypt=True",
        "Mcp__OwnerEmail": "demo@legacylens.dev"
      }
    }
  }
}
```

Dos confusiones fáciles en esa cadena: la base es **`LegacyLens`**, la de la aplicación, y no
`LegacyERP`, que es el sistema heredado de ejemplo; y el puerto es el **14330**, no el 1433.
Con cualquiera de las dos mal, el servidor arranca, conecta y responde vacío.

> También puedes probar el hospedaje HTTP en local, que es lo más parecido al despliegue
> porque usa la misma imagen: pon `Mcp__ApiKey` en el `.env` con 32 caracteres o más, levanta
> el compose y apunta a `http://localhost:8081/mcp`. Con menos de 32 caracteres el endpoint no
> se publica, a propósito.

## El fallo que no da ningún error

Si no hay análisis del usuario configurado, las herramientas responden **vacío a todo**. El
servidor arranca, conecta, se da de alta y no se queja. Es lo más desconcertante de los dos
montajes, así que la primera pregunta al agente conviene que sea siempre «¿qué análisis hay?».

## Preguntas que enseñan algo

Las cuatro herramientas responden a las cuatro preguntas reales que uno se hace antes de tocar
un sistema que no conoce:

| Pregunta al agente | Herramienta |
| --- | --- |
| «¿Qué sistemas hay analizados?» | `list_analyses` |
| «¿Qué hace `usp_ConsolidarExpediciones` y de qué depende?» | `find_object` |
| «¿Quién toca la tabla `Existencias`, y quién la escribe?» | `where_used` |
| «Si cambio `Existencias`, ¿qué se rompe y qué migro antes?» | `change_risk` |

Los cuatro objetos son del mismo análisis, el de `legacy-almacen.sql`, y eso no es un detalle:
las herramientas trabajan sobre un análisis concreto. Preguntar por la tabla `Stock`, que está
en `legacy-erp.sql`, devuelve vacío aunque el objeto exista en el otro sistema.

La última es la que merece la pena grabar: el agente encadena varias llamadas y termina
proponiendo un orden de migración que no estaba escrito en ningún sitio, sino que sale del
grafo de dependencias y de la puntuación de riesgo.
