---
marp: true
theme: default
paginate: true
title: 'Legacy Lens — TFM Máster de Desarrollo con IA'
---

<!--
Presentación en formato Marp. Tres formas de usarla:

  1. Extensión "Marp for VS Code": vista previa y exportación a PDF o HTML.
  2. CLI:  npx @marp-team/marp-cli docs/slides.md --pdf
  3. Copiar y pegar el contenido en Google Slides, una diapositiva por bloque.

Las notas del presentador van en comentarios HTML como este.
-->

# Legacy Lens

### De un script de SQL Server heredado a documentación y un plan de migración

**Nacho Tovar**
Trabajo de Fin de Máster — Máster de Desarrollo con IA

---

## El problema

En muchas empresas la lógica de negocio **no está en el código de la aplicación**.

Está enterrada en procedimientos almacenados escritos hace quince años por gente que ya no
trabaja allí. Sin documentación.

Cuando alguien plantea modernizar el sistema, el primer muro no es técnico:

> **Nadie sabe qué hace ese código, ni por dónde empezar sin romper producción.**

Hoy se resuelve con un consultor leyendo procedimientos a mano durante semanas.

---

## La idea

<br>

# Lo que se puede saber con certeza, se **calcula**.

# Lo que requiere juicio, se le **pregunta al modelo**.

<!--
Esta es la diapositiva central. Si solo se recuerda una cosa de la presentación,
que sea esta.
-->

---

## Por qué esa separación importa

Si a un LLM le pides las dependencias de cincuenta procedimientos, **inventará tablas que
no existen**. Es el uso equivocado de la herramienta.

| | Análisis estático | Modelo de lenguaje |
| --- | --- | --- |
| **Produce** | Dependencias, métricas, riesgo | Resúmenes, reglas, diseño propuesto |
| **Cómo** | Árbol sintáctico real del T-SQL | Prompt con los hechos ya verificados |
| **Garantía** | Exacto y reproducible | Interpretación revisable |

El parser es `Microsoft.SqlServer.TransactSql.ScriptDom`: el mismo que usa SSMS.

**Las dependencias no se infieren. Se leen del AST.**

---

## Qué hace

Se le da un `.sql` generado con *Generate Scripts* de SSMS y devuelve:

- **Inventario** de tablas, vistas, funciones, procedimientos y disparadores
- **Grafo de dependencias** real: qué lee, qué escribe, a quién invoca
- **Riesgo de migración** con el desglose completo de por qué
- **Documentación** de cada objeto en lenguaje de negocio
- **Plan de migración por fases** (patrón *strangler fig*)
- Todo **exportable como Markdown**

---

## Demo

<br>
<br>

### `usp_CerrarPedido` — riesgo 55/100

```
+15  CURSOR             Lógica fila a fila que hay que replantear
+25  NO_TRANSACTION     Escribe en 4 tablas sin transacción explícita
+15  NO_ERROR_HANDLING  Modifica datos sin TRY/CATCH
```

<br>

**Ninguna puntuación es un número suelto.** Tiene que poder discutirse con el cliente.

---

## Cuando el sistema admite lo que no sabe

`usp_InformeVentas` construye la consulta concatenando cadenas.

Sus dependencias reales **no se pueden conocer sin ejecutarlo**.

<br>

> La aplicación lo dice explícitamente, en lugar de fingir que la lista de dependencias
> está completa.

<br>

Un límite del análisis estático que hay que **señalar, no disimular**.

---

## Arquitectura

```
LegacyLens.Domain      Modelos. No conoce a nadie.
LegacyLens.Analysis    ScriptDom. Determinista y testeable.
LegacyLens.Ai          Interpretación. La única parte no determinista.
LegacyLens.Web         Blazor Web App (InteractiveServer)
```

**`Analysis` no depende de `Ai`.**

Si Azure OpenAI falla o no está configurado, el análisis estático se entrega igual y la
aplicación sigue siendo útil.

---

## Dos modelos, dos papeles

| | Documentar objetos | Plan de migración |
| --- | --- | --- |
| Modelo | `gpt-4.1-mini` | `gpt-4o` |
| Llamadas | Una por objeto, en paralelo | **Una sola** |
| Contexto | Un procedimiento | El grafo completo |

Documentar es trabajo repetitivo de contexto corto. El plan es una única decisión que exige
razonar sobre todo el sistema.

**Pagar el modelo grande cincuenta veces no mejora el resultado, solo la factura.**

---

## Lo medí, y me equivocaba

