# ADR 0008 · Servidor MCP sobre la capa de aplicación

**Estado:** aceptado · **Fecha:** 22/08/2026

## Contexto

Legacy Lens produce documentación, un grafo de dependencias y un riesgo explicable de un
sistema heredado. Pero todo eso vive en una página web, y el trabajo real de migrar no se
hace en una página web: se hace en un editor, escribiendo el código nuevo, con un agente al
lado.

En ese momento las preguntas que uno tiene son concretas y repetitivas. ¿Qué hace este
procedimiento? ¿Quién escribe en esta tabla? ¿Qué se rompe si cambio esto? Tener que
cambiar de ventana, buscar el objeto y leer una ficha convierte una consulta de cinco
segundos en una interrupción.

Además hay un problema de fondo peor: el agente que escribe la migración **no sabe nada del
sistema heredado**. Se le puede pegar el código de un procedimiento en el prompt, pero no el
grafo de dependencias ni el radio de impacto, que es justo lo que evita romper cosas.

## Decisión

**Exponer el conocimiento como un servidor MCP** con transporte stdio, en un proyecto
propio, `src/LegacyLens.Mcp`.

Cuatro herramientas, que son las cuatro preguntas de arriba:

| Herramienta | Responde a |
| --- | --- |
| `list_analyses` | qué sistemas hay analizados |
| `find_object` | qué hace esto, cuánto riesgo tiene y de qué depende |
| `where_used` | quién toca esta tabla, y si la lee o la escribe |
| `change_risk` | qué se rompe si lo cambio, y qué habría que migrar antes |

**El servidor no tiene lógica.** Cada herramienta resuelve el propietario, manda una consulta
por MediatR y serializa el resultado. Las consultas nuevas —`FindObjectQuery`,
`WhereUsedQuery`, `ChangeRiskQuery`— viven en `LegacyLens.Application`, junto a las que ya
usaba la web, y el recorrido del grafo vive en `LegacyLens.Domain`.

Esa colocación no es estética. Si mañana la web quiere una página «quién usa esta tabla», la
consulta ya está. Y el recorrido del grafo, que es donde un error se propagaría a todas las
respuestas, queda cubierto por tests que no necesitan base de datos ni modelo.

**Las consultas se resuelven sobre un único análisis**, cuyo identificador es un argumento de
cada herramienta. Un análisis es un sistema heredado, y preguntar «de quién depende esta
tabla» solo tiene sentido dentro de un sistema. De paso acota el coste: se carga un
documento, no el histórico del usuario.

**El servidor no autentica a nadie.** Se ejecuta en la máquina de una persona, lanzado por su
propio agente, con las credenciales que esa persona le da en la configuración. Lo que sí hace
es no salirse de los análisis de un usuario: el correo del propietario es obligatorio y las
consultas de la capa de aplicación exigen su identificador, sin ninguna forma de pedirles
«todos».

## Consecuencias

**A favor:**

- Cambia lo que el producto es. Deja de ser una herramienta que consultas y pasa a ser
  contexto que el agente tiene mientras migra.
- Coste cero de infraestructura: ni despliegue, ni recursos de Azure, ni llamadas al modelo.
  El servidor solo lee la base de datos que ya existe.
- Las respuestas separan lo calculado de lo interpretado, igual que la web. La ficha marca
  qué viene del árbol sintáctico y qué del modelo, que es la tesis del
  [ADR 0001](0001-separar-lo-calculado-de-lo-interpretado.md) llevada al consumo por agente:
  quien recibe estos datos necesita saber en cuáles puede confiar sin verificar.

**En contra, y conviene decirlo:**

- Es otro artefacto que hay que construir y configurar a mano, y su configuración vive fuera
  del repositorio, en el fichero del cliente MCP. No hay forma de probar en CI que la
  configuración de una máquina concreta sea correcta.
- El servidor lee la base de datos directamente, así que no sirve contra el despliegue en
  Azure: esa base solo admite identidades de Entra y su cortafuegos solo deja pasar servicios
  de Azure. **Esto era una limitación, no una decisión**, y la primera versión de este
  documento la presentó como si fuera deliberada. Está resuelto: ver la revisión al final.
- Las descripciones de las herramientas son, en la práctica, prompts. Son el único contexto
  que el modelo tiene para elegir herramienta y argumentos, así que un cambio descuidado ahí
  degrada el comportamiento sin que falle ningún test. Tienen el mismo cuidado que
  `Prompts.cs` y merecen la misma revisión.

## Dos cosas que solo aparecieron al probarlo

