# AGENTS.md

Instrucciones para agentes de IA que trabajen en este repositorio.

## Qué es este proyecto

Legacy Lens analiza scripts de SQL Server heredados y produce documentación y un plan de
migración. Su idea central, que condiciona todas las decisiones:

> **Lo que se puede saber con certeza, se calcula. Lo que requiere juicio, se le pregunta
> al modelo.**

Si una propuesta de cambio difumina esa frontera, es una mala propuesta aquí.

## Fronteras que no se cruzan

- **`LegacyLens.Analysis` no puede depender de `LegacyLens.Ai`.** Es lo que permite que el
  análisis estático funcione sin credenciales y sin red. Si necesitas datos del modelo
  dentro del análisis, el diseño está mal planteado.
- **`LegacyLens.Domain` no depende de nada.** Ni paquetes de terceros, ni infraestructura.
- **El SQL analizado nunca se ejecuta.** Solo se parsea. No añadas código que abra una
  conexión a la base de datos analizada, por conveniente que parezca.
- **Nada de dependencias inferidas por el modelo.** Las aristas del grafo salen del árbol
  sintáctico. Si el parser no lo ve, no entra en el grafo: se documenta como límite.

## Estructura

```
src/LegacyLens.Domain          Entidades y recorridos del grafo. Sin dependencias.
src/LegacyLens.Application     Casos de uso, puertos y behaviours. CQRS con MediatR.
src/LegacyLens.Persistence.EF  Adaptador de datos. Implementa el repositorio.
src/LegacyLens.Analysis        ScriptDom. Determinista → testeable con asserts.
src/LegacyLens.Ai              Modelo de lenguaje. No determinista → se mide con el arnés.
src/LegacyLens.Web             Blazor Web App (InteractiveServer).
src/LegacyLens.Mcp.Tools       Herramientas MCP. Las comparten el stdio y la web.
src/LegacyLens.Mcp             Hospedaje stdio del servidor MCP, para uso local.
tests/                         Tests del analizador y del grafo.
tools/LegacyLens.Evals         Arnés de evaluación del modelo.
Deploy/infra/                  Terraform.
Deploy/actions/                Composite action de Terraform para los pipelines.
```

Las dependencias apuntan **siempre hacia dentro**. `Web` y `Mcp` solo conocen `ISender`.
La web hospeda además el servidor MCP por HTTP en `/mcp`, con las mismas herramientas.

## Comandos

```bash
dotnet build LegacyLens.slnx                  # compila la solución (LegacyLens.slnx)
dotnet test LegacyLens.slnx                   # 51 tests: analizador, grafo y casos de uso
dotnet run --project src/LegacyLens.Web       # arranca en local
docker build -t legacylens .                  # valida el contenedor
cd Deploy/infra && terraform validate && terraform fmt -check -recursive
```

El CI ejecuta exactamente esas comprobaciones. Si pasan en local, pasan en CI.

## Convenciones

- **Idioma, y conviene leerlo entero porque no es uniforme:**
  - **Identificadores, nombres de fichero y rutas: en inglés.** Clases, métodos, variables,
    propiedades y las URL de las páginas. Sin excepciones.
  - **Comentarios y documentación: en español.** Son para el equipo, y traducirlos
    perdería matices en los razonamientos largos. Un comentario en español que menciona un
    identificador lo escribe con su nombre real en inglés.
  - **Texto que ve el usuario: en ficheros de recursos, nunca literal en el código.**
    `es-ES` es el idioma por omisión e `en` el alternativo.
  - **Mensajes de log: en español y sin localizar.** Decisión consciente del propietario del
    proyecto. Si algún día se quiere agregarlos o alertar sobre ellos, habrá que revisarla.
  - **Los tests son código, y sus nombres son identificadores: en inglés.** Es la parte
    donde más se escurre, porque un nombre de test se lee como una frase y apetece
    escribirla en el idioma en que se piensa. `The_nightly_process_is_the_riskiest_object`,
    no `El_proceso_nocturno_es_el_objeto_de_mayor_riesgo`. Vale también para las variables
    locales del test.
  - **En los YAML de pipeline la regla es la misma, y es fácil olvidarla:** nombres de
    trabajo, de paso, de salida, de entrada, de variable de entorno y de variable de shell
    **en inglés**, incluido el `name:` que se ve en la interfaz de Actions. Los comentarios
    siguen en español y los `echo` también, porque son log. El nombre del entorno de GitHub
    cuenta como identificador: cambiarlo obliga a rehacer las credenciales federadas, cuyo
    sujeto lo incluye.
  - El script de ejemplo `samples/legacy-erp.sql` está en español porque es **dato**, no
    código: representa un ERP heredado español y eso es parte del realismo del caso.
- **Comentarios: el qué está prohibido, el por qué no.** Si el código se explica solo, no
  lleva comentario: un buen nombre ahorra una frase. Si un comentario parafrasea la línea
  siguiente, sobra, y si repite lo que el nombre ya dice, sobra también.

  Lo que sí se queda es el motivo: una decisión con alternativas, una trampa que costó
  encontrar, un límite de una biblioteca. Esos pueden ser largos si el razonamiento lo es, y
  son lo más valioso del repositorio —el ciclo de la identidad administrada, la trampa de
  `breaks: true` en Marp, el orden de bytes del SID en Azure SQL—. Un comentario conciso que
  no explica nada no es mejor que ninguno.
