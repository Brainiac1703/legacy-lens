<img src="assets/logo.svg" alt="" width="72" align="left" hspace="16" />

# Legacy Lens

**De un script de SQL Server heredado a documentación y un plan de migración.**

Trabajo de Fin de Máster — Máster de Desarrollo con IA (BIG School / MoureDev)
Autor: Nacho Tovar

| Recurso | Enlace |
| --- | --- |
| Repositorio | https://github.com/Brainiac1703/legacy-lens |
| Aplicación desplegada | https://ca-legacylens-tfm.bluedesert-728dc156.francecentral.azurecontainerapps.io |
| Presentación | [docs/slides.pdf](docs/slides.pdf) · https://github.com/Brainiac1703/legacy-lens/blob/main/docs/slides.pdf |

---

## 1. Descripción general

En muchas empresas la lógica de negocio real no vive en el código de la aplicación, sino
enterrada en procedimientos almacenados de SQL Server escritos hace diez o quince años por
gente que ya no trabaja allí. Sin documentación. Cuando alguien plantea modernizar ese
sistema, el primer muro no es técnico sino **de conocimiento**: nadie sabe qué hace ese
código, qué depende de qué, ni por dónde empezar sin romper nada en producción.

Hoy eso se resuelve con un consultor leyendo procedimientos a mano durante semanas.

Legacy Lens automatiza ese primer paso. Se le entrega un script `.sql` — el que genera
cualquier SQL Server Management Studio con *Generate Scripts* — y devuelve:

- El **inventario** de tablas, vistas, funciones, procedimientos y disparadores.
- El **grafo de dependencias** real: qué lee y qué escribe cada objeto, y a quién invoca.
- Una **puntuación de riesgo de migración** con el desglose completo de por qué.
- La **documentación de cada objeto** en lenguaje de negocio, con sus reglas implícitas y
  una propuesta de a qué debería convertirse en .NET.
- Un **plan de migración por fases** siguiendo el patrón *strangler fig*.
- Todo ello **exportable como paquete de documentación en Markdown**.

### La decisión que sostiene el proyecto

> **Lo que se puede saber con certeza, se calcula. Lo que requiere juicio, se le pregunta
> al modelo.**

Si a un modelo de lenguaje se le pide «dime las dependencias de estos cincuenta
procedimientos», inventará tablas que no existen. Es el uso equivocado de la herramienta.

Legacy Lens separa las dos cosas de forma estricta:

| | Análisis estático | Modelo de lenguaje |
| --- | --- | --- |
| **Qué produce** | Dependencias, métricas, riesgo | Resúmenes, reglas de negocio, diseño propuesto |
| **Cómo** | Árbol sintáctico real del T-SQL | Prompt alimentado con los hechos ya verificados |
| **Garantía** | Exacto y reproducible | Interpretación fundamentada, revisable |

El parser usado es `Microsoft.SqlServer.TransactSql.ScriptDom`, el mismo que emplea SQL
Server Management Studio. Las dependencias no se infieren: se leen del AST. Y cuando un
objeto construye SQL dinámico, la aplicación **lo dice explícitamente** en lugar de fingir
que la lista de dependencias está completa, porque en ese caso no puede estarlo.

Esa separación es también lo que hace que la aplicación siga siendo útil sin IA: si Azure
OpenAI no está configurado o falla, el análisis estático se entrega igual.

---

## 2. Stack tecnológico

| Capa | Tecnología | Por qué |
| --- | --- | --- |
| Runtime | .NET 10 | Última LTS |
| Interfaz | Blazor Web App, render `InteractiveServer` | Ver decisiones de arquitectura |
| Análisis | `Microsoft.SqlServer.TransactSql.ScriptDom` 180.x | Parser oficial de T-SQL de Microsoft |
| IA | `Microsoft.Extensions.AI` sobre Azure OpenAI | Abstracción de proveedor y salida estructurada |
| Modelos | `gpt-4.1-mini` y `gpt-4o` | Dos modelos con papeles distintos, ver abajo |
| Datos | Azure SQL Database *serverless* con EF Core | Autopausa: el uso es a ráfagas |
| Aplicación | MediatR 12.5.0 y FluentValidation | CQRS con behaviours de log y validación |
| Autenticación | ASP.NET Core Identity | Requisito de usuario de prueba del TFM |
| Grafos | Mermaid | El grafo es texto: comparable, exportable, versionable |
| Contenedor | Docker multi-stage, imagen no-root | |
| Infraestructura | Terraform + Azure Container Apps | Infraestructura como código en el repositorio |
| CI | GitHub Actions | Compila, prueba, valida contenedor y Terraform |

