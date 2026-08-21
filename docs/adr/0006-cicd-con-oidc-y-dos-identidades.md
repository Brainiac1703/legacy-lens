# ADR 0006 · CI/CD con OIDC y dos identidades separadas

**Estado:** aceptada

## Contexto

La infraestructura y la aplicación deben poder desplegarse y actualizarse desde el
repositorio, sin que nadie ejecute comandos desde su portátil. Eso implica dar a GitHub
Actions permiso para tocar Azure.

La forma tradicional es crear un *service principal*, generar un secreto de cliente y
guardarlo como secreto del repositorio. Funciona, y arrastra tres problemas conocidos: el
secreto caduca y rompe el pipeline en el peor momento, hay que rotarlo, y cualquiera con
acceso de escritura al repositorio puede exfiltrarlo con un *workflow* que lo imprima.

Había además un problema previo: el estado de Terraform vivía en la máquina local. Un
pipeline no puede trabajar así.

## Decisión

**Autenticación con OIDC y credenciales federadas.** GitHub emite un token firmado en cada
ejecución, y Azure lo valida contra el repositorio, la rama y el tipo de evento. No existe
ningún secreto almacenado en el repositorio.

**Dos identidades con permisos distintos**, no una:

| Identidad | Permisos | Se ejecuta |
| --- | --- | --- |
| `legacy-lens-infra` | Contributor en la suscripción, *RBAC Administrator* limitado al grupo de recursos de la aplicación, *Storage Blob Data Contributor* sobre el estado | Solo al cambiar `infra/` |
| `legacy-lens-deploy` | Contributor limitado al grupo de recursos de la aplicación | En cada *commit* que toque el código |

**Estado remoto en Azure Storage**, con versionado, retención de borrados y acceso por
identidad de Azure en lugar de por clave de cuenta.

**Plan y apply separados.** En un *pull request* solo se planifica, y el plan se publica
como comentario. El `apply` usa **el fichero de plan guardado**, no uno nuevo.

**La imagen se construye con `az acr build`**, dentro de Azure.

## Consecuencias

**A favor:**

- Nada que rotar y nada que se pueda filtrar. Es coherente con la decisión del
  [ADR 0005](0005-identidad-administrada-sin-secretos.md): el proyecto no tiene secretos
  en ninguna capa.
- El pipeline que se ejecuta en cada *commit* —el de despliegue, el que más veces corre y
  más superficie tiene— **no puede tocar la infraestructura ni conceder permisos**. Una
  fuga ahí tiene un alcance acotado a un grupo de recursos.
- Aplicar el plan guardado elimina la ventana entre revisar y aplicar. Lo que se aprueba es
  exactamente lo que se ejecuta.
- `az acr build` evita autenticar el ejecutor contra el registro y permite mantener las
  credenciales de administrador del registro desactivadas. Tampoco hay que subir capas de
  imagen desde el ejecutor.
- El bloqueo del estado por *blob lease* impide que un `apply` del pipeline y otro de una
  máquina local se pisen.
- El entorno `produccion` de GitHub permite exigir aprobación manual antes de tocar
  infraestructura o publicar una revisión, que es el equivalente de las aprobaciones de
  *release* de Azure DevOps.

**En contra, dicho claramente:**

- **La identidad de infraestructura es potente.** Contributor en la suscripción y, dentro de
  un grupo de recursos, capacidad de conceder roles. Está acotada a los eventos de `infra/`
  y a la rama `main`, pero sigue siendo un permiso que hay que revisar en una organización.

  El permiso de RBAC hace falta porque Terraform crea las dos asignaciones de rol de la
  identidad administrada del Container App. Si en un entorno concreto no se acepta, la
  alternativa es sacar esos dos `azurerm_role_assignment` del Terraform y crearlos a mano
  una vez: el pipeline dejaría de necesitarlo, a cambio de que la infraestructura ya no
  fuera reproducible por completo desde código.

- Hay un arranque manual: la cuenta de almacenamiento del estado y las identidades se crean
  una vez con `scripts/bootstrap-tfstate.ps1` y `scripts/bootstrap-github-oidc.ps1`. Es
  inevitable —Terraform no puede crear el sitio donde guarda su propio estado— y por eso
  está en scripts versionados y no en instrucciones de un documento.

## Alternativas consideradas

**Secreto de cliente en los secretos del repositorio.** Descartada por lo dicho: caduca,
hay que rotarlo y es exfiltrable. OIDC no cuesta más trabajo una vez configurado.

**Una sola identidad para todo.** Más simple de montar, pero da permisos de infraestructura
al pipeline que se ejecuta en cada *commit*. La separación cuesta un script y reduce
sustancialmente el alcance de un incidente.

**Ejecutar `terraform apply` directamente en `main` sin plan guardado.** Es lo más habitual
y lo más rápido. Se descartó porque el plan revisado en el *pull request* dejaría de ser una
garantía de lo que se aplica.

**`docker build` y `docker push` desde el ejecutor.** Habría exigido credenciales del
registro o un `az acr login`, y subir las capas desde el ejecutor de GitHub. `az acr build`
resuelve las dos cosas y deja la construcción cerca del registro.

**Identidad administrada asignada por el usuario con credencial federada**, en lugar de
registro de aplicación. Es igual de válido y algo más limpio conceptualmente. Se eligió el
registro de aplicación porque su configuración con `az ad app federated-credential` es la
más documentada y la que menos sorpresas da.
