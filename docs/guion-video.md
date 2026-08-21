# Guion del vídeo — Legacy Lens

**Duración objetivo: 9 minutos.** Es obligatorio capturar la pantalla; la cámara es
opcional.

## Antes de grabar (10 minutos de preparación que ahorran tres tomas)

- [ ] Ejecuta el análisis del ejemplo **una vez antes de grabar**. Así la caché está
      caliente y en la toma real no esperas a las llamadas al modelo. Deja ese análisis ya
      hecho en «Mis análisis» como red de seguridad por si la demo en vivo falla.
- [ ] Ten abiertas y ordenadas estas pestañas: la app desplegada, el repositorio en GitHub,
      el fichero `samples/legacy-erp.sql` y `TSqlAnalyzerTests.cs`.
- [ ] Sube el zoom del navegador al 125 % y el del editor a un tamaño legible en vídeo.
      Lo que se lee bien en tu monitor no se lee en un vídeo comprimido.
- [ ] Silencia notificaciones de Teams, Slack y correo.
- [ ] Ten una terminal lista con `dotnet test` sin ejecutar.

---

## 0:00 – 0:45 · El problema (sin tocar la aplicación todavía)

Abre `samples/legacy-erp.sql` y baja hasta `usp_CerrarPedido`.

> «Esto es un procedimiento almacenado de un ERP. Ochenta y nueve líneas. Cierra un pedido:
> valida crédito, genera la factura, descuenta el stock. La lógica de negocio de la empresa
> está aquí dentro, no en el código de la aplicación.
>
> Ahora imagina cuarenta y siete procedimientos como este, escritos hace quince años, sin
> documentación, y que te piden migrarlos a .NET. El primer problema no es técnico: es que
> nadie sabe qué hace este código ni por dónde se puede empezar sin romper producción.
>
> Eso hoy se resuelve con un consultor leyendo procedimientos a mano durante semanas. Legacy
> Lens automatiza ese primer paso.»

## 0:45 – 1:45 · La decisión de diseño

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

## 1:45 – 3:30 · La demo

Pulsa **«Analizar el ejemplo»**. Deja que se vea el progreso.

> «Fase uno, análisis estático: instantáneo. Fase dos, documentación: una llamada por
> objeto, en paralelo. Fase tres, el plan de migración: una sola llamada.»

Cuando termine, recorre las cuatro tarjetas del resumen.

> «Diecinueve objetos, veinte dependencias detectadas, y de los ocho objetos programables
> tres están en riesgo alto.»

**Pestaña Plan.** Lee el diagnóstico general y una fase.

> «Fíjate en el orden: primero lo autocontenido, al final los nudos de los que depende medio
> sistema. Es el patrón strangler fig, y lo puede aplicar porque conoce el grafo real.»

**Pestaña Grafo.** Cambia entre las dos vistas.

> «El color es el riesgo. Y estas aristas no son una opinión del modelo: salen del AST.»

## 3:30 – 5:00 · El momento fuerte: riesgo explicable

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

## 5:00 – 6:00 · Que no es una demo con truco

Cambia a la terminal y ejecuta `dotnet test`.

> «Quince tests sobre el analizador. Verifican que distingue lecturas de escrituras, que
> detecta el SQL dinámico en sus dos formas, que no confunde `sp_executesql` con una llamada
> a procedimiento, y que la suma de los factores de riesgo siempre cuadra con el total.
>
> Se puede testear con asserts precisamente porque esa parte es determinista. Es la otra cara
> de la decisión de diseño del principio: al separar lo calculado de lo interpretado, la
> mitad del sistema se vuelve verificable.»

Ejecuta el arnés de evaluación, o abre `docs/evals/informe.md` si prefieres no esperar.

> «Y para la otra mitad, la que genera el modelo, los asserts no sirven. Así que hay un arnés
> de evaluación con un conjunto dorado: las reglas de negocio que **sé** que están en el
> código, porque el script de ejemplo lo escribí yo.
>
> Mide tres cosas. Cobertura de reglas. Si advierte del SQL dinámico donde debe. Y objetos
> inventados — y esta última se detecta **sola**: como el parser me da el inventario exacto
> del esquema, cualquier objeto que el modelo mencione y no esté ahí es inventado por
> definición. Sin juicio humano y sin otro modelo de juez. Es la decisión de arquitectura del
> principio cobrando intereses.
>
> Y aquí me llevé una sorpresa. Yo había elegido el modelo económico para documentar por
> coste, dando por hecho que perdía algo de calidad. Al medirlo, `gpt-4.1-mini` cubre el cien
> por cien de las reglas y `gpt-4o` el ochenta y ocho: se dejó que el procedimiento crítico
> puede dejar datos inconsistentes. El modelo pequeño documenta mejor.
>
> La explicación que me parece razonable es que, cuando los hechos ya van verificados en el
> prompt, documentar no es una tarea de razonamiento: es redactar sin dejarse nada, y ahí la
> verbosidad del modelo pequeño juega a favor.
>
> Dicho con honestidad: es una ejecución por modelo y la medida es por presencia de términos.
> No demuestro una ley universal. Pero he convertido una corazonada en un dato, y eso es
> exactamente lo que no tenía antes.»

