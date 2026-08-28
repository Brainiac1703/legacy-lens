# ADR 0001 · Separar lo calculado de lo interpretado

**Estado:** aceptada

## Contexto

El producto tiene que responder a dos preguntas de naturaleza distinta sobre un script de
SQL Server heredado:

1. ¿Qué tablas toca este procedimiento y a quién invoca?
2. ¿Qué hace en términos de negocio y en qué debería convertirse?

La primera tiene una respuesta exacta y comprobable. La segunda requiere juicio.

La vía rápida era pasarle el script completo a un modelo de lenguaje y pedirle las dos cosas
a la vez. Se descartó tras comprobar el problema conocido: al pedirle dependencias de varios
procedimientos, el modelo produce tablas que no existen en el esquema. Y una dependencia
inventada en un plan de migración no es un detalle estético: lleva a planificar mal el orden
de migración de un sistema en producción.

## Decisión

Se separan las dos preguntas en dos capas con garantías distintas.

**Lo verificable se calcula** recorriendo el árbol sintáctico real del T-SQL con
`Microsoft.SqlServer.TransactSql.ScriptDom`, el parser oficial de Microsoft. De ahí salen
las dependencias, las métricas y la puntuación de riesgo. Es exacto y reproducible.

**Lo interpretable se pregunta al modelo**, pero nunca en vacío: el prompt incluye los
hechos ya verificados y prohíbe explícitamente introducir objetos que no aparezcan en ellos.

La frontera se refleja en la estructura del código: `LegacyLens.Analysis` **no depende** de
`LegacyLens.Ai`.

## Consecuencias

**A favor:**

- Las dependencias del grafo no pueden ser falsas. Salen del AST.
- La mitad del sistema se vuelve **testeable con asserts**, porque es determinista. De ahí
  salen los 51 tests.
- La aplicación sigue siendo útil sin IA: si Azure OpenAI falla o no está configurado, se
  entrega el análisis estático completo.
- El modelo trabaja mejor: recibe contexto verificado en lugar de tener que deducir
  estructura.
- Permite una comprobación automática de alucinación: cualquier objeto que el modelo
  mencione se puede validar contra el inventario del parser. Es la base del arnés de
  evaluación de la fase 1.

**En contra:**

- Hay que aprender y mantener el uso de ScriptDom, cuya API de visitantes es verbosa.
- Existen dependencias que el parser no puede ver —el SQL dinámico— y hay que comunicarlas
  como límite en lugar de rellenarlas.

## Alternativas consideradas

**Todo al modelo.** Descartada por lo dicho: dependencias inventadas, resultado no
reproducible y nada que se pueda testear.

**Todo con el parser.** Habría dado un grafo perfecto y ninguna comprensión. Un grafo de
dependencias sin explicación no resuelve el problema real, que es de conocimiento.

**Parser primero y modelo para verificar su salida.** Invierte los papeles y deja al modelo
juzgando algo que ya es exacto: gasto sin ganancia.
