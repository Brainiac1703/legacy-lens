# Demo del servidor MCP

Esta carpeta está **vacía a propósito**. Se abre un agente aquí —Claude Code, por ejemplo— y
lo único que tiene a su alcance es el servidor MCP de Legacy Lens: ni el código de la
aplicación, ni los scripts SQL de `samples/`, ni el repositorio. Todo lo que el agente
responda sobre el sistema heredado tiene que haber salido de las cuatro herramientas.

Esa es la demostración. Si el agente pudiera leer `legacy-erp.sql` no se estaría probando
nada.

## Puesta en marcha

```bash
cp .mcp.json.example .mcp.json     # y pon la ruta real y la contraseña de tu .env
```

Antes de abrir el agente hacen falta tres cosas, y las tres se olvidan:

1. **El ejecutable compilado**, porque la configuración apunta a un binario, no al proyecto:
   `dotnet build src/LegacyLens.Mcp --configuration Release`
2. **La base de datos en marcha**, con `docker compose up -d` desde la raíz.
3. **Al menos un análisis guardado** por el usuario de `Mcp__OwnerEmail`. Entra en
   http://localhost:8081 con `demo@legacylens.dev`, analiza `legacy-erp.sql` y
   `legacy-almacen.sql`, y comprueba que aparecen en el listado.

Sin el tercer paso el servidor arranca, conecta y responde `[]` a todo. No da ningún error,
que es lo que lo hace desconcertante.

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
