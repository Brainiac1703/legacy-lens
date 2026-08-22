# Alcance y hoja de ruta

## Sobre este documento

Legacy Lens se entrega como Trabajo de Fin de Máster, pero **no termina con la entrega**.
Resuelve un problema que tengo delante en el trabajo, así que va a seguir evolucionando.

Este documento existe para dejar constancia de tres cosas:

1. **Qué está terminado** y se entrega funcionando.
2. **Qué queda fuera del alcance de la entrega, y por qué** — decisiones tomadas por plazo,
   no despistes.
3. **En qué orden se va a continuar**, con criterios de aceptación concretos.

Un proyecto que declara sus límites es más creíble que uno que finge no tenerlos. El
temario del máster insiste en lo mismo cuando habla de deuda técnica explícita y de
*coverage* honesto: lo que no está, se dice.

---

## Fase 0 · Entregado en el TFM

El núcleo del producto, funcionando y desplegado.

- Análisis estático de T-SQL con el parser oficial de Microsoft: inventario de los cinco
  tipos de objeto, grafo de dependencias, métricas y puntuación de riesgo explicable.
- Documentación por objeto y plan de migración por fases generados con IA, con los hechos
  verificados inyectados en el prompt.
- Aplicación web con autenticación, progreso en tiempo real, grafos y exportación del
  paquete de documentación en Markdown.
- Infraestructura como código, contenedor e integración continua.
- 15 tests sobre la capa determinista.

**Criterio de aceptación (cumplido):** analizar `samples/legacy-erp.sql` de principio a fin
y descargar un documento entregable, con la aplicación publicada en una URL pública.

---

## Fase 1 · Rigor: medir en lugar de afirmar

**El objetivo de esta fase es dejar de decir «funciona bien» y poder demostrarlo.**

Es la continuación más urgente, porque hoy la mitad no determinista del sistema no se mide.

### 1.1 Arnés de evaluación de LLM — **ENTREGADO**

Implementado en [`tools/LegacyLens.Evals`](../tools/LegacyLens.Evals). Informe reproducible
en [`evals/informe.md`](evals/informe.md).

**Primer hallazgo, y el motivo por el que valía la pena:** la elección de modelos estaba
tomada por criterio razonable. Al medirla, `gpt-4.1-mini` cubrió el 100 % de las reglas del
conjunto dorado frente al 88 % de `gpt-4o`. El modelo económico documenta **mejor**, no solo
más barato. Detalle y cautelas en el [ADR 0003](adr/0003-dos-modelos-de-lenguaje.md).

Lo que queda por hacer aquí: varias ejecuciones por modelo para separar señal de
variabilidad, y ampliar el conjunto dorado a un segundo script.

<details>
<summary>Especificación original</summary>

Un conjunto dorado de casos sobre el script de ejemplo: para cada procedimiento, las reglas
de negocio que **sabemos** que están en el código. El arnés ejecuta el análisis y mide:

- **Cobertura de reglas**: cuántas de las reglas esperadas aparecen en la salida.
- **Alucinación**: cuántas afirmaciones mencionan objetos que no existen en el esquema
  — comprobable de forma automática contra el inventario del parser, que es la ventaja de
  tener una fuente de verdad.
- **Estabilidad**: varianza entre ejecuciones con el mismo prompt.
- **Comparación entre modelos**: `gpt-4.1-mini` frente a `gpt-4o` frente a `gpt-4o-mini`,
  con coste y latencia al lado de la calidad.

*Criterio de aceptación:* `dotnet run --project tools/LegacyLens.Evals` produce una tabla
comparativa reproducible, y el resultado se publica en el repositorio.

*Por qué primero:* sin esto, cualquier cambio en un prompt es una corazonada. Con esto, los
prompts se pueden optimizar con datos, y la elección de los dos modelos deja de ser un
argumento razonable para convertirse en una decisión medida.

</details>

### 1.2 DevSecOps en el pipeline — **ENTREGADO**

`.github/workflows/security.yml` con CodeQL y comprobación de dependencias vulnerables que
**falla la compilación**. `dependabot.yml` vigilando NuGet, Actions, Terraform y Docker.
Mapeo completo de OWASP Top 10 2025 en [`seguridad.md`](seguridad.md).

<details>
<summary>Especificación original</summary>

- Dependabot para dependencias de NuGet y GitHub Actions.
- CodeQL como análisis estático de seguridad.
- `docs/seguridad.md` con el mapeo explícito de las diez categorías de OWASP Top 10 2025:
  qué aplica, qué no y por qué.

*Criterio de aceptación:* el CI falla ante una dependencia con vulnerabilidad conocida.

</details>

### 1.3 Métricas visibles — **ENTREGADO en su mayor parte**

Tokens de entrada y salida, llamadas y coste estimado, **desglosados por modelo**, en la
pantalla de resultado y en el informe exportado. Los precios están en configuración, y si
falta el de un modelo se muestran los tokens sin importe: es preferible no decir nada a
inventarse una cifra.

*Queda pendiente:* publicar el *coverage* real en el CI.

---

## Fase 2 · Ampliar lo que la herramienta sabe hacer

### 2.1 Servidor MCP

