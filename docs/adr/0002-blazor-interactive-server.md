# ADR 0002 · Blazor `InteractiveServer` en lugar de `Auto`

**Estado:** aceptada

## Contexto

La aplicación necesita una interfaz web que permita subir un script, mostrar el avance del
análisis mientras ocurre y navegar por el resultado.

El análisis tiene tres fases de duración muy distinta: el parseo es instantáneo, la
documentación son entre diez y cincuenta llamadas a un modelo, y el plan es una llamada
larga. Un usuario que no ve avance durante un minuto asume que la aplicación se ha colgado.

Blazor ofrece tres modos de render interactivo: `Server`, `WebAssembly` y `Auto`, que
arranca en el servidor y migra al navegador cuando el WASM está descargado.

## Decisión

Se usa `InteractiveServer` para toda la aplicación.

## Consecuencias

**A favor:**

- Los componentes Razor llaman **directamente** al analizador y a EF Core. No hace falta una
  API REST intermedia ni DTOs de transporte.
- El circuito SignalR proporciona el progreso en tiempo real prácticamente gratis: basta con
  un callback que invoque `StateHasChanged`.
- El código del analizador y las credenciales de Azure nunca salen al navegador.

**En contra:**

- El servidor mantiene estado por usuario, así que escalar horizontalmente exige afinidad de
  sesión. Con una réplica no aplica, pero queda anotado como requisito previo al escalado
  (fase 3).
- Cada interacción es un viaje de ida y vuelta. Para esta aplicación es irrelevante: el
  cuello de botella son las llamadas al modelo, no la latencia de la interfaz.
- Si se cae la conexión, se pierde el circuito. El resultado ya está guardado en base de
  datos, así que el usuario no pierde el análisis.

## Alternativas consideradas

**`Auto`.** Es la opción que parecía moderna y fue la primera intención. Se descartó al
detectar el coste real: cuando el componente migra a WebAssembly corre en el navegador y ya
no puede tocar EF Core ni el parser. Obligaría a construir un proyecto cliente, una API REST
y DTOs para todo — el doble de superficie. Y `Auto` existe para resolver la latencia de
primera carga en aplicaciones con mucho tráfico, un problema que esta no tiene.

**API + SPA (React o Angular).** Mismo problema que `Auto`, con más piezas y sin ninguna
ganancia para este caso de uso. Habría exigido además implementar a mano el streaming de
progreso que `Server` da resuelto.

**Render estático con recarga por sondeo.** Habría funcionado, pero convierte la parte más
demostrativa del producto —ver el análisis avanzar— en algo torpe.
