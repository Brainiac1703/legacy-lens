# Informe de evaluación

Generado por `tools/LegacyLens.Evals` sobre `samples/legacy-erp.sql`.

Este informe mide la parte **no determinista** del sistema. El análisis estático
se verifica con tests unitarios; la documentación generada por el modelo no se
puede comprobar con asserts, así que se mide contra un conjunto dorado de reglas
de negocio que sabemos que están en el código, porque el script de ejemplo se
escribió para este proyecto.

La métrica de **objetos inventados** merece una nota. Es comprobable de forma
automática y sin intervención humana gracias a la decisión de arquitectura
central: el parser produce el inventario exacto del esquema, así que cualquier
referencia cualificada que el modelo mencione y no esté en ese inventario es,
por definición, inventada.

## Comparativa

| Modelo | Cobertura de reglas | Objetos inventados | Avisos omitidos | Llamadas | Tokens entrada | Tokens salida | Segundos |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `gpt-4.1-mini` | 16/16 (100 %) | 0 | 0 | 9 | 8552 | 2951 | 17,3 |
| `gpt-4o` | 14/16 (88 %) | 0 | 0 | 9 | 8405 | 1832 | 9,1 |

Los tokens son acumulados e incluyen la llamada del plan de migración.

## Detalle: gpt-4.1-mini

- ✔ `dbo.usp_CerrarPedido` — 5/5
- ✔ `dbo.fn_CalcularDescuento` — 3/3
- ✔ `dbo.usp_FacturarPedidosPendientes` — 2/2
- ✔ `dbo.usp_InformeVentas` — 1/1
- ✔ `dbo.usp_PurgarAuditoria` — 1/1
- ✔ `dbo.usp_RecalcularTarifas` — 3/3
- ✔ `dbo.trg_LineasPedido_Auditoria` — 1/1

### Salida generada

#### `dbo.usp_CerrarPedido` (riesgo 55)

Este procedimiento cierra un pedido, generando la factura correspondiente y actualizando el stock de los artículos vendidos. Verifica que el cliente no tenga facturas vencidas con más de 60 días antes de proceder, y registra la operación en una tabla de auditoría.

*Reglas de negocio extraídas:*

- No se permite cerrar un pedido si el cliente asociado tiene facturas no cobradas vencidas hace más de 60 días.
- El total del pedido se calcula sumando el importe de las líneas del pedido y aplicando un descuento calculado según el cliente y el total.
- La factura se crea con base imponible igual al total descontado y total con impuesto del 21%, y tiene fecha de vencimiento calculada según los días de crédito del cliente.
- El stock se descuenta línea por línea del pedido llamando a un procedimiento que registra movimientos de stock.
- El estado del pedido se actualiza a 'CERRADO', se actualiza el total final y la fecha de cierre.
- Se registra una entrada en la tabla de auditoría indicando el cierre del pedido y el usuario que realizó la operación.

*Efectos colaterales:*

- Inserta registros en las tablas Facturas, LineasFactura y Auditoria.
- Actualiza el estado, total y fecha de cierre del pedido en la tabla Pedidos.
- Modifica el stock de los artículos mediante llamadas al procedimiento dbo.usp_RegistrarMovimientoStock.
- El proceso usa un cursor para el manejo fila a fila de las líneas del pedido.
- No utiliza transacciones explícitas ni manejo de errores, por lo que una falla podría dejar datos inconsistentes en las tablas afectadas.

*Destino propuesto:* Convertir este procedimiento almacenado en un servicio de dominio en .NET que gestione el cierre del pedido de forma transaccional. La lógica de negocio debería implementarse en código C# separando las responsabilidades: validación de facturas vencidas, cálculo de totales y descuentos, creación de factura y líneas, actualización del stock y cierre del pedido. Es necesario eliminar el cursor y gestionar las actualizaciones del stock en lote o con lógica transaccional adecuada. También se debe implementar manejo de excepciones para evitar estados inconsistentes. La tabla de auditoría podría ser gestionada por un mecanismo de logging propio de la aplicación. Se recomienda eliminar la lógica relacionada con actualizaciones de stock del ámbito de la base de datos y manejarla en la capa de dominio para facilitar la mantenibilidad y escalabilidad.

#### `dbo.usp_FacturarPedidosPendientes` (riesgo 40)

Este procedimiento procesa todos los pedidos pendientes registrándolos para cierre en orden cronológico. Para cada pedido pendiente, intenta cerrarlo invocando otro procedimiento y registra cualquier error ocurrido durante este proceso.

