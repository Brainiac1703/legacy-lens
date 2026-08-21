# ADR 0003 · Dos modelos de lenguaje con papeles distintos

**Estado:** aceptada

## Contexto

La capa de IA hace dos trabajos que solo se parecen en que ambos llaman a un modelo:

**Documentar cada objeto.** Una llamada por procedimiento. El contexto es corto —un
procedimiento y sus hechos verificados— y la tarea es acotada: resumir, extraer reglas,
proponer un destino. En una base de datos real son entre cincuenta y varios cientos de
llamadas.

**Generar el plan de migración.** Una sola llamada. El contexto es el inventario completo
con el grafo de dependencias, y la tarea exige razonar sobre el conjunto: ordenar fases sin
colocar un objeto antes que sus propias dependencias.

## Decisión

Dos despliegues con papeles separados:

| | Documentación | Plan |
| --- | --- | --- |
| Modelo | `gpt-4.1-mini` | `gpt-4o` |
| Llamadas por análisis | Una por objeto | Una |
| Contexto | Un objeto | El grafo completo |

La documentación se procesa en paralelo con concurrencia limitada. El plan se genera
**después**, para que su prompt pueda incluir los resúmenes ya producidos: razonar sobre el
sistema completo funciona mejor sabiendo qué hace cada pieza.

## Consecuencias

**A favor:**

- El coste de un análisis baja de forma sustancial sin perder calidad donde importa. El
  trabajo repetitivo va al modelo económico; el juicio global, al capaz.
- La cuota disponible se aprovecha mejor: en la región elegida `gpt-4.1-mini` tiene una
  cuota mucho más alta, que es justo la que necesita el trabajo de volumen.
- Los papeles quedan explícitos en la configuración, así que cambiar un modelo no obliga a
  tocar código.

**En contra:**

- Dos despliegues que aprovisionar y mantener en Terraform.

## Medición posterior

La decisión se tomó por criterio y después se midió con el arnés de
[`tools/LegacyLens.Evals`](../../tools/LegacyLens.Evals). El resultado no fue el esperado:

| Modelo | Cobertura de reglas | Objetos inventados | Tokens salida | Segundos |
| --- | --- | --- | --- | --- |
| `gpt-4.1-mini` | **16/16 (100 %)** | 0 | 2951 | 17,3 |
| `gpt-4o` | 14/16 (88 %) | 0 | 1832 | 9,1 |

**El modelo económico documentó mejor que el capaz.** `gpt-4o` produjo texto más lacónico y
omitió dos cosas relevantes: que `usp_CerrarPedido` puede dejar datos inconsistentes al
escribir sin transacción, y que el proceso nocturno recorre los pedidos uno a uno.

La lectura razonable es que documentar un procedimiento **no es una tarea de razonamiento**
cuando los hechos ya vienen verificados en el prompt: es una tarea de redacción exhaustiva, y
ahí la verbosidad del modelo pequeño juega a favor. Refuerza la decisión original por un
motivo distinto del previsto: no es un compromiso entre coste y calidad, es que para esta
tarea concreta el modelo económico es además el mejor.

**Cautelas honestas sobre esta medición:**

- `gpt-4.1-mini` se ha ejecutado tres veces con el mismo resultado (16/16), lo que da cierta
  confianza en su estabilidad. De `gpt-4o` solo hay **una ejecución**, así que su 88 % podría
  ser variabilidad y no una diferencia real. Es la primera cosa que hay que ampliar.
- La cobertura se mide por presencia de términos alternativos, no por juicio semántico. Es
  una medida de ausencias, no de calidad de la redacción; la lectura del informe completo
  sigue siendo necesaria.
- El conjunto dorado cubre un único script, escrito por nosotros. No prueba nada sobre bases
  de datos reales de terceros.

Nada de esto invalida el resultado, pero sí acota lo que se puede afirmar con él. Aumentar
el número de ejecuciones y ampliar el conjunto dorado son los siguientes pasos naturales.

## Alternativas consideradas

**Un solo modelo capaz para todo.** Simplifica la infraestructura, pero multiplica el coste
del trabajo de volumen sin evidencia de que mejore un resumen de un procedimiento. Pagar el
modelo grande cincuenta veces para tareas de contexto corto es gasto, no calidad.

**Un solo modelo económico para todo.** El plan de migración es la salida con más valor del
producto y la que más se juega si razona mal. No es el sitio donde ahorrar.

**Enviar todos los objetos en una única llamada con contexto largo.** Habría ahorrado
llamadas, pero impide el procesamiento en paralelo, elimina la caché por objeto y hace que
un fallo se lleve el análisis completo. La granularidad por objeto es lo que permite la
tolerancia a fallos.
