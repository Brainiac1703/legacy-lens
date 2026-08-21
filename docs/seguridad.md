# Seguridad

Revisión de Legacy Lens contra **OWASP Top 10 2025**, categoría por categoría.

El objetivo no es marcar casillas: es dejar por escrito qué se ha hecho, qué no aplica y
**qué queda pendiente**. Una categoría marcada como cubierta sin explicar cómo no vale nada.

## Resumen

| Categoría | Estado |
| --- | --- |
| A01 Broken Access Control | Cubierta |
| A02 Security Misconfiguration | Cubierta |
| A03 Software Supply Chain Failures | Cubierta |
| A04 Cryptographic Failures | Cubierta |
| A05 Injection | Cubierta por diseño |
| A06 Insecure Design | Cubierta |
| A07 Authentication Failures | Parcial |
| A08 Software and Data Integrity Failures | Parcial |
| A09 Logging and Alerting Failures | Parcial |
| A10 Mishandling of Exceptional Conditions | Cubierta |

---

## A01 · Broken Access Control

**Superficie:** cada usuario tiene sus propios análisis. Un análisis contiene el código
fuente de la base de datos de alguien, así que una fuga entre usuarios sería grave.

**Qué se ha hecho:**

- El propietario forma parte de la **firma** de `IAnalysisRepository`: no existe una forma
  de pedir un análisis sin decir de quién es. No es una comprobación que se pueda olvidar,
  es un parámetro obligatorio.
- El filtro va en el `WHERE` de la consulta, no en una comparación posterior en memoria: la
  fila de otro usuario nunca sale del servidor.
- `GetAsync` devuelve `null` tanto si no existe como si es de otro, a propósito: distinguir
  los dos casos revelaría qué identificadores existen.
- Las páginas llevan `[Authorize]`, y el endpoint de descarga `.RequireAuthorization()`.
- Los identificadores son `Guid`, no enteros secuenciales: no se pueden enumerar.

**Verificado:** una petición a `/analizar` sin sesión responde 302 al inicio de sesión.

**Pendiente:** una prueba automatizada que intente leer el análisis de otro usuario. Hoy la
garantía es la revisión del código, no un test. Entra en la fase 3 con Playwright.

## A02 · Security Misconfiguration

- El contenedor **no corre como root**: `USER $APP_UID` en el Dockerfile.
- El registro de contenedores se crea con `admin_enabled = false`.
- HSTS activo fuera de desarrollo.
- Las cabeceras reenviadas se procesan de forma explícita, y la razón de vaciar
  `KnownIPNetworks` está comentada en el código: el proxy de Container Apps no tiene IP
  fija, y el contenedor no es alcanzable salvo a través de él.
- La página de excepciones detallada solo existe en desarrollo.

**Pendiente:** cabeceras `Content-Security-Policy` y `X-Content-Type-Options`. Hoy la
aplicación carga Mermaid desde un CDN, así que una CSP estricta requiere fijar antes ese
origen o servir la librería desde el propio dominio.

## A03 · Software Supply Chain Failures

Esta categoría dejó de ser teórica durante el desarrollo: la compilación avisó de que
`SQLitePCLRaw.lib.e_sqlite3` 2.1.11 tenía una vulnerabilidad de gravedad alta conocida
(GHSA-2m69-gcr7-jv3q). Llegó como **dependencia transitiva** de EF Core, sin haberla pedido
nadie.

**Qué se ha hecho:**

- Se corrigió fijando una versión con parche del paquete afectado.
- `dependabot.yml` vigila NuGet, GitHub Actions, Terraform y las imágenes base de Docker.
- El *workflow* de seguridad ejecuta `dotnet list package --vulnerable --include-transitive`
  y **falla la compilación** si aparece cualquier vulnerabilidad de gravedad moderada o
  superior.
- Se ejecuta también de forma semanal por programación, porque una vulnerabilidad publicada
  después del último *commit* no la ve ningún análisis disparado por *push*.
- Las acciones de GitHub están fijadas por versión mayor, no por rama móvil.

## A04 · Cryptographic Failures

- **No hay ningún secreto en la aplicación desplegada.** La autenticación contra Azure
  OpenAI y contra el registro se hace con identidad administrada. Ver
  [ADR 0005](adr/0005-identidad-administrada-sin-secretos.md).
- TLS lo termina el proxy de Container Apps; el tráfico de entrada es HTTPS.
- Las contraseñas las gestiona ASP.NET Core Identity, que usa PBKDF2 con sal por usuario.
  No se ha reimplementado nada de esto a mano, que es la decisión correcta.
- `terraform.tfvars` está excluido del repositorio, y el estado de Terraform también.

**Cifrado en reposo:** resuelto al pasar a Azure SQL Database, que aplica *Transparent Data
Encryption* de forma predeterminada. Antes el almacén era un fichero SQLite sin cifrar, y eso
importaba porque contiene el código fuente de los scripts analizados.

El servidor se crea además con **autenticación exclusivamente por Entra**
(`azuread_authentication_only`), así que no existe ninguna contraseña de base de datos que
guardar, ni siquiera para el administrador.

