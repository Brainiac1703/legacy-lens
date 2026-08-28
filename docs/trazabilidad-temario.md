# Trazabilidad con el temario del máster

Este documento existe por una razón concreta: el TFM pide «un proyecto que demuestre los
conocimientos adquiridos a lo largo del máster», y conviene poder señalar **dónde** se
demuestra cada cosa en lugar de afirmarlo de palabra.

También señala con honestidad lo que **no** está cubierto. Las ausencias son deliberadas y
están planificadas como fases siguientes en [hoja-de-ruta.md](hoja-de-ruta.md), no
olvidadas.

Leyenda: **✔** implementado · **◐** parcial · **○** planificado, ver hoja de ruta

---

## 00 · Fundamentos del desarrollo de software

| Contenido | | Dónde |
| --- | --- | --- |
| Terminal y línea de comandos | ✔ | `scripts/deploy.ps1`, flujo de trabajo con CLI |
| Control de versiones con Git y GitHub | ✔ | Repositorio, `.gitignore` que excluye `tfvars` y `tfstate` |
| Pensamiento computacional | ✔ | El recorrido del AST y el cálculo del riesgo son el núcleo del proyecto |

## 01 · Ingeniería de Software

| Contenido | | Dónde |
| --- | --- | --- |
| Principios SOLID | ✔ | Dirección de dependencias entre proyectos; `Analysis` y `Ai` dependen solo de `Domain` |
| Inversión de dependencias | ✔ | La infraestructura implementa interfaces declarados en `Application`, no al revés. Cada capa se registra con su propio `Add...` |
| DRY, KISS, YAGNI | ✔ | Decisión de serializar el análisis en JSON en lugar de modelar 6 tablas: ver README §7 |
| Patrones de diseño | ✔ | **Visitor** en `ObjectAnalysisVisitor`; Repository en `AnalysisRepository`; Mediator y Decorator en la pipeline de behaviours |
| Antipatrones | ✔ | El propio producto **detecta antipatrones** en el código analizado: cursores, SQL dinámico, escrituras sin transacción |
| Validación de requisitos | ✔ | Requisitos del TFM trazados aquí; validación de entrada con FluentValidation en la pipeline |
| Spec Driven Development | ◐ | El alcance se fijó por escrito antes de codificar; sin especificación formal ejecutable |

## 02 · Arquitectura de Software

| Contenido | | Dónde |
| --- | --- | --- |
| Decisiones arquitectónicas y su registro | ✔ | `docs/adr/` |
| Monolito modular | ✔ | Seis proyectos con fronteras explícitas y dirección de dependencias controlada |
| Separación dominio / aplicación / infraestructura | ✔ | `Domain` sin dependencias externas; `Web` orquesta |
| Clean Architecture | ✔ | `Application` con casos de uso como clases e interfaces de salida que implementan `Persistence.EF`, `Analysis` y `Ai`. [ADR 0007](adr/0007-capas-cqrs-y-repositorios.md) |
| **CQRS** | ✔ | Comandos y queries con MediatR 12.5.0; el análisis como `IStreamRequest` |
| Patrón repositorio | ✔ | `IAnalysisRepository` con métodos de dominio; EF encerrado en `Persistence.EF` |
| Puertos y adaptadores | ✔ | `ITSqlAnalyzer`, `IAiEnrichmentService` e `IAnalysisRepository` son los puertos; los tres proyectos de infraestructura, los adaptadores |
| DDD: entidades y value objects | ◐ | `SqlObject` es una entidad; `CodeMetrics` y `RiskScore` son value objects inmutables. Sin agregados ni repositorios de dominio formales |
| Event-Driven Architecture, patrón Outbox | ○ | Fase 4: el análisis como trabajo asíncrono encolado |
| Microservicios | — | Descartado a propósito: no hay ningún problema que justifique distribuir esto |

## 03 · Fundamentos de la IA

| Contenido | | Dónde |
| --- | --- | --- |
| Capacidades y límites de los modelos | ✔ | La tesis del proyecto: qué se calcula y qué se pregunta. README §1 |
| Datos sintéticos | ✔ | `samples/legacy-erp.sql` es un dataset sintético creado para no exponer código real |

## 04 · Herramientas

