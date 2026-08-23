# Guion del vídeo — Legacy Lens

**Duración objetivo: 9 minutos.** Es obligatorio capturar la pantalla; la cámara es
opcional.

## Antes de grabar (10 minutos de preparación que ahorran tres tomas)

- [ ] Ejecuta el análisis de **los dos ejemplos una vez antes de grabar**. Así la caché está
      caliente y en la toma real no esperas a las llamadas al modelo. Deja los dos análisis
      hechos en «Mis análisis» como red de seguridad por si la demo en vivo falla.
- [ ] Ten abiertas y ordenadas estas pestañas: la app desplegada, el repositorio en GitHub,
      `samples/legacy-erp.sql`, `samples/legacy-almacen.sql` y `TSqlAnalyzerTests.cs`.
- [ ] Deja el servidor MCP construido y dado de alta en tu cliente, con una sesión de agente
      abierta y probada. Es la parte más frágil de la demo porque depende de otro programa.
- [ ] Sube el zoom del navegador al 125 % y el del editor a un tamaño legible en vídeo.
      Lo que se lee bien en tu monitor no se lee en un vídeo comprimido.
- [ ] Silencia notificaciones de Teams, Slack y correo.
- [ ] Ten una terminal lista con `dotnet test` sin ejecutar.
- [ ] Nada que preparar para la verificación contra el catálogo: se cuenta de viva voz sobre
      la pantalla que ya estés enseñando. La consulta queda en `docs/verificacion-grafo.sql`
      para quien quiera reproducirla.

---

## 0:00 – 0:20 · Apertura (diapositivas 1 y 2)

> «Hola, muy buenas. Soy Nacho Tovar, arquitecto de software en Pronet-ISE, y os presento
> Legacy Lens, mi trabajo de fin de máster.
>
> Diseño y evoluciono productos de gestión empresarial con .NET y Azure, y digo esto porque
> Legacy Lens no es un ejercicio de clase: resuelve una parte de mi trabajo. Decidir cómo se
> migra un sistema con la lógica de negocio enterrada en procedimientos almacenados es
> exactamente lo que se le pide a un arquitecto, y hoy se hace leyendo código a mano durante
> semanas.
>
> Quería un TFM que usara lo que sé hacer y que además me obligara a lo que vine a aprender:
> dónde encaja un modelo de lenguaje y, sobre todo, dónde no.»

## 0:20 – 0:45 · El problema (sin tocar la aplicación todavía)

Abre `samples/legacy-erp.sql` y baja hasta `usp_CerrarPedido`.

> «Esto es uno de esos procedimientos. Ochenta y nueve líneas. Cierra un pedido: valida
> crédito, genera la factura, descuenta el stock. La lógica de negocio de la empresa está aquí
> dentro, no en el código de la aplicación.
>
> Ahora imagina cuarenta como este, escritos hace quince años por gente que ya no está, y que
> te piden migrarlos a .NET. El primer problema no es técnico: es que nadie sabe qué hace este
> código ni por dónde empezar sin romper producción. Legacy Lens automatiza ese primer paso.»

## 0:45 – 1:40 · La decisión de diseño

Ve a la página de inicio de la aplicación, al recuadro azul.

> «Antes de la demo quiero explicar la decisión que sostiene todo el proyecto, porque es lo
> que lo diferencia de un chat sobre documentos.
>
> Si le pides a un modelo de lenguaje que te diga las dependencias de cincuenta
> procedimientos, se va a inventar tablas que no existen. Es el uso equivocado de la
> herramienta.
>
> Así que la regla aquí es: **lo que se puede saber con certeza, se calcula; lo que requiere
> juicio, se le pregunta al modelo.**
>
> Las dependencias, las métricas y el riesgo salen del árbol sintáctico real del SQL, con el
> parser oficial de Microsoft, el mismo que usa Management Studio. Son exactos. El modelo
> solo hace lo que un parser jamás podrá — entender la intención de negocio y proponer un
> diseño — y lo hace ya alimentado con esos hechos verificados.»

## 1:40 – 3:15 · La demo

En la página de análisis hay dos ejemplos. Pulsa **«Analizar»** en el **ERP de facturación**.
Deja que se vea el progreso.

