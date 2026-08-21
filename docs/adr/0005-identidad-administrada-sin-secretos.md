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

El Container App tiene **identidad asignada por el sistema**, y Terraform le concede dos
roles: *Cognitive Services OpenAI User* sobre el recurso de OpenAI y *AcrPull* sobre el
registro. El registro se crea con `admin_enabled = false`.

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

## Alternativas consideradas

**Clave de API en secretos del Container App.** Funciona y es lo más rápido. Descartada
porque introduce exactamente el tipo de secreto que el módulo de seguridad enseña a
eliminar, y porque no aporta nada frente a la identidad.

**Azure Key Vault con la clave dentro.** Mueve el problema en lugar de resolverlo: sigue
existiendo un secreto, y ahora hay que autenticarse contra el Key Vault. Si ya hace falta una
identidad para leer el Vault, esa misma identidad puede hablar directamente con OpenAI.

**Identidad asignada por el usuario** en lugar de por el sistema. Es preferible cuando varios
recursos comparten identidad o cuando hay que conceder permisos antes de crear el recurso.
Aquí hay un único servicio y añadía una pieza sin necesidad.