### Los dos modelos

Documentar cada objeto es trabajo repetitivo, de contexto corto y muchas repeticiones: una
llamada por procedimiento. El plan de migración es **una sola** decisión que exige razonar
sobre el grafo completo.

Pagar el modelo grande cincuenta veces para lo primero no mejora el resultado, solo la
factura. Por eso:

- **`gpt-4.1-mini`** documenta objeto a objeto, en paralelo con límite de concurrencia.
- **`gpt-4o`** genera el plan global, una única vez y ya alimentado con los resúmenes de la
  fase anterior.

La decisión se tomó por criterio y **después se midió** — y el resultado sorprendió: en la
tarea de documentar, `gpt-4.1-mini` cubrió el 100 % de las reglas del conjunto dorado frente
al 88 % de `gpt-4o`, que fue más lacónico y omitió detalles relevantes. Ver
[ADR 0003](docs/adr/0003-dos-modelos-de-lenguaje.md) y el
[informe de evaluación](docs/evals/informe.md).

---

## 3. Instalación y ejecución

Hay tres formas de tener esto funcionando, en orden de menos a más trabajo:

| | Qué necesitas | Para qué |
| --- | --- | --- |
| **Docker Compose** | Docker Desktop | probarlo entero, con base de datos, en un comando |
| `dotnet run` | SDK de .NET 10 | trabajar en el código |
| Despliegue en Azure | una suscripción | ponerlo en producción, **desde los pipelines** |

Si solo quieres verlo, no hace falta nada: está desplegado y las credenciales de prueba
están en la tabla del principio.

### 3.1 La forma recomendada: Docker Compose

Levanta la aplicación y un **SQL Server con un ERP de ejemplo ya cargado**, del que puedes
generar scripts reales para analizar.

```bash
git clone https://github.com/Brainiac1703/legacy-lens.git
cd legacy-lens
cp .env.example .env       # y pon MSSQL_SA_PASSWORD
docker compose up --build
```

| | |
| --- | --- |
| Aplicación | http://localhost:8081 |
| SQL Server | `localhost,14330`, usuario `sa`, base de datos `LegacyERP` |
| Usuario | `demo@legacylens.dev` / `Demo.1234!` |