> «Fase uno, análisis estático: instantáneo. Fase dos, documentación: una llamada por
> objeto, en paralelo. Fase tres, el plan de migración: una sola llamada.»

Cuando termine, recorre las tarjetas del resumen.

> «Diecinueve objetos, veintiuna dependencias detectadas, y de los ocho objetos programables
> uno está en riesgo alto. Ese es el que vamos a mirar.»

**Pestaña Plan.** Lee el diagnóstico general y una fase.

> «Fíjate en el orden: primero lo autocontenido, al final los nudos de los que depende medio
> sistema. Es una migración por estrangulamiento: se va sustituyendo el sistema por partes,
> por fuera, y el viejo se queda sin trabajo hasta que se puede apagar. Y lo puede ordenar así
> porque conoce el grafo real.»

**Pestaña Grafo.** Cambia entre las dos vistas.

> «El color es el riesgo. Y estas aristas no son una opinión del modelo: salen del árbol de
> sintaxis que construye el propio analizador de SQL Server antes de ejecutar nada. No busco
> texto, leo la gramática del lenguaje: una tabla nombrada en un comentario no genera
> dependencia, y una lectura escondida en un `JOIN` anidado sí aparece.»

> **Y qué es el árbol de sintaxis**, en una frase: cuando SQL Server recibe
> un script, antes de ejecutarlo lo convierte en un árbol donde cada sentencia, cada tabla y
> cada columna es un nodo, y así deja de ser texto y pasa a tener estructura. Legacy Lens usa
> ese mismo analizador —`ScriptDom`, el que publica Microsoft—, así que ve el script igual que
> lo ve el motor.

## 3:15 – 4:35 · El momento fuerte: riesgo explicable

**Pestaña Objetos**, despliega `usp_CerrarPedido`.

> «Aquí está el procedimiento del principio. El modelo ha entendido qué hace y ha extraído
> las reglas de negocio implícitas: por ejemplo, que un pedido no se puede cerrar si el
> cliente tiene facturas vencidas hace más de sesenta días. Eso estaba enterrado en un
> `IF` a mitad del código.
>
> Pero lo que más me interesa enseñar es esto.»

Baja hasta *«De dónde sale la puntuación»*.

> «Riesgo 55. Y no es un número que se haya inventado nadie: son quince puntos por el
> cursor, veinticinco porque escribe en cuatro tablas sin una sola transacción explícita, y
> quince por modificar datos sin TRY/CATCH.
>
> Esto importa porque una puntuación de riesgo tienes que poder discutirla con el cliente. Si
> sale de una caja negra, no la puedes defender en una reunión.»

Baja a `usp_InformeVentas`.

> «Y este caso me gusta aún más, porque es donde el sistema **admite lo que no sabe**. Este
> procedimiento construye la consulta concatenando cadenas, así que sus dependencias reales
> no se pueden conocer sin ejecutarlo. En lugar de fingir que la lista está completa, lo
> dice. Un límite del análisis estático que hay que señalar, no disimular.»

## 4:35 – 5:00 · Que no mide siempre lo mismo

Abre el análisis ya hecho del segundo ejemplo, **Almacén y expediciones**, y ve a
`usp_ConsolidarExpediciones`.

> «Con un solo ejemplo la herramienta parecería medir siempre lo mismo, así que hay un
> segundo con la deuda en otro sitio. Aquí no hay **ni un solo cursor**.
>
> Es el proceso nocturno que nadie se atreve a tocar: cinco tablas temporales como etapas,
> doce parámetros, la lógica repartida entre seis procedimientos que se llaman en cadena, y
> once tablas tocadas sin transacción y sin manejo de errores. Riesgo 80, crítico, con siete
> factores distintos.
>
> Mismo analizador, diagnóstico completamente distinto.»

## 5:00 – 6:05 · Que no es una demo con truco

Cambia a la terminal y ejecuta `dotnet test`.

> «Treinta y tres tests sobre las partes deterministas. Verifican que distingue lecturas de
> escrituras, que detecta el SQL dinámico en sus dos formas, que no confunde `sp_executesql`
> con una llamada a procedimiento, que la suma de los factores de riesgo cuadra con el total,
> y que el recorrido del grafo aguanta ciclos: dos procedimientos que se llaman mutuamente
> colgarían un recorrido ingenuo, y en un sistema heredado eso no es una rareza.
>
> Se puede testear con asserts precisamente porque esa parte es determinista. Es la otra cara
> de la decisión de diseño del principio: al separar lo calculado de lo interpretado, la
> mitad del sistema se vuelve verificable.»