Muestra el botón de descarga de documentación y abre el `.md` resultante.

> «Y el resultado es entregable: un documento Markdown con el plan, el grafo y una ficha por
> objeto, listo para meter en el repositorio del cliente.»

## 6:00 – 7:30 · Arquitectura e infraestructura

Abre la estructura del repositorio.

> «Cuatro proyectos. `Domain` no conoce a nadie. `Analysis` y `Ai` solo conocen a `Domain`.
> Y algo importante: **`Analysis` no depende de `Ai`**. Por eso, si Azure OpenAI se cae o no
> está configurado, el análisis estático se sigue entregando y la aplicación sigue siendo
> útil.»

Abre `infra/`.

> «La infraestructura es Terraform: Azure OpenAI con sus dos despliegues de modelo, el
> registro de contenedores y el Container App. Y no hay ni un secreto: la aplicación llama a
> OpenAI y lee el registro con su identidad administrada, mediante asignaciones de rol.»

Abre `variables.tf` en los dos modelos.

> «Dos modelos con papeles distintos. Documentar cincuenta objetos es trabajo repetitivo y
> de contexto corto, así que va con el modelo económico. El plan de migración es una sola
> decisión que necesita ver el grafo entero, y ahí sí compensa el modelo capaz. Pagar el
> grande cincuenta veces no habría mejorado el resultado, solo la factura.»

## 7:30 – 8:30 · Cómo lo construí con IA

> «Como es un máster de desarrollo con IA, digo también cómo se hizo.
>
> Delegué la exploración de la API del parser, que es enorme y verbosa, el script de ejemplo
> y el código repetitivo de la interfaz. Decidí yo la separación entre lo determinista y lo
> interpretado, el modelo de dominio y la elección de los dos modelos.
>
> Y hubo dos fallos que tuve que corregir a mano. El analizador perdía las funciones
> escalares invocadas dentro de expresiones, porque no se llaman con `EXEC`. Y la primera
> versión confundía «objetos a los que nadie llama» con «objetos que no llaman a nadie», que
> son cosas distintas y llevan a órdenes de migración opuestos.
>
> Ninguno de los dos lo detectó la IA. Los detectó el volcado de diagnóstico de los tests.
> Esa es mi conclusión práctica del máster: la IA acelera muchísimo la parte mecánica, y los
> tests siguen siendo lo que separa "compila" de "funciona".»

## 8:30 – 8:50 · Que el proyecto continúa

Abre `docs/hoja-de-ruta.md`.

> «Lo que entrego es el núcleo funcionando, y el resto está planificado, no olvidado.
>
> La siguiente fase es medir: un arnés de evaluación con un conjunto dorado, porque hoy la
> mitad no determinista del sistema no se mide y quiero que la elección de los dos modelos
> deje de ser un criterio razonable para ser un dato.
>
> Y la fase dos es la que cambia la naturaleza del producto: exponer el análisis como
> servidor MCP. Ahí Legacy Lens deja de ser una herramienta que consultas y pasa a ser el
> contexto que tu agente tiene mientras escribe el código de la migración.
>
> También hay cosas descartadas a propósito: microservicios, Kubernetes y fine-tuning. No son
> pendientes, son decisiones, y están razonadas en el documento.»

## 8:50 – 9:00 · Cierre

> «Legacy Lens no sustituye al arquitecto que decide la migración. Le ahorra las dos primeras
> semanas de leer procedimientos a mano y le da un mapa con el que empezar a discutir.
>
> Gracias.»

---

## Errores que evitar

- **No leas esto palabra por palabra.** Ten los puntos delante y habla.
- **Si la demo en vivo falla, no la repares en cámara.** Di «tengo un análisis ya hecho» y
  abre el de «Mis análisis». Se ve profesional, no lo contrario.
- **No prometas lo que no hace.** La sección de limitaciones del README es un punto a favor,
  no algo que esconder. Si mencionas el SQL dinámico como límite reconocido, ganas
  credibilidad.
- **Comprueba el audio en los primeros diez segundos** de la primera toma antes de grabar
  nueve minutos sin sonido.