*Reglas de negocio extraídas:*

- Los pedidos pendientes deben procesarse en orden de fecha ascendente.
- Cada pedido pendiente se procesa individualmente para cerrarlo utilizando el procedimiento dbo.usp_CerrarPedido.
- Si ocurren errores al cerrar un pedido, se incrementa un contador y se registra el error en la tabla dbo.Auditoria con información específica del error y la operación 'ERROR_CIERRE'.

*Efectos colaterales:*

- Modifica la tabla dbo.Auditoria insertando registros que documentan los errores durante el cierre de pedidos.
- El procedimiento realiza modificaciones indirectas a través de dbo.usp_CerrarPedido en cada pedido cerrado.
- No hay una transacción explícita que englobe todo el proceso, por lo que si ocurre una falla durante el procesamiento, los datos pueden quedar en un estado inconsistente, con pedidos parcialmente cerrados y auditoría registrada solo para errores ocurridos hasta ese punto.

*Destino propuesto:* Debe refactorizarse como un servicio en segundo plano en .NET que ejecute un proceso por lotes para cerrar pedidos pendientes. La lógica de procesamiento fila a fila debe eliminarse para usar operaciones en lote o concurrencia controlada en el servicio. La gestión de errores debe centralizarse en el servicio, registrando los fallos en un repositorio de auditoría equivalente. Se recomienda extraer por completo la lógica de cierre de pedidos fuera de la base de datos (incluyendo lo que hace dbo.usp_CerrarPedido), para implementar esta lógica dentro del servicio .NET, evitando cursores y mejorando la consistencia y control transaccional.

#### `dbo.trg_LineasPedido_Auditoria` (riesgo 40)

Este disparador se ejecuta automáticamente después de insertar o actualizar registros en la tabla LineasPedido. Su función es registrar en la tabla Auditoria una entrada que indica qué líneas y pedidos han sido modificados, incluyendo el usuario que realizó la operación.

*Reglas de negocio extraídas:*

- Cada vez que se inserte o actualice una línea de pedido en la tabla LineasPedido, se debe crear un registro de auditoría con los detalles de la línea modificada y el usuario que realizó el cambio.

*Efectos colaterales:*

- Inserta registros en la tabla Auditoria describiendo la modificación realizada en LineasPedido.
- No utiliza transacciones explícitas ni control de errores, por lo que si la inserción en Auditoria falla, podría dejar inconsistencias en el registro de auditoría sin afectar directamente a LineasPedido.

*Destino propuesto:* Este disparador debería migrarse a un servicio de dominio o componente de aplicación en .NET que escuche eventos o comandos de modificación sobre las líneas de pedido. Dicho servicio debe registrar las auditorías en la base de datos de forma transaccional y con manejo de errores adecuado, retirando la lógica de auditoría del motor de base de datos para mejorar el control, la mantenibilidad y la trazabilidad.

#### `dbo.usp_InformeVentas` (riesgo 20)

El procedimiento genera un informe de ventas filtrado por fechas y por familia de productos, mostrando facturas con su fecha, el nombre del cliente y el total. Permite ordenar los resultados por diferentes columnas indicadas en el parámetro.

*Reglas de negocio extraídas:*

- Si se proporciona una fecha inicial, solo se incluyen facturas a partir de esa fecha.
- Si se proporciona una fecha final, solo se incluyen facturas hasta esa fecha.
- Si se especifica una familia de productos, se incluyen solo las facturas que contienen al menos un artículo de esa familia.
- Los datos se ordenan según el criterio indicado en el parámetro 'OrdenarPor', que puede ser cualquier columna válida del conjunto de resultados.

*Efectos colaterales:*

- No modifica datos ni tablas en la base de datos.
- Construye y ejecuta una consulta dinámica, por lo que la exactitud del ordenamiento y filtros depende de la validación externa de parámetros.

*Destino propuesto:* Convertir en un servicio de dominio en .NET que reciba parámetros de filtro y ordenamiento, construyendo la consulta mediante consultas parametrizadas o LINQ para garantizar seguridad y claridad. La lógica de filtrado debe moverse fuera de SQL dinámico para evitar riesgos de inyección y mejorar la mantenibilidad. Además, la consulta debe ejecutarse desde .NET contra el modelo de datos para controlar mejor las dependencias y facilitar pruebas.

#### `dbo.usp_PurgarAuditoria` (riesgo 20)

