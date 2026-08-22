# ADR 0005 · Identidad administrada en lugar de claves

**Estado:** aceptada

## Contexto

La aplicación desplegada necesita dos accesos privilegiados: llamar a Azure OpenAI y leer la
imagen del registro de contenedores.

La vía habitual es una clave de API y las credenciales de administrador del registro,
guardadas como secretos del Container App y como secretos del repositorio para el
despliegue. Eso genera cuatro problemas conocidos: hay que almacenarlas, hay que rotarlas,
aparecen en variables de entorno visibles en el portal, y acaban filtrándose en un `git add`
descuidado.

El módulo de seguridad del máster insiste en *security by default*: la opción por omisión
tiene que ser la segura, no la cómoda.

## Decisión

El Container App usa una **identidad asignada por el usuario**, creada como recurso
independiente, y Terraform le concede dos roles: *Cognitive Services OpenAI User* sobre el
recurso de OpenAI y *AcrPull* sobre el registro. El registro se crea con
`admin_enabled = false`.

Esta decisión se tomó primero al contrario, con identidad asignada por el sistema, y el
despliegue demostró que no funcionaba. Queda explicado abajo, en las alternativas, porque el
motivo es más instructivo que la decisión.

En el código, `AiOptions` decide el mecanismo por ausencia:

- Con `Ai:ApiKey` configurada, se usa la clave. Solo para desarrollo local.
- Sin clave, se usa `DefaultAzureCredential`: la identidad administrada en Azure, o la sesión
  de `az login` en la máquina del desarrollador.

**En producción no hay ninguna clave configurada.** No es una política que haya que
recordar: es que no existe el secreto.

## Consecuencias

**A favor:**

- No hay ningún secreto que almacenar, rotar ni que se pueda filtrar.
- El permiso es explícito y auditable: está en `Deploy/infra/app.tf`, revisable en un *pull
  request*.
- El mismo código funciona en local y en producción sin ramas de configuración: en local usa
  la identidad del desarrollador, que además hereda sus propios permisos.
- Los permisos son mínimos y concretos. *OpenAI User* no puede crear ni borrar despliegues de
  modelo, y *AcrPull* no puede escribir en el registro.

**En contra:**

- Para desarrollar en local hace falta `az login` y tener asignado el rol sobre el recurso.
  Es un paso más que copiar una clave, y está documentado en el README.
- `DefaultAzureCredential` prueba varios mecanismos en orden, lo que hace que un fallo de
  autenticación sea algo menos evidente de diagnosticar que una clave incorrecta.
- Con identidad asignada por el usuario hay que decirle **cuál**: el contenedor recibe
  `AZURE_CLIENT_ID`. Con identidad de sistema no hacía falta. Es una variable de entorno más
  que, si falta, produce un fallo de autenticación en SQL y en OpenAI a la vez.
- El usuario de la base de datos ya no se llama igual que el Container App, sino igual que la
  identidad. El pipeline lee ese nombre de una salida de Terraform en lugar de darlo por
  sabido.

## Alternativas consideradas

**Clave de API en secretos del Container App.** Funciona y es lo más rápido. Descartada
porque introduce exactamente el tipo de secreto que el módulo de seguridad enseña a
eliminar, y porque no aporta nada frente a la identidad.

**Azure Key Vault con la clave dentro.** Mueve el problema en lugar de resolverlo: sigue
existiendo un secreto, y ahora hay que autenticarse contra el Key Vault. Si ya hace falta una
identidad para leer el Vault, esa misma identidad puede hablar directamente con OpenAI.

**Identidad asignada por el sistema** en lugar de por el usuario. Fue la primera decisión, y
era la equivocada. La descartamos al revés —esta misma sección decía que la identidad de
usuario «es preferible cuando hay que conceder permisos antes de crear el recurso, y aquí hay
un único servicio»— sin darnos cuenta de que este proyecto **es** ese caso.

Una identidad de sistema nace con el recurso, así que su `principal_id` no existe hasta que
el Container App está creado y los roles solo pueden asignarse después. Pero Azure no termina
de aprovisionar el Container App hasta poder autenticarse contra el registro de contenedores,
y para eso necesita *AcrPull*. El ciclo se cierra: el rol espera al recurso y el recurso
espera al rol.

No falla con un error, que sería más fácil. El aprovisionamiento se queda en `InProgress` sin
crear ninguna revisión, y `terraform apply` sigue imprimiendo *Still creating…* hasta agotar
el tiempo. Lo descubrimos a los 19 minutos del primer despliegue real.

Crear la identidad como recurso aparte rompe el ciclo: los dos roles existen antes que la
aplicación. Hace falta además un `depends_on` explícito, porque los roles cuelgan de la
identidad y no del Container App, así que Terraform no ve ninguna dependencia entre ellos y
los crearía en paralelo. Sin él, el ciclo vuelve convertido en carrera: a veces el registro
estaría autorizado a tiempo y a veces no.

La lección que merece la pena guardar no es sobre Azure, es sobre el propio ADR: la
alternativa descartada llevaba escrito el criterio exacto que la habría elegido.
