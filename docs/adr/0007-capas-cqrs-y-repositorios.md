# ADR 0007 · Separación en capas con CQRS y repositorios

**Estado:** aceptada

## Contexto

El proyecto arrancó con cuatro proyectos: `Domain`, `Analysis`, `Ai` y `Web`. La dirección
de dependencias era correcta y `Analysis` no dependía de `Ai`, que es lo importante. Pero el
proyecto web había acumulado cosas que no le corresponden:

- Los dos `DbContext`, las migraciones y las entidades de Entity Framework.
- `AnalysisStore`, que serializaba el análisis a JSON: una decisión de **cómo se almacena**
  viviendo en la capa de presentación.
- `AnalysisWorkflow`, que orquestaba parser, IA y persistencia. Un caso de uso completo,
  imposible de ejecutar sin levantar la aplicación.
- El exportador a Markdown y el generador de grafos, que son lógica pura sin nada de web.

El síntoma más claro: la página del listado recibía `StoredAnalysis`, la **entidad de
Entity Framework**, con su columna `Payload` incluida. La presentación conocía el esquema de
la base de datos.

## Decisión

Seis proyectos con responsabilidades separadas:

| Proyecto | Responsabilidad | Depende de |
| --- | --- | --- |
| `Domain` | Entidades y value objects | nada |
| `Application` | Casos de uso, interfaces de salida, behaviours | `Domain` |
| `Persistence.EF` | Contexto, configuraciones, repositorio | `Domain`, `Application` |
| `Analysis` | Parser de T-SQL | `Domain`, `Application` |
| `Ai` | Azure OpenAI | `Domain`, `Application` |
| `Web` | Presentación | todas, solo para componer |

**`Analysis` y `Ai` no se renombran a `Infrastructure.*`.** Lo que hace que un proyecto sea
infraestructura es la dirección de sus dependencias, no su nombre, y un renombrado masivo
solo habría ensuciado el historial. Van agrupados en una carpeta de solución.

**CQRS con MediatR 12.5.0.** Cada operación es una petición con su handler. El proyecto web
solo conoce `ISender`.

**Repositorio con métodos de dominio**, en lugar de exponer el `DbContext` detrás de un
interface.

**El análisis es un `IStreamRequest`**, no un comando.

**Behaviours de log y validación** en la pipeline, con el log envolviendo a la validación.

## Consecuencias

**A favor:**

- Los casos de uso se pueden ejecutar sin navegador. `AnalyzeScriptHandler` recibe tres
  interfaces y emite progreso: es testeable con dobles, cosa que `AnalysisWorkflow` dentro
  del proyecto web no era en la práctica.
- La presentación dejó de conocer el esquema. El listado ahora recibe `AnalysisSummary`, un
  modelo de lectura, y la proyección deja el documento completo fuera de la consulta.
- La validación dejó de ser algo que hay que acordarse de invocar. Si existe un validador
  para una petición, se ejecuta siempre.
- La regla de que un usuario solo ve sus análisis está en la firma del repositorio y en el
  `WHERE` de la consulta, no en una comprobación posterior que se pueda olvidar.

**En contra, dicho claramente:**

- **Más ficheros y más indirección.** Para un producto de este tamaño, la estructura
  anterior era funcionalmente suficiente. Lo que se compra es capacidad de prueba y una
  frontera explícita; lo que se paga es que abrir una funcionalidad nueva toca más sitios.
- **MediatR condiciona la evolución.** La 12.5.0 es la última Apache-2.0: desde la 13 el
  paquete está bajo RPL-1.5 o licencia comercial. Quedarse en la 12 significa renunciar a
  mejoras futuras; actualizar significaría revisar la licencia.
- El puente entre el `IProgress` de la capa de IA y el flujo del handler usa un canal. Es
  código correcto pero no trivial, y es el sitio del proyecto que más cuidado pide.

## Alternativas consideradas

**`IApplicationDbContext` en lugar de repositorio.** Era la petición inicial, y es lo que
hace la Clean Architecture de referencia de Jason Taylor. Se descartó tras razonarlo: un
interface que expone `DbSet<T>` e `IQueryable` deja a la capa de aplicación hablando el
idioma de Entity Framework —consultas diferidas, seguimiento de entidades, comportamientos
de proveedor—, así que **no desacopla de verdad**; su beneficio real es una costura para
tests. Con solo tres operaciones de datos, un repositorio con métodos de dominio da ese
mismo beneficio, deja EF encerrado en su capa y además es menos código.

Habría sido la elección correcta con muchas consultas variadas, donde un repositorio degenera
en decenas de métodos y el `IQueryable` sale más práctico.

**Un comando normal para el análisis, con callback de progreso.** Obligaría a meter un
delegate en la petición, es decir a que el caso de uso conociera cómo la presentación quiere
enterarse del avance. Justo el acoplamiento que este refactor elimina.

**Sin MediatR, con servicios de aplicación inyectados directamente.** Menos ceremonia y
ninguna dependencia. Se descartó porque los behaviours —validación y log transversales sin
tocar cada handler— son la mitad del valor de esta decisión, y reimplementarlos sería
reescribir MediatR peor. Con la licencia como está, un despachador propio de cuarenta líneas
sigue siendo una alternativa razonable si algún día molesta la 12.

**Dos contextos de Entity Framework**, como estaban. Eran dos por herencia de la plantilla,
no por decisión. Con una base de datos servidor detrás, dos historiales de esquema
significan dos cosas que aplicar y mantener en orden en cada despliegue, sin ninguna ventaja.