Este procedimiento elimina registros antiguos de la tabla de auditoría que tienen una fecha anterior a un límite calculado en función de los días de retención especificados. Su propósito es mantener la tabla de auditoría limpia y optimizar el almacenamiento.

*Reglas de negocio extraídas:*

- Sólo se eliminan registros cuya fecha es anterior al límite calculado restando el número de días de retención desde la fecha actual.
- El valor por defecto de días de retención es 365, lo que implica conservar al menos un año de registros de auditoría.

*Efectos colaterales:*

- El procedimiento elimina permanentemente registros de la tabla dbo.Auditoria que superan el periodo de retención.
- El uso de SQL dinámico impide conocer estáticamente las dependencias exactas de la eliminación.

*Destino propuesto:* Este procedimiento debería implementarse como un trabajo en segundo plano (background job) o un servicio de dominio en .NET que ejecute periódicamente la purga de registros antiguos. La lógica del cálculo del límite y eliminación debe trasladarse a código C# que invoque comandos parametrizados para evitar SQL dinámico y mejorar la seguridad y mantenibilidad. Además, la responsabilidad de ejecutar esta limpieza debería sacarse de la base de datos y gestionarse en la capa de aplicación para mayor control y escalabilidad.

#### `dbo.fn_CalcularDescuento` (riesgo 0)

Esta función calcula el porcentaje de descuento aplicable a un cliente en base a su antigüedad, el total facturado el año anterior y el importe actual de la compra.

*Reglas de negocio extraídas:*

- Se otorga un descuento del 5% si el cliente tiene 10 o más años de antigüedad.
- Se otorga un descuento del 3% si el cliente tiene entre 5 y 9 años de antigüedad.
- Si el cliente facturó más de 100,000 el año anterior, se añade un 4% de descuento al total.
- Si el cliente facturó entre 50,001 y 100,000 el año anterior, se añade un 2% de descuento al total.
- Si el importe actual es mayor a 10,000, se añade un 1% de descuento al total.
- El descuento máximo total que se puede aplicar es del 12%.

*Destino propuesto:* Esta función se debe migrar como un método en un servicio de dominio dentro de la capa de lógica de negocio en .NET. La lógica debe obtener previamente los datos necesarios (antigüedad y facturación del cliente) mediante consultas separadas a la base de datos y luego calcular el descuento aplicando las reglas de negocio. Esto permite separar la lógica de cálculo del acceso a datos y elimina la dependencia de la función SQL.

#### `dbo.usp_RegistrarMovimientoStock` (riesgo 0)

Este procedimiento registra un movimiento en el inventario para un artículo y almacén específicos, almacenando el detalle del movimiento y actualizando la cantidad disponible en stock. Realiza ambas operaciones dentro de una transacción para asegurar la consistencia de los datos.

*Reglas de negocio extraídas:*

- Cada movimiento de stock debe quedar registrado en la tabla MovimientosStock con el artículo, almacén, cantidad y motivo proporcionados.
- La cantidad en la tabla Stock se debe actualizar sumando la cantidad del movimiento para el mismo artículo y almacén.
- Se debe asegurar que ambas operaciones (registro del movimiento y actualización del stock) se ejecuten en una única transacción atómica para mantener la consistencia.

*Efectos colaterales:*

- Inserta un nuevo registro en la tabla MovimientosStock con el detalle del movimiento.
- Actualiza la cantidad en la tabla Stock para el artículo y almacén especificados.
- Si ocurre un error, se revierte la transacción para evitar cambios parciales que puedan dejar los datos inconsistentes.

*Destino propuesto:* Servicio de dominio en .NET que exponga un método para registrar movimientos de stock. Este método debe manejar la transacción de manera atómica, utilizando acceso a datos mediante Entity Framework u otra herramienta ORM para insertar el registro de movimiento y actualizar el stock. Se recomienda eliminar esta lógica de la base de datos y manejar toda la operación en la capa de aplicación para mejorar mantenibilidad y control de errores.

#### `dbo.usp_RecalcularTarifas` (riesgo 0)

El procedimiento recalcula las tarifas de los artículos, aplicando un incremento porcentual variable según el precio base y opcionalmente filtrando por familia. Actualiza la tabla de tarifas con los nuevos precios y registra en auditoría la cantidad de tarifas recalculadas.

*Reglas de negocio extraídas:*

- Se recalculan precios por artículo que no estén descatalogados y, si se especifica, pertenezcan a una familia dada.
- El incremento aplicado varía según el precio base del artículo: para precios mayores a 1000 se aplica la mitad del incremento, para precios entre 100 y 1000 se aplica el incremento completo, y para precios inferiores a 100 se multiplica el incremento por 1.5.
- Se insertan nuevas filas en la tabla de tarifas si no existía registro previo o si la diferencia entre el precio nuevo y anterior es mayor a 0.01.