Ninguna de las dos se ve compilando, y las dos habrían llegado al usuario:

- **La salida estándar es el canal del protocolo.** En transporte stdio, cualquier línea de
  log que caiga en stdout corrompe los mensajes JSON-RPC, y el síntoma es un servidor que «no
  responde» sin ningún error. Todo el registro va a stderr explícitamente.
- **La raíz de contenido no puede ser el directorio de trabajo.** Al servidor lo lanza el
  agente desde donde le conviene —la carpeta del proyecto que se está migrando—, así que con
  el valor por omisión no encontraba su propio `appsettings.json`. Se fija al directorio del
  ejecutable. El síntoma era una excepción de cadena de conexión ausente que no tenía nada
  que ver con la causa.

Y una tercera de usabilidad, que apareció al leer una respuesta real: las enumeraciones se
serializaban como números. `"Kind": 1` no le dice nada a un modelo que tiene que decidir si
una relación es una lectura o una escritura. Van como texto, por el mismo motivo por el que
ya iban así en la persistencia.

## Alternativas consideradas

**Transporte HTTP en lugar de stdio.** Habría permitido un servidor compartido, desplegado
junto a la web. Descartado por ahora: obliga a resolver autenticación y autorización de
verdad —el servidor pasaría a ser una superficie pública sobre datos de varios usuarios— y no
hay ninguna necesidad que lo pida. `ModelContextProtocol.AspNetCore` lo permitiría sin tocar
las herramientas, así que la puerta queda abierta.

**Una API REST y que el agente la llame con `curl`.** Funciona, y de hecho es lo que muchos
hacen. Pero entonces el agente tiene que saber que esa API existe, cómo se autentica y qué
rutas tiene, y eso hay que explicárselo en cada conversación. MCP existe precisamente para
que el descubrimiento y el esquema de los argumentos vengan dados.

**Reimplementar las consultas dentro del servidor** para no tocar la capa de aplicación. Más
rápido de escribir y peor en todo lo demás: dos implementaciones del mismo grafo que se
separan en cuanto una cambie, y la lógica nueva sin tests ni posibilidad de reutilizarla en
la web.

**Exponer todos los análisis del usuario en cada consulta**, sin identificador de análisis.
Más cómodo de llamar, pero obliga a cargar el histórico completo para responder y hace
ambigua cualquier respuesta: dos sistemas heredados distintos pueden tener un `dbo.Facturas`
cada uno, y mezclarlos daría un radio de impacto falso.

## Revisión · 24 de agosto de 2026

La versión original de este documento decía que el servidor estaba «acotado a uso local a
propósito». No era cierto: era un atajo, y presentarlo como una decisión de diseño lo
disfrazaba. Un servidor MCP que se despliega en la nube y que solo puede usar quien tenga el
repositorio y una base de datos local no está terminado.

Lo que se ha cambiado:

- Las herramientas se han movido a `src/LegacyLens.Mcp.Tools`, una biblioteca que consumen
  dos hospedadores. No se referencia el ejecutable desde la web porque su `appsettings.json`
  se copia al directorio de salida y sobrescribiría el de la aplicación.
- La aplicación web expone las mismas cuatro herramientas en `/mcp` con transporte HTTP
  (`ModelContextProtocol.AspNetCore`). La credencial que abre la base de datos sigue siendo
  la identidad administrada del contenedor y no sale de Azure: quien consulta solo presenta
  un token.
- El ejecutable stdio se mantiene sin cambios para uso local, que es lo que enseña el vídeo.

Y dos cosas que solo aparecieron al probarlo, otra vez:

- **El token no puede entrar por una variable de Terraform.** El plan se sube como artefacto
  de la ejecución, y en un repositorio público eso es descargable con el valor en claro
  dentro. Lo escribe el pipeline con `az containerapp secret set`, y Terraform deja de
  gestionar el valor del secreto —solo ese, no las variables de entorno—. El relleno que crea
  Terraform es inerte por construcción: la aplicación exige 32 caracteres y con menos no
  publica el endpoint, así que un fallo del pipeline no deja la puerta abierta con una
  credencial escrita en el repositorio.
- **`UseStatusCodePagesWithReExecute` se comía el 401.** Intercepta cualquier respuesta de
  error sin cuerpo y reejecuta la petición contra `/not-found`; ese POST con
  `application/json` contra una página Razor lo rechazaba antiforgery, y el cliente recibía
  «400 The request has an incorrect Content-type». El rechazo lleva cuerpo a propósito.
