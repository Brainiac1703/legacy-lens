# Legacy Lens

**De un script de SQL Server heredado a documentación y un plan de migración.**

Trabajo de Fin de Máster — Máster de Desarrollo con IA (BIG School / MoureDev)
Autor: Nacho Tovar

| Recurso | Enlace |
| --- | --- |
| Repositorio | https://github.com/Brainiac1703/legacy-lens |
| Aplicación desplegada | _(pendiente: URL de Azure Container Apps)_ |
| Presentación | _(pendiente: URL de las slides)_ |
| Vídeo explicativo | _(pendiente: URL del vídeo)_ |
| Usuario de prueba | `demo@legacylens.dev` / `Demo.1234!` |

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
| Datos | SQLite con EF Core | Sin infraestructura de datos que aprovisionar |
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

### Requisitos

- SDK de .NET 10
- Opcional: Docker, Terraform 1.7+, Azure CLI (solo para desplegar)

### En local, sin IA

El análisis estático no necesita ninguna credencial:

```bash
git clone https://github.com/<usuario>/legacy-lens.git
cd legacy-lens
dotnet run --project src/LegacyLens.Web
```

La aplicación avisa en pantalla de que la IA no está configurada y entrega el inventario,
el grafo, las métricas y el riesgo. El usuario de prueba se siembra en el arranque.

### En local, con IA

Aprovisiona Azure OpenAI y configura el endpoint. Sin clave se usa la identidad de tu
sesión de `az login`:

```bash
cd infra
cp terraform.tfvars.example terraform.tfvars   # y pon tu subscription_id
terraform init
terraform apply

cd ..
dotnet user-secrets --project src/LegacyLens.Web \
  set "Ai:Endpoint" "$(cd infra && terraform output -raw openai_endpoint)"
dotnet run --project src/LegacyLens.Web
```

Necesitarás el rol *Cognitive Services OpenAI User* sobre el recurso.

### Ejecutar los tests

```bash
dotnet test
```

### Ejecutar el arnés de evaluación

Mide la calidad de la parte no determinista contra un conjunto dorado de reglas de negocio,
y compara modelos:

```bash
export Ai__Endpoint=$(cd infra && terraform output -raw openai_endpoint)
dotnet run --project tools/LegacyLens.Evals -- \
  --models gpt-4.1-mini,gpt-4o \
  --out docs/evals/informe.md
```

El informe incluye las métricas **y la salida generada íntegra**: una cobertura del cien por
cien no significa nada si nadie lee el texto.

### En contenedor

```bash
docker build -t legacylens .
docker run -p 8080:8080 legacylens
```

### Desplegar en Azure

```bash
cd infra
terraform apply -var deploy_app=true
cd ..
./scripts/deploy.ps1
```

`deploy.ps1` construye la imagen **dentro de Azure** con `az acr build`, así que no hace
falta autenticarse contra el registro ni subir la imagen desde casa.

---

## 4. Estructura del proyecto

```
legacy-lens/
├── src/
│   ├── LegacyLens.Domain/        Modelos. Sin dependencias externas.
│   │   ├── SqlObjects.cs         SqlObject, Dependency
│   │   ├── Metrics.cs            CodeMetrics, RiskScore, RiskFactor
│   │   └── Documentation.cs      ObjectDocumentation, MigrationPlan, AnalysisResult
│   │
│   ├── LegacyLens.Analysis/      Análisis estático. Determinista y testeable.
│   │   ├── TSqlAnalyzer.cs       Punto de entrada: script → AnalysisResult
│   │   ├── ObjectAnalysisVisitor.cs  Recorrido del AST
│   │   ├── NameResolver.cs       Normalización de nombres
│   │   └── RiskScorer.cs         Pesos explícitos con justificación
│   │
│   ├── LegacyLens.Ai/            Interpretación. La única parte no determinista.
│   │   ├── AiOptions.cs          Configuración y contador de consumo
│   │   ├── Prompts.cs            Construcción de prompts con hechos verificados
│   │   └── AiEnrichmentService.cs  Paralelismo, caché y tolerancia a fallos
│   │
│   └── LegacyLens.Web/           Blazor Web App
│       ├── Components/Pages/     Home, Analizar, Analisis, AnalisisDetalle
│       ├── Services/             Workflow, almacén, exportador, grafos Mermaid
│       └── Data/                 Identity, almacén de análisis, sembrado
│
├── tests/
│   └── LegacyLens.Analysis.Tests/  15 tests sobre el analizador
│
├── samples/legacy-erp.sql        Base de datos de ejemplo (sintética)
├── infra/                        Terraform: Azure OpenAI, ACR, Container Apps
├── scripts/deploy.ps1            Despliegue
├── .github/workflows/ci.yml      Integración continua
├── AGENTS.md                     Instrucciones para agentes de IA
├── tools/LegacyLens.Evals/       Arnés de evaluación del modelo
└── docs/
    ├── adr/                      Registros de decisiones de arquitectura
    ├── evals/informe.md          Resultado de la evaluación, con la salida generada
    ├── seguridad.md              Revisión contra OWASP Top 10 2025
    ├── hoja-de-ruta.md           Alcance, fases siguientes y descartes
    ├── trazabilidad-temario.md   Dónde se demuestra cada módulo del máster
    ├── guion-video.md            Guion de la presentación grabada
    └── slides.md                 Presentación (formato Marp)
```

La dirección de las dependencias es estricta: `Domain` no conoce a nadie, `Analysis` y `Ai`
solo conocen `Domain`, y `Web` los orquesta. `Analysis` **no** depende de `Ai`, que es lo
que permite que el análisis estático funcione por sí solo.

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

**Dos contextos de EF Core.** Identity trae su juego de migraciones con la plantilla y
conviene no tocarlo. El almacén de análisis es una única tabla de solo-añadir sin evolución
de esquema que versionar, así que usa un contexto aparte con `EnsureCreated`.

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
- **SQLite en almacenamiento efímero.** Al reiniciarse el contenedor se pierden los
  análisis guardados. Es aceptable para una demo; en producción iría a PostgreSQL o al
  almacenamiento persistente de Container Apps.
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