*Efectos colaterales:*

- Modifica la tabla dbo.Tarifas insertando nuevas filas con las tarifas recalculadas.
- Inserta en la tabla dbo.Auditoria un registro con el total de tarifas actualizadas durante la ejecución.
- Utiliza una transacción para garantizar que las modificaciones a Tarifas y Auditoria ocurren de forma atómica, evitando inconsistencias en caso de error.

*Destino propuesto:* Debería implementarse como un servicio de dominio en .NET que recupere los artículos desde la base, realice el cálculo de precios en memoria y actualice las tarifas y auditoría mediante comandos explícitos para mantener la trazabilidad. Este servicio podría ejecutarse como un trabajo programado o una operación manual, eliminando la lógica compleja que reside actualmente en el procedimiento almacenado y facilitando futuras modificaciones y pruebas.

### Plan de migración generado

El sistema existente contiene lógica de negocios implementada directamente en objetos programables de SQL Server, lo que puede ocasionar desafíos en cuanto a mantenimiento y migración debido a dependencias intrincadas y prácticas inseguras de manejo de transacciones.

**Fase 1 — Fase 1: Migración de componentes autocontenidos y de bajo riesgo**

- Por qué ahora: Para minimizar riesgos y establecer las bases del nuevo sistema, comenzaremos migrando los componentes autocontenidos y con bajo impacto en las operaciones generales.
- Objetos: `dbo.fn_CalcularDescuento`, `dbo.usp_RegistrarMovimientoStock`, `dbo.usp_RecalcularTarifas`, `dbo.usp_PurgarAuditoria`, `dbo.usp_InformeVentas`
- Riesgo: Low

**Fase 2 — Fase 2: Migración de triggers con secuencias específicas de actualización**

- Por qué ahora: En esta etapa, se tratarán objetos programables que interactúan directamente con las tablas a través de actualizaciones automatizadas en función de eventos específicos.
- Objetos: `dbo.trg_LineasPedido_Auditoria`
- Riesgo: Medium

**Fase 3 — Fase 3: Migración de procedimientos intermedios con dependencias hacia módulos manejados**

- Por qué ahora: Para seguir con modulos más complejos mientras se mantienen sus dependencias correctamente manejadas.
- Objetos: `dbo.usp_CerrarPedido`
- Riesgo: High

**Fase 4 — Fase 4: Migración de procesos de alto nivel con integración total**

- Por qué ahora: Finalmente, se manejarán los procedimientos de orquestación que involucran llamadas complejas entre componentes.
- Objetos: `dbo.usp_FacturarPedidosPendientes`
- Riesgo: High

*Riesgos globales:*

- Dependencias cruzadas no detectadas en el análisis inicial.
- Complejidad en la recreación de procesos con transacciones distribuidas en múltiples capas.
- Confusión potencial entre el comportamiento previo y posterior a la migración debido a cambios en la lógica de negocio.

## Detalle: gpt-4o

- ◐ `dbo.usp_CerrarPedido` — 4/5
  - No cubierta: Puede dejar datos inconsistentes: escribe sin transacción
- ✔ `dbo.fn_CalcularDescuento` — 3/3
- ◐ `dbo.usp_FacturarPedidosPendientes` — 1/2
  - No cubierta: Recorre los pedidos pendientes uno a uno
- ✔ `dbo.usp_InformeVentas` — 1/1
- ✔ `dbo.usp_PurgarAuditoria` — 1/1
- ✔ `dbo.usp_RecalcularTarifas` — 3/3
- ✔ `dbo.trg_LineasPedido_Auditoria` — 1/1

### Salida generada

#### `dbo.usp_CerrarPedido` (riesgo 55)

Este procedimiento gestiona el cierre de un pedido, validando si el cliente tiene facturas vencidas, calculando el total con descuentos, generando la factura correspondiente y registrando en la auditoría.

*Reglas de negocio extraídas:*

- Un pedido no puede cerrarse si el cliente asociado tiene facturas vencidas más de 60 días.
- El total del pedido se calcula sumando los productos y aplicando un descuento obtenido de dbo.fn_CalcularDescuento.
- El estado del pedido cerrado se actualiza a 'CERRADO' y su fecha queda registrada.
- Para cada artículo del pedido se reduce el stock utilizando el procedimiento dbo.usp_RegistrarMovimientoStock.