Abre `docs/evals/informe.md`.

> «Y para la otra mitad, la que genera el modelo, los asserts no sirven. Así que hay un arnés
> de evaluación con un conjunto dorado: las reglas de negocio que **sé** que están en el
> código, porque el script de ejemplo lo escribí yo.
>
> Mide cobertura de reglas, si advierte del SQL dinámico donde debe, y objetos inventados — y
> esta última se detecta **sola**: como el parser me da el inventario exacto del esquema,
> cualquier objeto que el modelo mencione y no esté ahí es inventado por definición. Sin
> juicio humano y sin otro modelo de juez.
>
> Y aquí me llevé una sorpresa. Yo había elegido el modelo económico para documentar por
> coste, dando por hecho que perdía algo de calidad. Al medirlo, `gpt-4.1-mini` cubre el cien
> por cien de las reglas y `gpt-4o` el ochenta y ocho: se dejó que el procedimiento crítico
> puede dejar datos inconsistentes. El modelo pequeño documenta mejor.
>
> Dicho con honestidad: es una ejecución por modelo y la medida es por presencia de términos.
> No demuestro una ley universal. Pero he convertido una corazonada en un dato.»

Sin cambiar de pantalla: esto se dice, no se enseña. La consulta que lo comprueba está en
`docs/verificacion-grafo.sql` y cualquiera puede ejecutarla contra el compose, así que el
comprobante vive en el repositorio y no te cuesta ni una toma.

> «Y una última comprobación, la más incómoda para mí. SQL Server ya trae su propio grafo de
> dependencias, en `sys.sql_expression_dependencies`. Si mi analizador sobra, se nota aquí. Lo
> he medido contra el mismo esquema: el catálogo da veintiuna dependencias reales, y mi
> analizador da las mismas veintiuna.
>
> Y eso es lo que quería demostrar, no lo contrario: esa parte es un hecho, así que se calcula
> y se puede verificar contra el propio motor. Si la hubiera pedido a un modelo, no podría
> deciros si son veintiuna o diecinueve.
>
> Con una diferencia que sí importa. Estos dos procedimientos construyen SQL dinámico, y el
> catálogo devuelve cero dependencias para ellos: indistinguible de «no depende de nada». En un
> plan de migración los pondrías en la primera fase por autocontenidos, cuando en realidad
> tocan cuatro tablas. Legacy Lens tampoco puede verlas, pero puntúa esa ceguera con cuarenta
> puntos y la escribe. El catálogo calla; la herramienta avisa de lo que no sabe.»

## 6:05 – 6:55 · El análisis dentro del agente

Cambia a tu cliente de agente, con el servidor MCP ya dado de alta.

> «Y esto es lo que más ha cambiado el proyecto. Todo lo que acabas de ver vive en una página
> web, pero el trabajo real de migrar no se hace en una página web: se hace en un editor, con
> un agente al lado que **no sabe nada del sistema heredado**.
>
> Así que el análisis se expone como servidor MCP.»

Pide al agente algo concreto, por ejemplo: *«¿qué se rompe si cambio `usp_CerrarPedido`?»*

> «Cuatro herramientas, que son las cuatro preguntas que uno se hace de verdad antes de tocar
> un sistema heredado: qué hace esto, quién toca esta tabla, qué se rompe si lo cambio, y qué
> habría que migrar antes.
>
> Y no gasta ni una llamada al modelo ni un recurso nuevo: lee la misma base de datos y
> reutiliza las mismas consultas de la capa de aplicación que usa la web. El servidor no
> tiene lógica; si la tuviera, estaría en el sitio equivocado.
>
> Deja de ser una herramienta que consultas y pasa a ser el contexto que tu agente tiene
> mientras escribe la migración.»

## 6:55 – 8:05 · Arquitectura e infraestructura

Abre la estructura del repositorio.

