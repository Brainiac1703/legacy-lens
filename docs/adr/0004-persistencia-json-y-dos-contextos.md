# ADR 0004 · Análisis serializado en JSON y dos contextos de EF Core

**Estado:** parcialmente sustituida por el [ADR 0007](0007-capas-cqrs-y-repositorios.md)

> **Qué sigue vigente y qué no.**
>
> La decisión de guardar el análisis serializado en una columna **sigue en pie**, y por los
> mismos motivos: se escribe una vez y se lee entero. Lo que cambió es dónde vive esa
> decisión: la serialización estaba en un servicio del proyecto web y ahora está en el
> repositorio de la capa de persistencia, que es su sitio.
>
> La decisión de usar **dos contextos** queda sustituida. Se justificaba porque el contexto
> de análisis no tenía evolución de esquema que versionar y usaba `EnsureCreated`. Al pasar
> a Azure SQL con migraciones aplicadas desde el pipeline, dos historiales de esquema
> pasaron a ser dos cosas que aplicar y mantener en orden sin ninguna ventaja, así que hay
> un único contexto.
>
> También desapareció `EnsureCreated`, y con él la limitación que este documento anotaba
> como contrapartida.

## Contexto

El resultado de un análisis es un agregado con bastante estructura: una lista de objetos,
cada uno con sus métricas, su puntuación de riesgo con la lista de factores que la componen,
y su documentación con reglas de negocio y efectos colaterales. Más el grafo de dependencias
y el plan con sus fases.

Modelarlo de forma relacional serían seis o siete tablas con sus relaciones.

Hay además una restricción de partida: la plantilla de Blazor con Identity trae su propio
juego de migraciones de EF Core, que conviene no alterar.

## Decisión

**El agregado se guarda serializado en JSON** en una única columna. Fuera del documento
quedan solo las columnas necesarias para listar y filtrar sin abrir el contenido: fichero,
fecha, propietario, número de objetos y dos indicadores de si llegó a generarse documentación
y plan.

**Se usan dos contextos de EF Core.** `ApplicationDbContext` mantiene Identity con sus
migraciones intactas. `AnalysisDbContext` gestiona la tabla de análisis y se crea con
`EnsureCreated`.

Las enumeraciones se serializan como texto, para que el JSON siga siendo legible dentro de un
año.

## Consecuencias

**A favor:**

- Se ahorra el modelado, las migraciones y el mapeo de siete tablas para un caso que no lo
  necesita: el análisis **se escribe una vez y se lee entero**. Nunca se consulta por partes
  ni se actualiza campo a campo.
- Añadir un campo al modelo de dominio no requiere una migración.
- Identity queda aislado: su historial de migraciones no se toca.
- `EnsureCreated` es apropiado precisamente porque no hay evolución de esquema que
  versionar: es una tabla de solo-añadir.

**En contra:**

- No se pueden hacer consultas sobre el contenido del análisis desde SQL. Hoy no hace falta;
  el día que haga falta —comparar análisis en el tiempo, fase 4— habrá que revisar esta
  decisión, y entonces estará justificada.
- `EnsureCreated` no soporta migraciones. Si el esquema de la tabla cambiara, habría que
  recrearla o adoptar migraciones para este contexto.
- Un cambio incompatible en el modelo de dominio rompería la lectura de análisis antiguos.
  Aceptable mientras el JSON no salga del proceso que lo escribió.

## Alternativas consideradas

**Modelo relacional completo.** Es la respuesta ortodoxa, y sería la correcta si hubiera que
consultar por partes o actualizar campos sueltos. No es el caso, y habría costado varias
horas que hacían falta en otro sitio.

**Un solo contexto para todo.** Habría obligado a generar una migración de EF sobre el
contexto de Identity, tocando lo que la plantilla ya trae resuelto y a cambio de nada.

**Ficheros JSON en disco.** Descartada porque el contenedor tiene almacenamiento efímero y
porque perdía el filtrado por propietario sin escribir código para ello. Con SQLite sale
gratis.