*Efectos colaterales:*

- Insertar facturas y detalles de línea en dbo.Facturas y dbo.LineasFactura, respectivamente.
- Registrar modificaciones de stock con dbo.usp_RegistrarMovimientoStock.
- Actualizar el estado del pedido a ‘CERRADO’ y registrar su total y fecha de cierre.
- Insertar detalles del proceso en dbo.Auditoria para fines de auditoría.

*Destino propuesto:* Convertir a un servicio de dominio en .NET que gestione la lógica del cierre de pedidos, usando una transacción global y servicios de infraestructura para manejar auditoría y actualizaciones de transacciones.

#### `dbo.usp_FacturarPedidosPendientes` (riesgo 40)

Este procedimiento se encarga de procesar cada pedido pendiente registrado en la vista vw_PedidosPendientes, invocando el procedimiento usp_CerrarPedido, y registra errores en caso de fallos.

*Reglas de negocio extraídas:*

- Todos los pedidos pendientes se seleccionan de la vista vw_PedidosPendientes en orden de fecha para ser procesados.
- Por cada pedido procesado, si ocurre un error, este se registra en la tabla dbo.Auditoria con un detalle del mensaje de error.

*Efectos colaterales:*

- Actualiza información en la tabla Auditoria en caso de errores al procesar un pedido.
- Invoca al procedimiento usp_CerrarPedido para intentar cerrar los pedidos identificados como pendientes.

*Destino propuesto:* Un trabajo en segundo plano en .NET con manejo asíncrono y procesamiento en bloques para optimizar la ejecución y minimizar impacto en la base de datos.

#### `dbo.trg_LineasPedido_Auditoria` (riesgo 40)

Este trigger audita las operaciones de inserción y actualización realizadas en la tabla LineasPedido, registrando detalles en la tabla Auditoria.

*Reglas de negocio extraídas:*

- Cada vez que se inserta o actualiza un registro en la tabla LineasPedido, se registra una entrada en la tabla Auditoria.
- El registro de auditoría incluye el nombre de la tabla afectada, el tipo de operación ('MODIFICACION'), detalles sobre las claves modificadas, y el usuario que realizó el cambio.

*Efectos colaterales:*

- Inserta registros en la tabla Auditoria sin garantizar atomicidad debido a la ausencia de una transacción explicítita. Esto puede generar inconsistencias en caso de fallo a mitad del proceso.

*Destino propuesto:* Este trigger debe migrarse a un servicio de auditoría implementado como una clase en .NET. Este servicio debería ser llamado explícitamente desde el código de la capa de negocio o datos durante las operaciones de inserción o actualización. Esto proporciona manejo explícito de transacciones y permite agregar registros de auditoría en tiempo real.

#### `dbo.usp_InformeVentas` (riesgo 20)

Este procedimiento genera un informe dinámico de ventas en función de parámetros opcionales.

*Reglas de negocio extraídas:*

- Solo se consideran las facturas dentro del rango de fechas especificado por los parámetros @Desde y @Hasta.
- Se filtran las facturas relacionadas a artículos de una familia específica si se proporciona el parámetro @Familia.
- El informe es dinámico y el orden se establece según el parámetro @OrdenarPor.

*Efectos colaterales:*

- Es un procedimiento de solo lectura, no produce impactos directos sobre los datos existentes.

*Destino propuesto:* Este procedimiento debería migrarse a un servicio de dominio o a un repositiorio que genere consultas parametrizadas dinámicamente para mantener seguridad y escalabilidad.

#### `dbo.usp_PurgarAuditoria` (riesgo 20)

Este procedimiento elimina registros antiguos de una tabla dinámica llamada 'Auditoria', basada en una fecha límite calculada.

*Reglas de negocio extraídas:*

- Solo los registros de la tabla 'Auditoria' con una fecha anterior a la fecha límite calculada se eliminan.
- La fecha límite se calcula restando 'DiasRetencion' días a la fecha actual.

*Efectos colaterales:*

- Modifica registros en la tabla 'Auditoria', cuyo contenido específico no se puede determinar sin ejecutar la consulta dinámica.

*Destino propuesto:* Un servicio de dominio en .NET que interactúe con bases de datos y permita la limpieza basada en un período de retención configurable.

#### `dbo.fn_CalcularDescuento` (riesgo 0)

La función calcula el porcentaje de descuento aplicable según la antigüedad del cliente, sus valores de facturación previos y el importe actual.

*Reglas de negocio extraídas:*