**No hace falta ninguna credencial de Azure.** El análisis estático funciona solo; la
aplicación avisa en pantalla de que la IA no está configurada y entrega el inventario, el
grafo, las métricas y el riesgo. Si quieres además la parte de IA, añade `Ai__Endpoint` al
`.env` — ver [3.3](#33-con-ia).

Cuatro detalles que conviene conocer:

- **El puerto de SQL Server no es el 1433 a propósito**, para no chocar con una instancia
  local ni con el contenedor de otro proyecto. Se cambia con `SQLSERVER_PORT` en `.env`.
- **La aplicación no se conecta a ese SQL Server, y no debe hacerlo nunca.** El SQL que
  analiza se parsea, jamás se ejecuta. El servidor está ahí como fuente de la que generar
  scripts con *Generate Scripts* y probar el flujo completo sin depender de ningún servidor
  de la empresa. Por eso el servicio `web` no declara `depends_on` sobre él: sería una
  dependencia falsa.
- **Los datos sobreviven a un `docker compose down`**, porque viven en un volumen. Para
  empezar de cero, `docker compose down -v`.
- `docker-compose.dcproj` existe para que el entorno aparezca como proyecto en la solución de
  Visual Studio y arranque con F5. Fuera de Visual Studio no hace falta.

  Ese proyecto fija `DockerComposeProjectName` al mismo nombre que declara
  `docker-compose.yml`, y no es cosmético: sin ello Visual Studio deriva un nombre de la ruta
  y F5 levanta **un segundo juego de contenedores** en paralelo al que hubiera creado
  `docker compose` desde la terminal. Los dos intentan publicar el mismo puerto y el segundo
  falla con `port is already allocated`.

### 3.2 Sin Docker, para trabajar en el código

Necesitas el **SDK de .NET 10**, fijado en `global.json`, y el SQL Server del compose
levantado: la aplicación aplica las migraciones al arrancar, así que sin base de datos no
llega a servir.

```bash
docker compose up -d sqlserver          # solo la base de datos
dotnet run --project src/LegacyLens.Web
```

La cadena de conexión de `appsettings.Development.json` ya apunta a ese contenedor, en
`localhost,14330`. Si prefieres tu propia instancia, cámbiala ahí.

**Si vas a abrirlo en Visual Studio, necesitas Visual Studio 2026.** El 2022 trae el SDK 9 y
no reconoce `net10.0`: el síntoma no es un error claro, sino avisos desconcertantes en los
nodos `Microsoft.AspNetCore.App` y `Microsoft.NETCore.App` del árbol de dependencias, y
ficheros que no se abren. El `global.json` está precisamente para que el diagnóstico sea
explícito en lugar de ese síntoma indirecto. Con VS Code, Rider o la línea de comandos no hay
ninguna restricción.

Un aviso si vas a comprobar algo de la interfaz: **ejecutar desde `bin/Release` devuelve 500
en todos los recursos estáticos**, `blazor.web.js` incluido, porque el manejador de desarrollo
busca los `.razor.js` con ámbito en una ruta que solo existe en el árbol de fuentes. Usa
`dotnet run` o `dotnet publish`, nunca el ejecutable de `bin`.

### 3.3 Con IA

Hace falta un recurso de **Azure OpenAI** con dos despliegues de modelo. Lo normal es que ya
lo tengas o que lo cree el pipeline (ver [3.6](#36-desplegar-en-azure)); una vez existe,
basta apuntar la aplicación a su endpoint:

```bash
dotnet user-secrets --project src/LegacyLens.Web set "Ai:Endpoint" "https://<tu-recurso>.openai.azure.com/"
dotnet run --project src/LegacyLens.Web
```

O, con Docker Compose, poniendo `Ai__Endpoint` en el `.env`.

**No se configura ninguna clave.** Sin `Ai:ApiKey` la aplicación usa `DefaultAzureCredential`,
que en tu máquina es la sesión de `az login` y en Azure es la identidad administrada del
Container App. Necesitas el rol *Cognitive Services OpenAI User* sobre el recurso.

### 3.4 Los tests

```bash
dotnet test LegacyLens.slnx
```

33 tests sobre las partes deterministas: el analizador, la puntuación de riesgo y los
recorridos del grafo. Es exactamente lo que ejecuta el CI.

### 3.5 El arnés de evaluación

Mide la calidad de la parte **no** determinista contra un conjunto dorado de reglas de
negocio, y compara modelos:

```bash
export Ai__Endpoint="https://<tu-recurso>.openai.azure.com/"
dotnet run --project tools/LegacyLens.Evals -- \
  --models gpt-4.1-mini,gpt-4o \
  --out docs/evals/informe.md
```

Gasta cuota del modelo, así que no corre en el CI. El informe incluye las métricas **y la
salida generada íntegra**: una cobertura del cien por cien no significa nada si nadie lee el
texto.

### 3.6 Desplegar en Azure

**Se despliega desde GitHub Actions, no a mano.** El pipeline `deploy.yml` hace el camino
completo en un solo recorrido: compila y prueba, planifica la infraestructura, **espera tu
aprobación**, aplica el plan que aprobaste —no uno nuevo—, actualiza el esquema de la base de
datos y publica la revisión de la aplicación, comprobando al final que responde 200.

La puesta en marcha son tres scripts que se ejecutan **una sola vez**, y está detallada en
[§9 · Despliegue continuo desde GitHub](#despliegue-continuo-desde-github).

> **Se puede aplicar Terraform a mano, y no es lo que deberías hacer.**
>
> ```bash
> cd Deploy/infra && terraform apply -var deploy_app=true
> ```
>
> Funciona, y sirve para depurar un cambio de infraestructura antes de subirlo. Pero se salta
> la aprobación, aplica un plan que nadie ha revisado, y deja el estado remoto tocado desde
> una máquina que no es la del pipeline. En este proyecto ya provocó dos incidencias: un plan
> aprobado que quedó obsoleto porque otra ejecución movió el estado, y un *lease* del blob de
> estado abandonado que hubo que romper a mano.
>
> Si lo usas, que sea a sabiendas y sobre una suscripción tuya.

### 3.7 El servidor MCP: el análisis dentro de tu agente

El análisis también se expone como servidor de **Model Context Protocol**, para que un agente
—Claude Code, por ejemplo— consulte el sistema heredado mientras escribes el código de la
migración, sin abrir la aplicación web.

```bash
dotnet build src/LegacyLens.Mcp --configuration Release
```

Y en la configuración MCP del cliente:

```json
{
  "mcpServers": {
    "legacy-lens": {
      "command": "ruta/al/repositorio/src/LegacyLens.Mcp/bin/Release/net10.0/legacy-lens-mcp.exe",
      "env": {
        "ConnectionStrings__DefaultConnection": "Server=localhost,14330;Database=LegacyLens;User Id=sa;Password=...;TrustServerCertificate=True",
        "Mcp__OwnerEmail": "demo@legacylens.dev"
      }
    }
  }
}
```

Cuatro herramientas, que son las cuatro preguntas que uno se hace de verdad antes de tocar un
sistema heredado:

| Herramienta | Responde a |
| --- | --- |
| `list_analyses` | qué sistemas hay analizados |
| `find_object` | qué hace esto, cuánto riesgo tiene y de qué depende |
| `where_used` | quién toca esta tabla, y si la lee o la escribe |
| `change_risk` | qué se rompe si lo cambio, y qué habría que migrar antes |

Las respuestas separan lo calculado de lo interpretado igual que la web, porque un agente
necesita saber en qué puede confiar sin verificar.

El servidor **se ejecuta en local y no autentica a nadie**: lo lanza tu propio agente con las
credenciales que le das, y está acotado a los análisis del correo configurado. No es un
servicio desplegado, y la razón está en el
[ADR 0008](docs/adr/0008-servidor-mcp-sobre-la-capa-de-aplicacion.md).

---

## 4. Estructura del proyecto

```
legacy-lens/
├── src/
│   ├── LegacyLens.Domain/         Entidades y value objects. Sin dependencias.
│   │
│   ├── LegacyLens.Application/    Casos de uso. Depende solo de Domain.
│   │   ├── Abstractions/          Los puertos: repositorio, analizador, IA
│   │   ├── Analyses/              Comandos, queries, handlers y validadores
│   │   ├── Common/Behaviours/     Log y validación en la pipeline de MediatR
│   │   ├── Knowledge/            Consultas sobre el grafo, que usa el servidor MCP
│   │   ├── Documentation/         Exportador a Markdown y grafos Mermaid
│   │   └── Costing/               Precios y consumo por modelo
│   │
│   ├── LegacyLens.Persistence.EF/ Adaptador de datos. Implementa el repositorio.
│   │   ├── LegacyLensDbContext.cs Contexto único: identidades y análisis
│   │   ├── Entities/              Lo que se guarda, no lo que se razona
│   │   ├── Configurations/        IEntityTypeConfiguration por entidad
│   │   ├── Migrations/            Historial de esquema
│   │   └── Repositories/          Aquí vive la serialización a JSON
│   │
│   ├── LegacyLens.Analysis/       Adaptador de parseo. Implementa ITSqlAnalyzer.
│   ├── LegacyLens.Ai/             Adaptador de IA. Implementa IAiEnrichmentService.
│   ├── LegacyLens.Mcp/            Servidor MCP. Solo traduce a MediatR, sin lógica.
│   └── LegacyLens.Web/            Presentación. Solo ISender y composición de DI.
│
├── tests/LegacyLens.Analysis.Tests/  33 tests sobre el analizador y el grafo
├── tools/LegacyLens.Evals/          Arnés de evaluación del modelo
│
├── Deploy/
│   ├── infra/                     Terraform
│   ├── actions/                   Composite actions del pipeline
│   └── sql/                       Scripts del plano de datos de Azure SQL
│
├── docker-compose.yml            App + SQL Server con su base y el ERP de ejemplo
├── Directory.Packages.props      Versiones de paquetes, centralizadas
├── NuGet.config                  Solo nuget.org, con asignación de origen
├── AGENTS.md                     Instrucciones para agentes de IA
└── docs/
    ├── adr/                      Siete decisiones de arquitectura razonadas
    ├── evals/informe.md          Resultado de la evaluación del modelo
    ├── seguridad.md              Revisión contra OWASP Top 10 2025
    ├── hoja-de-ruta.md           Alcance, fases siguientes y descartes
    ├── trazabilidad-temario.md   Dónde se demuestra cada módulo del máster
    ├── guion-video.md            Guion de la presentación grabada
    └── slides.md                 Presentación (formato Marp)
```

**La dirección de las dependencias es estricta y apunta siempre hacia dentro.** `Domain` no
conoce a nadie. `Application` solo conoce `Domain` y declara los puertos que necesita.
`Persistence.EF`, `Analysis` y `Ai` son adaptadores: implementan esos puertos, y ninguno
conoce a los otros. `Web` no hace más que componer.

Dos consecuencias concretas de esa disciplina:

- `Analysis` **no** depende de `Ai`, que es lo que permite que el análisis estático funcione
  por sí solo cuando no hay IA configurada.
- La presentación no conoce el esquema de la base de datos. El listado recibe un modelo de
  lectura, no la entidad de Entity Framework.

El razonamiento completo, con las alternativas descartadas, está en el
[ADR 0007](docs/adr/0007-capas-cqrs-y-repositorios.md).

---

## 5. Funcionalidades principales

### Análisis estático de T-SQL

Detecta objetos de los cinco tipos (tabla, vista, función, procedimiento, disparador),
cubriendo `CREATE`, `ALTER` y `CREATE OR ALTER`. Para cada objeto programable extrae:

- **Tablas que lee** frente a **tablas en las que escribe**, distinguiendo por sentencia:
  `INSERT`, `UPDATE`, `DELETE`, `MERGE` y `SELECT INTO` cuentan como escritura.
- **Objetos que invoca**, incluidas las **funciones escalares usadas dentro de
  expresiones** — que no se invocan con `EXEC` y son una dependencia real fácil de perder.
- **Construcciones de riesgo**: cursores, SQL dinámico en sus dos formas (`EXEC (@sql)` y
  `sp_executesql`), transacciones, TRY/CATCH, tablas temporales, complejidad de control.

Lo que queda deliberadamente **fuera** del grafo: tablas temporales, las pseudo-tablas
`inserted`/`deleted` de los disparadores, y los procedimientos del sistema. No son objetos
del esquema y solo añadirían ruido.

### Riesgo explicable

La puntuación nunca es un número suelto. Cada objeto lleva la lista de factores que la
componen, con sus puntos y su explicación:

```
dbo.usp_CerrarPedido — riesgo 55/100 (Alto)
  +15  CURSOR             Usa 1 cursor: lógica fila a fila que hay que replantear.
  +25  NO_TRANSACTION     Escribe en 4 tablas sin transacción explícita.
  +15  NO_ERROR_HANDLING  Modifica datos sin TRY/CATCH.
```

Se hizo así porque una puntuación de riesgo tiene que poder discutirse con el cliente, y
para eso no puede salir de una caja negra. Un test verifica que la suma de los factores
siempre coincide con el total.

### Documentación generada con IA

Para cada objeto: resumen en lenguaje de negocio, reglas de negocio implícitas, efectos
colaterales y destino propuesto en .NET. El prompt incluye los hechos ya verificados y
prohíbe explícitamente inventar objetos.

Se procesa en paralelo con límite de concurrencia, con caché por contenido (el mismo
procedimiento no se paga dos veces) y de forma tolerante a fallos: si un objeto falla, se
queda sin documentar y la interfaz lo refleja en lugar de tumbar el análisis completo.

### Plan de migración

Ordenado por fases según la posición en el grafo: primero los objetos autocontenidos, al
final los nudos de los que depende medio sistema. Con el riesgo de cada fase y los riesgos
globales del proyecto.

### Grafo de dependencias

Dos vistas: **llamadas entre objetos programables** (la que importa para planificar) y
**flujo de datos** con las tablas, distinguiendo lectura de escritura. Coloreado por nivel
de riesgo.

### Exportación

Un documento Markdown con el plan, el grafo (como bloque `mermaid`, que GitHub renderiza
solo), y una ficha por objeto. Incluye una sección que explica al lector qué parte del
informe es verificada y qué parte es interpretación del modelo.

---

## 6. Usuario y contraseña de prueba

```
Usuario:    demo@legacylens.dev
Contraseña: Demo.1234!
```

Se siembra automáticamente al arrancar, con el correo ya confirmado. También puedes
registrar una cuenta nueva; cada usuario ve únicamente sus propios análisis.

Dentro de la aplicación, el botón **«Analizar el ejemplo»** ejecuta el análisis sobre
`samples/legacy-erp.sql` sin necesidad de subir nada.

### Sobre el script de ejemplo

Es una base de datos de ERP **sintética, escrita a propósito para este proyecto**. No
procede de ningún sistema real. Reproduce los patrones que sí aparecen en sistemas
heredados: lógica de negocio en la base de datos, cursores, SQL dinámico, escrituras sin
transacción y procedimientos encadenados. Se eligió así para que la demo sea reproducible
por cualquiera y no exponga código de ningún cliente.

---

## 7. Decisiones de arquitectura

Cada una de estas decisiones tiene su registro completo en [`docs/adr/`](docs/adr/), con el
contexto, las consecuencias y las alternativas que se descartaron. Aquí va el resumen.

**Blazor `InteractiveServer` en lugar de `Auto`.** `Auto` migra el componente a
WebAssembly, y desde el navegador ya no puede tocar EF Core ni el parser: obligaría a
construir una API REST y DTOs para todo. `Auto` existe para resolver la latencia de
primera carga en aplicaciones con mucho tráfico, un problema que esta no tiene. Con
`Server`, los componentes llaman directamente al analizador, y el circuito de SignalR
proporciona **el progreso del análisis en tiempo real sin escribir nada**.

**Un único contexto de EF Core.** Al principio hubo dos, por herencia de la plantilla: uno
para Identity con migraciones y otro para los análisis con `EnsureCreated`. Con una base de
datos servidor detrás, dos historiales de esquema son dos cosas que aplicar y mantener en
orden en cada despliegue, sin ninguna ventaja.

**El resultado del análisis se guarda serializado como JSON**, no con una tabla por
entidad. Se escribe una vez y se lee entero, nunca se consulta por partes ni se actualiza
campo a campo. Un modelo relacional detallado habría añadido bastante trabajo sin resolver
ningún problema real. Fuera del documento quedan solo las columnas necesarias para listar.

**Identidad administrada en lugar de claves.** El Container App llama a Azure OpenAI y lee
el registro de contenedores con su propia identidad, mediante asignaciones de rol en
Terraform. No hay ningún secreto que guardar ni rotar. Si se configura `Ai:ApiKey` se usa
la clave, útil solo para desarrollo local.

**Mermaid en lugar de una librería de grafos.** El grafo se describe en texto, lo que lo
hace comparable entre ejecuciones y exportable dentro del propio Markdown.

### Limitaciones conocidas

- **El SQL dinámico es un límite infranqueable del análisis estático.** Las dependencias
  construidas en tiempo de ejecución no se pueden conocer sin ejecutar el código. La
  aplicación lo señala en lugar de disimularlo.
- **Afinidad de sesión.** El provider de `azurerm` no expone todavía `stickySessions` para
  Container Apps. Con una réplica no aplica, pero es lo primero que habría que resolver —
  con el provider `azapi` — antes de escalar horizontalmente, porque el circuito de Blazor
  Server tiene estado.
- **Una sola réplica.** El circuito de Blazor Server tiene estado, y escalar
  horizontalmente exige afinidad de sesión, que el provider de `azurerm` no expone todavía.
- **El estado de Terraform es local.** No hay backend remoto configurado, así que el
  `terraform.tfstate` vive en la máquina de quien aplica y está excluido del repositorio.
  Para un proyecto de una sola persona es suficiente; en equipo haría falta un backend en
  Azure Storage con bloqueo.
- **La documentación generada por IA hay que revisarla.** Es una interpretación
  fundamentada, no una verdad demostrada, y el propio informe exportado lo advierte.

---

## 8. Cómo se construyó: la IA como herramienta de desarrollo

Al ser un máster de desarrollo *con* IA, el proceso también forma parte del trabajo.

El proyecto se construyó con Claude Code en sesión de pareja. El reparto que funcionó:

- **Delegado a la IA:** la exploración de la API de `ScriptDom` (verbosa y con muchos tipos
  de nodo), la generación del script de ejemplo, el código repetitivo de la interfaz y la
  primera versión de los prompts.
- **Decidido por mí:** la separación entre lo determinista y lo interpretado — que es la
  idea central del proyecto —, el modelo de dominio, la elección de los dos modelos y todas
  las decisiones de arquitectura de la sección anterior.
- **Lo que hizo falta corregir a mano:** el analizador perdía las funciones escalares
  invocadas dentro de expresiones, y la primera versión confundía «objetos a los que nadie
  llama» con «objetos que no llaman a nadie», que son cosas distintas y llevan a órdenes de
  migración opuestos. Ninguno de los dos fallos lo detectó la IA: los detectó el volcado de
  diagnóstico de los tests.

La conclusión práctica: la IA acelera enormemente la parte mecánica, y los tests siguen
siendo lo que separa «compila» de «funciona».

---

---

## 9. Estado del proyecto y continuidad

**Legacy Lens no termina con la entrega del TFM.** Resuelve un problema que tengo delante en
el trabajo, así que va a seguir evolucionando.

Lo que se entrega es el núcleo funcionando de principio a fin: análisis estático,
documentación con IA, plan de migración, aplicación desplegada y exportación del informe.
Lo que queda fuera está fuera **por plazo y con criterio**, no por descuido, y está
planificado:

| Fase | Contenido | Estado |
| --- | --- | --- |
| **0** | Núcleo del producto | **Entregado** |
| **1.1** | **Arnés de evaluación de LLM** con conjunto dorado y detección automática de alucinación | **Entregado** |
| **1.2** | **DevSecOps**: CodeQL, Dependabot y mapeo OWASP Top 10 2025 | **Entregado** |
| **1.3** | **Coste y consumo de tokens** visibles por análisis y por modelo | **Entregado** |
| **2** | Servidor **MCP**, **RAG** sobre base vectorial, más dialectos (PL/SQL, Delphi) | Planificado |
| **3** | Observabilidad con OpenTelemetry, E2E con Playwright, PostgreSQL | Planificado |
| **4** | Análisis asíncrono encolado con patrón Outbox, comparación temporal | Planificado |

El detalle de cada fase, con criterios de aceptación concretos y la lista de lo que se ha
**descartado a propósito** —microservicios, Kubernetes, *fine-tuning*— está en
[`docs/hoja-de-ruta.md`](docs/hoja-de-ruta.md).

La fase 1.1 ya está entregada, y su primer resultado justifica por sí solo haberla hecho: la
elección de modelos estaba tomada por criterio razonable, y al medirla resultó que el modelo
económico **documenta mejor** que el capaz. Eso no se descubre discutiendo, se descubre
midiendo.

### Despliegue continuo desde GitHub

La infraestructura y la aplicación se gestionan desde el repositorio, con **un único camino a
producción** y sin ningún secreto de cliente almacenado: la autenticación es con OIDC, así que
GitHub presenta un token firmado que Azure valida contra este repositorio, esta rama y este
entorno.

| Workflow | Se dispara con | Qué hace |
| --- | --- | --- |
| `ci.yml` | *pull request* y `main` | Compila, ejecuta los 33 tests, construye la imagen y **comprueba que arranca, migra y sirve el runtime de Blazor**. Valida el Terraform. |
| `deploy.yml` | `main`, o a mano | El camino completo a producción. Detalle abajo. |
| `security.yml` | *pull request*, `main` y semanalmente | CodeQL y búsqueda de paquetes vulnerables, que **falla la ejecución** si aparece alguno. |

**Hubo dos pipelines, uno de infraestructura y otro de aplicación, y se unieron en uno.**
Separados podían pisarse: el de aplicación desplegaba una revisión contra una infraestructura
que el otro estaba cambiando, y el orden entre aplicar el esquema de base de datos y publicar
la revisión no estaba garantizado. Ese orden no es negociable —el esquema tiene que estar listo
antes de que arranque el código que lo usa— así que ahora es un solo recorrido de cinco pasos:

1. **Compilar y probar.** Si los tests fallan, no se toca nada.
2. **Planificar** la infraestructura, con el plan completo en el resumen de la ejecución.
3. **Esperar aprobación** del entorno `production`. Es la parada de revisión.
4. **Aplicar el plan aprobado**, no uno nuevo: no hay ventana para que algo cambie entre que
   lo lees y se ejecuta.
5. **Actualizar la base de datos** y después **publicar la revisión**, comprobando que la
   aplicación responde 200 antes de dar el despliegue por bueno.

Hay **dos identidades con permisos distintos**, y es deliberado: la de despliegue —la que se
ejecuta en cada *commit*— está limitada a un grupo de recursos y **no puede tocar la
infraestructura ni conceder permisos**. El razonamiento completo, con sus contrapartidas
declaradas, está en el [ADR 0006](docs/adr/0006-cicd-con-oidc-y-dos-identidades.md).

#### Puesta en marcha, una sola vez

```powershell
# 1. Cuenta de almacenamiento para el estado de Terraform, con versionado.
#    Se omite si ya tienes una cuenta de despliegues donde guardarlo.
./scripts/bootstrap-tfstate.ps1

# 2. Configurar el backend y migrar el estado local al remoto.
cp Deploy/infra/backend.hcl.example Deploy/infra/backend.hcl   # y rellenar
cd Deploy/infra; terraform init -migrate-state -backend-config=backend.hcl; cd ..

# 3. Identidades y credenciales federadas para GitHub.
./scripts/bootstrap-github-oidc.ps1 -Repository Brainiac1703/legacy-lens
```

El último script imprime las variables que hay que definir en GitHub, en
*Settings → Secrets and variables → Actions*.

**Casi todo son variables y no secretos**, porque con OIDC ninguno de esos valores es una
credencial. La única excepción, cuando aplica, es `TFSTATE_ACCESS_KEY`: si la cuenta de
almacenamiento del estado está en otra suscripción —el caso de una cuenta de despliegues
corporativa compartida—, no se le pueden asignar roles desde este proyecto y el backend
tiene que autenticarse con clave. Está anotado como desviación consciente en el
[ADR 0006](docs/adr/0006-cicd-con-oidc-y-dos-identidades.md).

Si además configuras una regla de protección en el entorno `production`, GitHub exigirá
aprobación manual antes de tocar infraestructura o publicar una revisión — el equivalente a
las aprobaciones de *release* de Azure DevOps.

### Idiomas

La aplicación está en **español de España** por omisión y en **inglés** como alternativa. El
selector está arriba a la derecha.

**En las páginas del producto, ningún texto que ve el usuario está escrito en el código:
todo vive en ficheros de recursos.** El área de cuenta es distinta y conviene decirlo con
precisión, porque viene del andamiaje de la plantilla de Identity y trae su propio texto en
inglés incrustado.

Están localizadas las pantallas a las que se llega navegando: inicio de sesión, registro,
confirmación de la cuenta, y toda la gestión del perfil —correo, contraseña, verificación en
dos pasos, claves de acceso y datos personales— con su menú lateral.

Siguen en inglés las que solo aparecen dentro de un flujo ya empezado o ante un error:
recuperación de contraseña, entrada con segundo factor o con código de recuperación,
configuración de la aplicación de autenticación, y las páginas de cuenta bloqueada, acceso
denegado o enlace caducado. Son unas 90 cadenas en 19 componentes, y está en la hoja de ruta.

Un límite que no se arregla con recursos: los mensajes de validación de esas páginas los
genera `DataAnnotations` a partir de los atributos del modelo, así que salen en inglés aunque
la etiqueta del campo esté traducida. Las páginas del producto no tienen ese problema porque
validan con FluentValidation desde la capa de aplicación, que sí lee los recursos.

| Recurso | Qué contiene |
| --- | --- |
| `src/LegacyLens.Web/Resources/UiText.resx` | Textos de la interfaz, en español |
| `src/LegacyLens.Web/Resources/UiText.en.resx` | Los mismos, en inglés |
| `src/LegacyLens.Application/Resources/ValidationText.resx` | Mensajes de validación |

Tres decisiones que conviene conocer:

- **El español vive en los `.resx` neutros**, no en un `es-ES.resx`. Así, una cultura sin
  traducir cae en español en lugar de mostrar la clave del recurso.
- **Los mensajes de validación viven en la capa de aplicación**, no en la web, porque es el
  validador quien decide qué está mal. Si mañana hubiera una API además de la web, ambas
  darían el mismo mensaje.
- **El caso de uso no produce texto.** El progreso del análisis emite la fase y el nombre del
  objeto; el mensaje lo compone la presentación. Antes el handler devolvía una cadena en
  español, con lo que un caso de uso decidía la redacción de la interfaz.

Los **mensajes de log siguen en español y sin localizar**, por decisión explícita. Es una
desviación de la práctica habitual —los logs se agregan y se buscan, y conviene un solo
idioma— y está anotada como tal.

### Seguridad

[`docs/seguridad.md`](docs/seguridad.md) revisa el proyecto contra las diez categorías de
**OWASP Top 10 2025**, una por una, incluyendo lo que queda pendiente y por orden de
importancia real. Dos apartados merecen la pena leerse:

- **A05 Injection** se resuelve por diseño: el SQL recibido se parsea, **nunca se ejecuta**.
  No existe ninguna conexión a la base de datos analizada, y esa frontera está escrita en
  `AGENTS.md` para que no se debilite en el futuro.
- **A03 Supply Chain** dejó de ser teórico durante el desarrollo: apareció una dependencia
  transitiva con CVE conocido. Se corrigió, y ahora el CI falla si vuelve a ocurrir.

### Trazabilidad con el temario del máster

[`docs/trazabilidad-temario.md`](docs/trazabilidad-temario.md) recorre los doce módulos del
máster y señala dónde se demuestra cada contenido — y también, con honestidad, qué no está
cubierto y en qué fase entra.

---

## Licencia

MIT