> «Siete proyectos, con las dependencias apuntando siempre hacia dentro. `Domain` no conoce a
> nadie. `Application` tiene los casos de uso y los puertos, con CQRS sobre MediatR.
> `Persistence`, `Analysis` y `Ai` son adaptadores que implementan esos puertos. La web solo
> conoce `ISender`, y el servidor MCP tampoco conoce nada más.
>
> Y algo importante: **`Analysis` no depende de `Ai`**. Por eso, si Azure OpenAI se cae o no
> está configurado, el análisis estático se sigue entregando y la aplicación sigue siendo
> útil.»

Abre `Deploy/infra/`.

> «La infraestructura es Terraform: Azure OpenAI con sus dos despliegues de modelo, el
> registro de contenedores, el Container App y la base de datos. Y no hay ni un secreto: la
> aplicación llama a OpenAI, lee el registro y entra en la base de datos con su identidad
> administrada.
>
> Eso último costó más de lo que parece: hubo que pasar a una identidad asignada por el
> usuario para romper una dependencia circular entre el Container App y el registro. Está
> contado en el ADR 0005.»

Abre `variables.tf` en los dos modelos.

> «Dos modelos con papeles distintos. Documentar cincuenta objetos es trabajo repetitivo y
> de contexto corto, así que va con el modelo económico. El plan de migración es una sola
> decisión que necesita ver el grafo entero, y ahí compensa el modelo capaz. Pagar el grande
> cincuenta veces no habría mejorado el resultado, solo la factura.»

## 8:05 – 8:45 · Cómo lo construí con IA

> «Como es un máster de desarrollo con IA, digo también cómo se hizo.
>
> Delegué la exploración de la API del parser, que es enorme y verbosa, los scripts de
> ejemplo y el código repetitivo de la interfaz. Decidí yo la separación entre lo determinista
> y lo interpretado, el modelo de dominio y la elección de los dos modelos.
>
> Y hubo fallos que tuve que corregir a mano. El analizador perdía las funciones escalares
> invocadas dentro de expresiones, porque no se llaman con `EXEC`. La primera versión
> confundía «objetos a los que nadie llama» con «objetos que no llaman a nadie», que llevan a
> órdenes de migración opuestos. Y en el servidor MCP, los logs iban a la salida estándar,
> que en ese protocolo **es el canal de mensajes**: el servidor no respondía y no había
> ningún error que mirar.
>
> Ninguno de los tres lo detectó la IA. Los dos primeros los detectó el volcado de
> diagnóstico de los tests, y el tercero, arrancar el servidor y hablarle. Esa es mi
> conclusión práctica del máster: la IA acelera muchísimo la parte mecánica, y lo que separa
> "compila" de "funciona" sigue siendo ejecutarlo.»

## 8:45 – 8:55 · Que el proyecto continúa

Abre `docs/hoja-de-ruta.md`.

> «Durante el desarrollo se entregaron la fase uno —evaluación y DevSecOps— y el servidor MCP
> de la fase dos. Lo que queda está planificado, no olvidado: RAG sobre el corpus, más
> dialectos, observabilidad y análisis encolado.
>
> Y hay cosas descartadas a propósito: microservicios, Kubernetes y fine-tuning. No son
> pendientes, son decisiones, y están razonadas.»

## 8:55 – 9:00 · Cierre

> «Legacy Lens no sustituye al arquitecto que decide la migración. Le ahorra las dos primeras
> semanas de leer procedimientos a mano y le da un mapa con el que empezar a discutir.
>
> Gracias.»

---

## Errores que evitar

- **No leas esto palabra por palabra.** Ten los puntos delante y habla.
- **Si la demo en vivo falla, no la repares en cámara.** Di «tengo un análisis ya hecho» y
  abre el de «Mis análisis». Se ve profesional, no lo contrario.
- **El tramo del MCP es el más frágil**, porque depende de otro programa y de una
  configuración que vive fuera del repositorio. Si el agente no responde, no lo depures en
  cámara: enseña la tabla de herramientas en las diapositivas y sigue.
- **No prometas lo que no hace.** La sección de limitaciones del README es un punto a favor,
  no algo que esconder. Si mencionas el SQL dinámico como límite reconocido, ganas
  credibilidad.
- **Comprueba el audio en los primeros diez segundos** de la primera toma antes de grabar
  nueve minutos sin sonido.