Exponer el análisis como herramientas de Model Context Protocol, para que cualquier agente
—Claude Code, Copilot, un agente propio— pueda consultar la base de conocimiento del
sistema heredado mientras escribe el código de la migración:

- `buscar_objeto(nombre)` → ficha completa con hechos verificados
- `dependencias_de(objeto, direccion)` → el subgrafo
- `donde_se_usa(tabla)` → qué tocaría al cambiar una tabla
- `riesgo_de_cambiar(objeto)` → radio de impacto

*Por qué importa:* cambia la naturaleza del producto. Deja de ser una herramienta que
consultas y pasa a ser **contexto que tu agente tiene mientras migra**. Es el paso de
«documentación» a «infraestructura de conocimiento».

*Criterio de aceptación:* migrar un procedimiento real con Claude Code conectado al
servidor MCP, sin abrir la aplicación web.

### 2.2 RAG sobre el corpus analizado

Indexar en una base vectorial las fichas generadas y el código fuente troceado, para
responder preguntas que hoy no se pueden hacer: *«¿dónde se calcula el descuento de un
cliente?»*, *«¿qué procedimientos tocan la tabla de facturas?»*, *«¿hay lógica de
impuestos duplicada?»*.

*Criterio de aceptación:* responder correctamente a diez preguntas de un conjunto de
prueba, citando siempre el objeto de origen.

### 2.3 Más dialectos

El diseño ya separa el análisis del resto, así que añadir un dialecto es añadir un
analizador. Por orden de utilidad real:

1. **Oracle PL/SQL** — el otro gran depósito de lógica de negocio heredada.
2. **Delphi / Object Pascal** — el caso que originó la idea. Necesita un parser propio con
   ANTLR, y es la razón por la que no entró en la entrega.

*Criterio de aceptación:* el mismo informe, con el mismo formato, a partir de un script de
otro dialecto.

### 2.4 Skills y subagentes

Empaquetar el flujo de migración como Skills reutilizables, y usar subagentes para
paralelizar el análisis de bases de datos grandes: un agente por área funcional del
esquema, con un agente que consolida.

---

## Fase 3 · Que aguante uso real

### 3.1 Observabilidad

OpenTelemetry con trazas por llamada al modelo: latencia, tokens, coste y tasa de fallo por
despliegue. Es lo que convierte la fase 1 en algo continuo en lugar de una foto.

### 3.2 Migrar el proyecto de tests a Microsoft.Testing Platform

`xunit.v3` 4.x abandona VSTest, así que la actualización dejó de ser un cambio de versión y
pasó a ser una migración: quitar `xunit.runner.visualstudio` y `Microsoft.NET.Test.Sdk`, y
activar la nueva experiencia de `dotnet test`. Hasta hacerla, el mayor de `xunit.v3` está
bloqueado en `dependabot.yml` para no arrastrar una propuesta en rojo permanente.

*Criterio de aceptación:* los 15 tests siguen pasando y el CI no necesita `--no-build` ni
banderas de compatibilidad.

### 3.3 Pruebas de extremo a extremo

Playwright sobre el recorrido completo: iniciar sesión, analizar el ejemplo, comprobar que
el plan aparece y descargar el documento.

### 3.4 Persistencia y escalado de verdad

- ~~Base de datos servidor en lugar de SQLite~~ **entregado**: Azure SQL Database serverless,
  con las migraciones aplicadas por el pipeline antes de publicar la revisión nueva.
- Afinidad de sesión con el provider `azapi`, requisito para pasar de una réplica con
  Blazor Server.
- ~~Estado de Terraform en un backend remoto con bloqueo~~ **entregado**: Azure Storage con
  versionado y retención de borrados.

---

## Fase 4 · Escala

Solo si el uso lo justifica. Se documenta para dejar claro que el camino está pensado, no
para prometerlo.

- **Análisis asíncrono encolado.** Una base de datos con dos mil procedimientos no se
  analiza dentro de una petición web. Cola de trabajos con reintentos, y **patrón Outbox**
  para publicar el evento «análisis terminado» sin perder consistencia.
- **Comparación temporal.** Analizar la misma base de datos cada mes y ver qué riesgo sube:
  la migración como algo medible en el tiempo, no como una foto.
- **Multi-tenant** con aislamiento real, si esto llegara a usarse con varios clientes.

---

## Lo que se ha descartado a propósito

No todo lo que se puede hacer merece hacerse. Estas decisiones no son pendientes:

| Descartado | Por qué |
| --- | --- |
| **Microservicios** | No hay ningún problema de escalado, despliegue independiente ni equipos separados que lo justifique. Un monolito modular bien delimitado es la respuesta correcta aquí. |
| **Kubernetes** | Container Apps resuelve el caso sin cargar con la operación de un clúster. |
| **Fine-tuning** | No hay volumen de datos etiquetados que lo justifique, y el problema real se resuelve mejor fundamentando el prompt con hechos verificados que ajustando pesos. |
| **Ejecutar el SQL analizado** | El producto analiza, nunca ejecuta. Es la garantía de seguridad más fuerte que puede dar y no se va a debilitar por conseguir más precisión. |