| Contenido | | Dónde |
| --- | --- | --- |
| Claude Code CLI | ✔ | El proyecto se construyó en sesión de pareja con Claude Code. README §8 |
| Revisión de código con IA | ○ | Fase 1: CodeRabbit sobre los PR |
| Code scanning y Dependabot | ◐ | CodeQL analiza en cada push y semanalmente, con 30 análisis registrados, y `dependabot.yml` cubre NuGet, Actions, Terraform y Docker. Queda a medias por dos cosas concretas: las **alertas de seguridad de Dependabot están desactivadas** en la configuración del repositorio, así que solo llegan actualizaciones de versión y no avisos de vulnerabilidad; y hay **5 alertas de CodeQL abiertas sin triar**, 2 de severidad alta |

## 05 · Flujo de desarrollo con IA

| Contenido | | Dónde |
| --- | --- | --- |
| Prompt engineering aplicado a código | ✔ | `Prompts.cs`: rol, restricciones explícitas, formato de salida |
| Prompts con restricciones anti-alucinación | ✔ | «No inventes tablas que no aparezcan en los hechos verificados» |
| **Prompt chaining** | ✔ | Cadena real: documentar cada objeto → los resúmenes alimentan el prompt del plan global |
| Roles y personificación | ✔ | Dos prompts de sistema distintos: documentador y planificador de migración |
| Bases de conocimiento para IA | ✔ | Los hechos verificados por el parser **son** la base de conocimiento que se inyecta |
| Salida estructurada | ✔ | Esquema JSON forzado con `GetResponseAsync<T>` |
| APIs de IA | ✔ | Azure OpenAI vía `Microsoft.Extensions.AI` |
| Multi-proveedor | ◐ | La abstracción `IChatClient` lo permite; solo hay un proveedor conectado |
| `AGENTS.md` / comportamiento del agente | ✔ | `AGENTS.md` en la raíz |
| Skills y subagentes | ◐ | Tres skills en `.claude/skills/` para las tareas que se repiten en este repositorio: añadir un caso de uso, añadir una página localizada y añadir un test. Cada una recoge las trampas propias del proyecto. **Subagentes no**: no hay ninguna tarea aquí que se beneficie de paralelizar en varios contextos |
| **MCP (Model Context Protocol)** | ✔ | Servidor propio en `src/LegacyLens.Mcp` con cuatro herramientas sobre las consultas de la capa de aplicación. Transporte stdio, sin infraestructura añadida. [ADR 0008](adr/0008-servidor-mcp-sobre-la-capa-de-aplicacion.md) |

## 06 · Calidad

| Contenido | | Dónde |
| --- | --- | --- |
| Testing y mapa de pruebas | ◐ | 51 tests: 33 sobre la capa determinista —analizador, riesgo y grafo— y 18 sobre los casos de uso que expone el servidor MCP, incluido el aislamiento entre usuarios. **`Persistence`, `Ai` y `Web` siguen sin ninguno**, así que la cobertura está donde es barata y valiosa, no donde haría falta para hablar de cobertura sin matices |
| Estrategia de qué testear | ✔ | Se testea lo determinista con asserts; lo no determinista se **evalúa con métricas** en `tools/LegacyLens.Evals` |
| **ADR: documentar el porqué** | ✔ | `docs/adr/` |
| Docs as code | ✔ | Toda la documentación en Markdown en el repositorio; el producto **genera** docs-as-code |
| Deuda técnica explícita | ✔ | README §7 «Limitaciones conocidas», sin maquillar |
| Métricas mínimas que importan | ✔ | Tokens y llamadas por modelo, con coste estimado, en la pantalla de resultado y en el informe exportado |
| Coverage honesto | ○ | Fase 1 |
| Quality gates | ✔ | El pipeline de despliegue para en seco si fallan los tests, y exige aprobación manual del plan antes de tocar infraestructura |
| Observabilidad y Release Health | ○ | Fase 3: OpenTelemetry y trazas por llamada al modelo |
| E2E con Playwright | ○ | Fase 3 |
| Internacionalización | ✔ | `es-ES` e `en` en ficheros de recursos, con selector y detección por cabecera. Ningún literal de interfaz en el código |
| Microcopy | ✔ | Los textos se revisaron al extraerlos a recursos: mensajes concretos en lugar de genéricos |
| Accesibilidad | ◐ | Bootstrap accesible de base, `aria` en la barra de progreso; sin auditoría |

## 07 · Infraestructura y Cloud