Construí un arnés de evaluación con un conjunto dorado: las reglas de negocio que **sé** que
están en el código.

| Modelo | Cobertura | Inventados | Tokens salida |
| --- | --- | --- | --- |
| `gpt-4.1-mini` | **100 %** | 0 | 2951 |
| `gpt-4o` | 88 % | 0 | 1832 |

**El modelo económico documenta mejor.** `gpt-4o` fue más lacónico y omitió que
`usp_CerrarPedido` puede dejar datos inconsistentes.

Cuando los hechos ya vienen verificados en el prompt, documentar no es razonar: es redactar
sin dejarse nada.

<!--
Cautela que hay que decir en voz alta: una ejecución por modelo, y la medida es por
presencia de términos. No prueba una ley universal; sí convierte una corazonada en un dato.
-->

---

## La alucinación se detecta sola

El parser da el inventario **exacto** del esquema.

> Cualquier objeto cualificado que el modelo mencione y no esté en ese inventario es, por
> definición, inventado.

Sin juicio humano. Sin otro modelo de juez.

**Es la decisión de arquitectura del principio, cobrando intereses.**

---

## Infraestructura como código

```
Terraform  →  Azure OpenAI (2 despliegues de modelo)
              Container Registry
              Container Apps
              Log Analytics
```

<br>

**Sin un solo secreto.** La aplicación llama a OpenAI y lee el registro con su **identidad
administrada**, mediante asignaciones de rol.

Nada que guardar. Nada que rotar.

---

## Que no es una demo con truco

### 15 tests sobre el analizador

- Distingue lecturas de escrituras
- Detecta SQL dinámico en sus dos formas
- No confunde `sp_executesql` con una llamada a procedimiento
- Detecta funciones escalares usadas dentro de expresiones
- La suma de los factores de riesgo siempre cuadra con el total

<br>

Se puede testear con asserts **porque esa mitad del sistema es determinista**.

---

## Cómo lo construí con IA

**Delegué:** la exploración de la API del parser, el script de ejemplo, el código
repetitivo de la interfaz, la primera versión de los prompts.

**Decidí yo:** la separación entre lo calculado y lo interpretado, el modelo de dominio, la
elección de los dos modelos.

**Tuve que corregir a mano dos fallos:**

- El analizador perdía las funciones escalares invocadas en expresiones
- Confundía «objetos a los que nadie llama» con «objetos que no llaman a nadie»
  → órdenes de migración **opuestos**

---

## La conclusión práctica

<br>

### Ninguno de esos dos fallos lo detectó la IA.

### Los detectó el volcado de diagnóstico de los tests.

<br>

> La IA acelera enormemente la parte mecánica.
> Los tests siguen siendo lo que separa **«compila»** de **«funciona»**.

---

## Limitaciones reconocidas

- **El SQL dinámico es un límite infranqueable** del análisis estático
- **Afinidad de sesión**: `azurerm` no expone `stickySessions`; con una réplica no aplica,
  pero hay que resolverlo antes de escalar
- **SQLite efímero**: los análisis se pierden al reiniciar el contenedor
- **La documentación generada hay que revisarla**: es interpretación fundamentada, no
  verdad demostrada

---

## El proyecto no termina aquí

Resuelve un problema que tengo delante en el trabajo. Va a seguir.

| Fase | | |
| --- | --- | --- |
| **0** | Núcleo del producto | **Entregado** |
| **1** | Evaluación de LLM, DevSecOps, coste visible | Siguiente |
| **2** | **Servidor MCP**, **RAG** vectorial, PL/SQL y Delphi | Planificado |
| **3** | OpenTelemetry, Playwright, PostgreSQL | Planificado |
| **4** | Análisis encolado con patrón Outbox | Planificado |

**Descartado a propósito:** microservicios, Kubernetes, *fine-tuning*.
No son pendientes: son decisiones.

<!--
La fase 2 es la que cambia la naturaleza del producto: con un servidor MCP,
deja de ser una herramienta que consultas y pasa a ser contexto que tu agente
tiene mientras migra el código.
-->

---

## Cierre

Legacy Lens **no sustituye** al arquitecto que decide la migración.

Le ahorra las dos primeras semanas de leer procedimientos a mano y le da un mapa con el que
empezar a discutir.

<br>

| | |
| --- | --- |
| Repositorio | _(URL)_ |
| Aplicación | _(URL)_ |
| Usuario de prueba | `demo@legacylens.dev` / `Demo.1234!` |

<br>

### Gracias
