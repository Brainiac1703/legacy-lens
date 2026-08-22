---
marp: true
theme: default
paginate: true
title: 'Legacy Lens — TFM Máster de Desarrollo con IA'
---

<!--
Presentación en formato Marp. Tres formas de usarla:

  1. Extensión "Marp for VS Code": vista previa y exportación a PDF o HTML.
  2. CLI, que es como se generó el PDF entregado. Hacen falta las dos cosas:
     la ruta a un Chrome o Edge instalado y --allow-local-files, porque las
     diapositivas cargan recursos del propio repositorio.

       set CHROME_PATH=C:\Program Files\Google\Chrome\Application\chrome.exe
       npx @marp-team/marp-cli@4.5.0 docs/slides.md --pdf --allow-local-files -o docs/slides.pdf

  3. Google Slides importa .pptx, así que si se prefiere ese formato el camino
     corto es exportar con --pptx --pptx-editable e importarlo, en lugar de
     copiar y pegar diapositiva a diapositiva.

  El PDF está versionado a propósito: el requisito del TFM admite «documento
  adjunto junto al código», y GitHub renderiza PDF en el navegador, así que su
  URL sirve además como enlace público a la presentación. Si se edita este
  fichero, hay que regenerarlo.

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

### `usp_CerrarPedido` — riesgo 55/100

```
+15  CURSOR             Lógica fila a fila que hay que replantear
+25  NO_TRANSACTION     Escribe en 4 tablas sin transacción explícita
+15  NO_ERROR_HANDLING  Modifica datos sin TRY/CATCH
```

<br>

**Ninguna puntuación es un número suelto.** Tiene que poder discutirse con el cliente.

---

## Dos ejemplos, dos formas de estar mal

| | Riesgo máximo | Dónde está la deuda |
| --- | --- | --- |
| ERP de facturación | **55** · alto | cursores, SQL dinámico, transacciones |
| Almacén y expediciones | **80** · crítico | proceso por etapas, lógica repartida, cero garantías |

<br>

El segundo no tiene **ni un solo cursor**. Con un único ejemplo la herramienta parecía
medir siempre lo mismo.

<!--
El de almacén es el proceso nocturno que nadie se atreve a tocar: cinco tablas
temporales, doce parámetros, seis procedimientos en cadena, once tablas, sin
transacción y sin TRY/CATCH. Su riesgo son siete factores distintos.
-->

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
Domain          entidades y recorridos del grafo. No conoce a nadie.
Application     casos de uso, puertos y behaviours (CQRS con MediatR)
Persistence.EF  ─┐
Analysis         ├─ adaptadores: implementan los puertos
Ai              ─┘
Web             presentación. Solo ISender.
Mcp             servidor MCP. Solo traduce a MediatR.
```

Las dependencias apuntan **siempre hacia dentro**. Y `Analysis` no depende de `Ai`.

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
              Azure SQL Database (serverless, solo Entra)
              Log Analytics
```

<br>

**La aplicación no tiene ningún secreto.** Llama a OpenAI, lee el registro y entra en la base
de datos con su **identidad administrada**. El servidor SQL no admite usuario y contraseña.

Un único secreto en todo el proyecto, y está razonado: la clave de la cuenta que guarda el
estado de Terraform, que vive en otra suscripción donde no se pueden asignar roles.

<!--
Decirlo así y no «sin un solo secreto» es deliberado: es lo que el ADR 0006
documenta, y la diferencia entre una afirmación defendible y una que se cae en
cuanto alguien abre el fichero de secretos del repositorio.

La identidad tuvo que pasar a asignada por el usuario: una de sistema no existe
hasta que el recurso está creado, y Azure no termina de crear el Container App
hasta poder autenticarse contra el registro, que necesita ese permiso. El ciclo
se cierra y el despliegue se queda esperando sin error. ADR 0005.
-->

---

## Que no es una demo con truco

### 33 tests sobre las partes deterministas

- Distingue lecturas de escrituras
- Detecta SQL dinámico en sus dos formas
- No confunde `sp_executesql` con una llamada a procedimiento
- Detecta funciones escalares usadas dentro de expresiones
- La suma de los factores de riesgo siempre cuadra con el total
- El recorrido del grafo aguanta **ciclos**: dos procedimientos que se llaman
  mutuamente colgarían un recorrido ingenuo

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

## El análisis, dentro de tu agente

Servidor **MCP** sobre las mismas consultas de la capa de aplicación.

| Herramienta | Responde a |
| --- | --- |
| `list_analyses` | qué sistemas hay analizados |
| `find_object` | qué hace esto, cuánto riesgo tiene, de qué depende |
| `where_used` | quién toca esta tabla, y si la lee o la escribe |
| `change_risk` | qué se rompe si lo cambio, y qué migrar antes |

<br>

Deja de ser una herramienta que **consultas** y pasa a ser contexto que tu agente **tiene
mientras migra**.

<!--
Sin infraestructura añadida: lee la misma base de datos y no gasta ni una llamada
al modelo. El servidor no tiene lógica; si apareciera, estaría en el sitio
equivocado. Las respuestas separan lo calculado de lo interpretado, igual que la
web, porque quien las recibe necesita saber en qué puede confiar sin verificar.
-->

---

## Limitaciones reconocidas

- **El SQL dinámico es un límite infranqueable** del análisis estático
- **Afinidad de sesión**: `azurerm` no expone `stickySessions`; con una réplica no aplica,
  pero hay que resolverlo antes de escalar
- **Una réplica**: el circuito de Blazor Server tiene estado y escalar exige afinidad de sesión
- **La documentación generada hay que revisarla**: es interpretación fundamentada, no
  verdad demostrada

---

## El proyecto no termina aquí

Resuelve un problema que tengo delante en el trabajo. Va a seguir.

| Fase | | |
| --- | --- | --- |
| **0** | Núcleo del producto | **Entregado** |
| **1** | Evaluación de LLM, DevSecOps, coste visible | **Entregado** |
| **2** | **Servidor MCP** | **Entregado** |
| **2** | **RAG** vectorial, PL/SQL y Delphi | Planificado |
| **3** | OpenTelemetry, Playwright, PostgreSQL | Planificado |
| **4** | Análisis encolado con patrón Outbox | Planificado |

**Descartado a propósito:** microservicios, Kubernetes, *fine-tuning*.
No son pendientes: son decisiones.

<!--
Las fases 1 y 2.1 se entregaron durante el desarrollo, y merece decir qué
enseñaron. La evaluación tumbó una decisión que creía tomada por buen criterio:
el modelo económico documenta mejor. Y el servidor MCP trajo tres fallos que no
se ven compilando, entre ellos que en stdio la salida estándar ES el canal del
protocolo, así que un log ahí lo corrompe en silencio.
-->

---

## Cierre

Legacy Lens **no sustituye** al arquitecto que decide la migración.

Le ahorra las dos primeras semanas de leer procedimientos a mano y le da un mapa con el que
empezar a discutir.

<br>

| | |
| --- | --- |
| Repositorio | `github.com/Brainiac1703/legacy-lens` |
| Aplicación | `https://ca-legacylens-tfm.bluedesert-728dc156.francecentral.azurecontainerapps.io` |
| Usuario de prueba | `demo@legacylens.dev` / `Demo.1234!` |

<br>

### Gracias