- **Nada de resúmenes de cambios en el código.** No dejes comentarios del tipo «añadido
  para arreglar X»: eso es trabajo del historial de git.
- **Los tests son el criterio.** Si tocas el analizador, el test de diagnóstico
  `Diagnostic_dump` imprime el análisis completo de un script de ejemplo: úsalo para ver
  qué cambió de verdad antes de dar nada por bueno. Hay uno por cada ejemplo.

## Convenciones de código

- **`PascalCase` en lo público** —tipos, métodos, propiedades— y `camelCase` en locales y
  parámetros. Los campos privados, si hacen falta, van con `_camelCase`.

  Aquí casi no hay: el proyecto usa **constructores primarios** en doce clases, que son
  inyección por constructor y dejan el parámetro en `camelCase` sin campo intermedio. Es lo
  preferido; no conviertas un constructor primario en campos solo por la convención.
- **Inyección por constructor, nunca localizador de servicios ni estado estático.** Los
  cinco `GetRequiredService` que hay están donde toca: la raíz de composición, el sembrador
  de datos y el registro de endpoints. Fuera de ahí es un olor.
- **`async`/`await` para toda entrada y salida. Nunca `.Result` ni `.Wait()`.** Ojo con los
  dos `response.Result` de `AiEnrichmentService`: son la propiedad `Result` de
  `ChatResponse<T>` de `Microsoft.Extensions.AI`, no el bloqueo de una tarea. No los
  «arregles».
- **Los estilos no van en el atributo `style`.** Van en `wwwroot/app.css` si son globales o
  en el `.razor.css` del componente si son suyos, que es lo que da aislamiento de estilos en
  Blazor. Quedan tres `style=` en línea heredados de la plantilla; no añadas más.

  Este proyecto **no tiene SASS ni sistema de diseño**: son treinta ficheros CSS planos con
  Bootstrap debajo. La paleta vive en variables al principio de `app.css`, y el color de
  marca se sobrescribe ahí sobre las reglas de Bootstrap. Si algún día se introduce SASS,
  esta convención hay que reescribirla.

### Una preferencia de equipo que aquí no se aplica

**`@inject` en `.razor` frente a inyección por constructor en un code-behind `.razor.cs`.**
La segunda es una preferencia razonable —separa marcado de lógica y facilita probar el
componente— y es habitual en equipos grandes.

**En este repositorio no se aplica, a propósito.** Hay 109 `@inject` en 32 componentes y
ningún `.razor.cs`: es la forma idiomática de Blazor y la que usa la plantilla oficial.
Convertirlos sería un refactor de horas, con riesgo de romper el renderizado, y sin ninguna
ganancia funcional. Queda escrito para que se vea que es una decisión y no un descuido.

## Cómo trabajar en este repositorio

- **Acceso total al contexto.** Todo el repositorio es contexto: lee los ficheros que
  necesites sin preguntar. No hay que pedir permiso para abrir algo que ya está versionado.
- **Si falta contexto, busca en este orden:** primero las definiciones de los interfaces
  —`Application/Abstractions/`—, después sus implementaciones en los adaptadores. Analiza la
  estructura antes de generar código: casi todo lo que parece que falta ya existe con otro
  nombre.
- **No uses PowerShell para modificar código.** Da problemas de codificación con los
  acentos y choca con el búfer de Visual Studio, que puede tener el fichero abierto. Usa las
  herramientas de edición del agente. Para ejecutar comandos —compilar, probar, `git`— sí es
  válido.

  Vale lo mismo para los scripts intermedios: en este proyecto se han corrompido ficheros
  dos veces por pasar cadenas con barras invertidas a través de un *heredoc*, y una tercera
  por escribir un carácter de control sin querer.

## Al tocar los prompts

`src/LegacyLens.Ai/Prompts.cs` es el punto más delicado del proyecto.

- Los hechos verificados se inyectan **siempre**. Un prompt que solo manda código fuente es
  un retroceso.
- La instrucción de no inventar objetos no se toca.
- Cuando un objeto usa SQL dinámico, el prompt debe seguir exigiendo que el modelo lo
  advierta. Reconocer los límites del análisis forma parte del producto.
- Cambiar un prompt sin medir el efecto es adivinar. La fase 1 de la
  [hoja de ruta](docs/hoja-de-ruta.md) introduce el arnés de evaluación precisamente para
  esto.

## Documentación que hay que mantener al día

Al añadir una funcionalidad relevante, actualiza también:

- [`docs/trazabilidad-temario.md`](docs/trazabilidad-temario.md) si cubre un contenido nuevo
- [`docs/hoja-de-ruta.md`](docs/hoja-de-ruta.md) si completa o reordena una fase
- [`docs/adr/`](docs/adr/) si la decisión es estructural y tiene alternativas descartadas
- La sección de limitaciones del README si introduce una nueva

## Lo que no se hace sin preguntar

- Aprovisionar recursos en Azure.
- Cambiar el modelo desplegado o la región.
- Introducir un proveedor de IA nuevo.
- Añadir una dependencia que arrastre medio ecosistema para resolver diez líneas.