| Contenido | | Dónde |
| --- | --- | --- |
| DevOps y CI/CD | ✔ | Cinco *workflows*: integración, seguridad, infraestructura, despliegue y Dependabot |
| GitHub Actions | ✔ | Plan de Terraform comentado en el *pull request*, apply del plan guardado, y despliegue verificado con comprobación de que la aplicación responde |
| Despliegue continuo sin secretos | ✔ | OIDC con credenciales federadas y dos identidades de permisos distintos. [ADR 0006](adr/0006-cicd-con-oidc-y-dos-identidades.md) |
| Cloud computing | ✔ | Azure Container Apps, Container Registry, Log Analytics |
| **Infraestructura como código** | ✔ | `Deploy/infra/` completo, con aprovisionamiento por etapas mediante `deploy_app` |
| Costes y mejores prácticas | ✔ | Dos modelos por coste; SKU Basic; una réplica. Ver README §2 |
| Contenerización | ✔ | Dockerfile multi-stage, imagen no-root, 503 MB |
| Orquestación local con Compose | ✔ | `docker-compose.yml`: aplicación y SQL Server con su base de datos y el ERP de ejemplo cargado de forma idempotente |
| Estado de infraestructura gestionado | ✔ | Backend remoto en Azure Storage con versionado, retención y acceso por identidad, no por clave |
| Bases de datos | ✔ | Azure SQL Database serverless con autopausa; un contexto con migraciones aplicadas por el pipeline |
| **Bases de datos vectoriales** | ○ | **Fase 2**, junto con RAG |
| **RAG** | ○ | **Fase 2**: preguntar en lenguaje natural sobre todo el corpus analizado |
| Kubernetes | — | Descartado: Container Apps cubre el caso sin la complejidad operativa |
| **LLMOps** | ◐ | Evaluación reproducible entregada; la observabilidad en producción es fase 3 |

## 08 · Seguridad

| Contenido | | Dónde |
| --- | --- | --- |
| Security by design / by default | ✔ | Sin secretos por diseño: identidad administrada |
| Gestión de credenciales | ✔ | Asignaciones de rol en Terraform; `terraform.tfvars` fuera del repositorio |
| Identificación y autenticación | ✔ | ASP.NET Core Identity; cada usuario ve solo sus análisis |
| Broken Access Control | ✔ | El filtro por propietario está en la consulta del repositorio y en la firma del método, no en una comprobación posterior |
| Injection | ✔ | El SQL analizado **nunca se ejecuta**: se parsea. Es análisis estático puro |
| Componentes vulnerables | ✔ | Detectado y corregido `SQLitePCLRaw` con CVE conocido durante el desarrollo; ahora el CI lo impide de forma automática |
| Costes y su control | ✔ | Coste estimado por análisis, desglosado por modelo, visible en la interfaz |
| Validación de entradas | ◐ | Límite de tamaño y extensión en la subida; sin validación de contenido más profunda |
| DevSecOps en el pipeline | ✔ | `.github/workflows/security.yml`: CodeQL y comprobación de dependencias vulnerables que **falla la compilación**. `dependabot.yml` sobre NuGet, Actions, Terraform y Docker |
| Shift-left security | ✔ | El análisis de seguridad corre en cada *pull request*, no antes de desplegar |
| Mapeo OWASP Top 10 explícito | ✔ | [`docs/seguridad.md`](seguridad.md), las diez categorías de la edición 2025 |
| Logging y monitorización | ◐ | Logging estructurado; sin alertas |

## 09 y 11 · Desarrollo potenciado por IA y masterclass

| Contenido | | Dónde |
| --- | --- | --- |
| Integración de IA en producto real | ✔ | Es el proyecto |
| Tolerancia a fallos del modelo | ✔ | Un objeto que falla no tumba el análisis; sin IA la aplicación sigue siendo útil |
| Caché y control de coste | ✔ | Caché por hash de contenido; concurrencia limitada; modelo económico para el volumen |
| **Evaluación de LLMs** | ✔ | `tools/LegacyLens.Evals`: conjunto dorado, detección automática de alucinación y comparativa entre modelos. Informe en [`docs/evals/informe.md`](evals/informe.md) |
| Fine-tuning | — | Descartado: no hay volumen de datos que lo justifique, y el prompting fundamentado resuelve el caso |
| Docker e IA | ✔ | Aplicación con IA contenerizada y desplegada |