## A05 · Injection

**Esta categoría se resuelve por diseño y merece explicarse bien, porque la aplicación
manipula SQL constantemente.**

Legacy Lens recibe scripts de SQL Server y los procesa. La tentación evidente sería
conectarse a la base de datos para obtener metadatos o validar algo. **No se hace, y no se
va a hacer:** el SQL recibido se **parsea**, nunca se ejecuta. No existe en el proyecto
ninguna conexión a la base de datos analizada.

Consecuencia: un script malicioso no tiene nada que atacar. El peor caso posible es un
script que el parser no entienda, y eso se degrada a un error de sintaxis reportado.

Esta restricción está escrita en [`AGENTS.md`](../AGENTS.md) como frontera que no se cruza,
precisamente para que nadie —persona o agente— la debilite en el futuro buscando más
precisión.

El acceso a la base de datos propia de la aplicación va siempre por EF Core con consultas
parametrizadas. No hay SQL concatenado en ninguna parte.

## A06 · Insecure Design

- El análisis estático y la capa de IA están separados de forma que **el fallo de la IA no
  degrada la seguridad ni la utilidad**: si el modelo no responde, se entrega el análisis
  verificado.
- Los límites de la subida son explícitos: 8 MB y extensiones `.sql` o `.txt`.
- La concurrencia contra el modelo está limitada, lo que evita agotar la cuota del
  despliegue por accidente o por abuso.
- Los prompts prohíben al modelo inventar objetos, y el arnés de evaluación **comprueba
  automáticamente** que no lo haga. Es un control de integridad de la salida, no solo una
  instrucción.

## A07 · Authentication Failures

- ASP.NET Core Identity con bloqueo por intentos fallidos y política de contraseñas por
  omisión.
- El usuario de prueba se siembra con correo confirmado y **sus credenciales están
  publicadas a propósito** en el README, porque es un requisito de la entrega del TFM. Es
  una cuenta de demostración sin datos reales.

**Pendiente:** no hay segundo factor, y la plantilla trae un remitente de correo que no
envía nada (`IdentityNoOpEmailSender`), así que el restablecimiento de contraseña no
funciona de verdad. Aceptable en una demostración, inaceptable en producción. Anotado.

## A08 · Software and Data Integrity Failures

- El CI compila, ejecuta los tests y construye el contenedor en cada *push*.
- La imagen se construye dentro de Azure con `az acr build` a partir del código del
  repositorio, no desde una máquina local con estado desconocido.
- La infraestructura es reproducible desde Terraform.

**Pendiente:** firma de imágenes y atestación de procedencia. Es lo siguiente natural en
esta categoría y no está hecho.

## A09 · Logging and Alerting Failures

- Logging estructurado en toda la aplicación, con los fallos del modelo registrados por
  objeto y sin interrumpir el análisis.
- Log Analytics aprovisionado por Terraform y conectado al entorno de Container Apps.
- **No se registran secretos ni contenido de credenciales.** El registro de arranque dice
  qué mecanismo de autenticación se usa, nunca el valor.

**Pendiente, y es la carencia más real de esta lista:** no hay alertas. Un fallo sostenido
de las llamadas al modelo se vería en los registros si alguien los mira, y nadie los mira.
La observabilidad con OpenTelemetry es la fase 3 de la hoja de ruta.

## A10 · Mishandling of Exceptional Conditions

- El fallo al documentar un objeto se captura por objeto: se registra, ese objeto queda sin
  documentar y **la interfaz lo dice**. El análisis continúa.
- El fallo al generar el plan devuelve `null`, y la interfaz explica que el análisis
  estático sigue siendo válido.
- `OperationCanceledException` se excluye de forma explícita de los `catch`, para no
  confundir una cancelación con un error.
- Los errores de sintaxis del script se recogen y se muestran al usuario en lugar de
  romper el análisis completo.
- El manejador global de excepciones no filtra detalles internos fuera de desarrollo.

El criterio general: **degradar con información, nunca fingir éxito**. Un análisis
incompleto que dice qué falta es útil; uno que aparenta estar completo es peligroso.

---

## Lo que no aplica

**SSRF.** La aplicación no hace ninguna petición HTTP a URLs proporcionadas por el usuario.
La única salida de red es hacia el endpoint de Azure OpenAI, fijado por configuración del
despliegue.

**Deserialización insegura.** El único JSON que se deserializa es el que la propia
aplicación ha serializado antes, hacia tipos concretos y sin polimorfismo.

---

## Resumen de lo pendiente

Por orden de importancia real:

1. **Alertas** sobre los fallos de las llamadas al modelo (A09) — fase 3.
2. **Test automatizado de control de acceso** entre usuarios (A01) — fase 3.
3. **Cabeceras de seguridad y CSP** (A02) — requiere dejar de cargar Mermaid desde un CDN.
4. **Restablecimiento de contraseña funcional** y segundo factor (A07).
5. **Cifrado en reposo** del almacén, cuando haya datos que no sean de demostración (A04).
6. **Firma de imágenes** (A08).