- Un cliente con antigüedad de 10 o más años recibe un 5% de descuento.
- Un cliente con antigüedad de entre 5 y menos de 10 años recibe un 3% de descuento.
- Si el cliente facturó más de 100,000 unidades monetarias el año anterior, su descuento aumenta en 4%.
- Si el cliente facturó entre 50,000 y 100,000 unidades monetarias el año anterior, su descuento aumenta en 2%.
- Si el importe actual excede 10,000 unidades, el descuento se incrementa en 1%.
- El descuento total no puede superar el 12%.

*Destino propuesto:* Servicio de dominio en .NET, utilizando objetos de consulta para reemplazar las consultas de SQL y centralizando la lógica empresarial en una capa de servicio.

#### `dbo.usp_RegistrarMovimientoStock` (riesgo 0)

Este procedimiento almacena un registro de movimiento de stock y ajusta la cantidad de stock en consecuencia.

*Reglas de negocio extraídas:*

- El movimiento de stock registrado debe incluir un ArticuloId, un AlmacenId, una Cantidad y un Motivo.
- La cantidad de stock se actualiza sumando la cantidad dada solo si coinciden el ArticuloId y el AlmacenId.

*Efectos colaterales:*

- Inserta un registro en la tabla dbo.MovimientosStock.
- Actualiza la columna Cantidad en la tabla dbo.Stock.
- Si el procedimiento falla, no queda ningún cambio inconsistente debido al uso de transacciones.

*Destino propuesto:* Servicio de dominio .NET con operaciones transaccionales para registrar movimientos de stock y actualizar cantidades, utilizando Entity Framework o Dapper para la persistencia.

#### `dbo.usp_RecalcularTarifas` (riesgo 0)

Este procedimiento almacenado realiza un cálculo de tarifas basado en precios base de artículos, aplicando un incremento y registrando los cambios en un registro de auditoría.

*Reglas de negocio extraídas:*

- Los artículos procesados son únicamente aquellos no descatalogados.
- Si no se especifica una familia, se consideran todas.
- El incremento aplicado varía según el precio base del artículo (tres tramos definidos).
- Se persisten las tarifas nuevas cuando el artículo no tiene tarifa previa o el cambio supera determinada diferencia (0.01).

*Efectos colaterales:*

- Actualiza la tabla dbo.Tarifas insertando nuevas tarifas calculadas.
- Registra estadísticas de la operación en la tabla dbo.Auditoria.

*Destino propuesto:* Este procedimiento debe migrarse a un servicio de dominio que gestione los cálculos y actualizaciones como operaciones transaccionales. Se pueden mapear las consultas a clases de dominio y manejar los cálculos en un contexto de aplicación o servicio.

### Plan de migración generado

El sistema actual emplea SQL Server para alojar diversas piezas clave de la lógica de negocio, incluyendo funciones y procedimientos almacenados que interactúan frecuentemente con tablas y otras funciones, muchas veces con el uso de SQL dinámico y cursores.

**Fase 1 — Fase 1: Migración de componentes autocontenidos de bajo riesgo**

- Por qué ahora: Comenzar con componentes autocontenidos de bajo riesgo permite establecer una base sólida y comprender mejor el proceso de migración.
- Objetos: `dbo.fn_CalcularDescuento`, `dbo.usp_RegistrarMovimientoStock`, `dbo.usp_RecalcularTarifas`, `dbo.usp_PurgarAuditoria`
- Riesgo: Bajo

**Fase 2 — Fase 2: Migración de componentes de riesgo moderado y eficientes**

- Por qué ahora: Los objetos de riesgo moderado que no tienen dependencias son ideales para la siguiente fase, consolidando la migración.
- Objetos: `dbo.usp_InformeVentas`, `dbo.trg_LineasPedido_Auditoria`
- Riesgo: Moderado

**Fase 3 — Fase 3: Migración de componentes dependientes de alto riesgo**

- Por qué ahora: Finalmente, los componentes más críticos y dependientes son migrados, ya teniendo experiencia con las fases previas.
- Objetos: `dbo.usp_CerrarPedido`, `dbo.usp_FacturarPedidosPendientes`
- Riesgo: Alto

*Riesgos globales:*

- Posibles inconsistencias al sincronizar acceso entre el sistema heredado y nuevo durante la transición.
- El uso de lógica compleja en SQL podría ser difícil de replicar eficientemente en el código .NET.
- Demandará pruebas exhaustivas para asegurar que las implementaciones migradas cumplen con los requisitos funcionales.

