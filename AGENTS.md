# AGENTS.md

Instrucciones para agentes de IA que trabajen en este repositorio.

## Qué es este proyecto

Legacy Lens analiza scripts de SQL Server heredados y produce documentación y un plan de
migración. Su idea central, que condiciona todas las decisiones:

> **Lo que se puede saber con certeza, se calcula. Lo que requiere juicio, se le pregunta
> al modelo.**

Si una propuesta de cambio difumina esa frontera, es una mala propuesta aquí.

## Fronteras que no se cruzan

- **`LegacyLens.Analysis` no puede depender de `LegacyLens.Ai`.** Es lo que permite que el
  análisis estático funcione sin credenciales y sin red. Si necesitas datos del modelo
  dentro del análisis, el diseño está mal planteado.
- **`LegacyLens.Domain` no depende de nada.** Ni paquetes de terceros, ni infraestructura.
- **El SQL analizado nunca se ejecuta.** Solo se parsea. No añadas código que abra una
  conexión a la base de datos analizada, por conveniente que parezca.
- **Nada de dependencias inferidas por el modelo.** Las aristas del grafo salen del árbol
  sintáctico. Si el parser no lo ve, no entra en el grafo: se documenta como límite.

## Estructura

```
src/LegacyLens.Domain      Modelos. Sin dependencias.
src/LegacyLens.Analysis    ScriptDom. Determinista → testeable con asserts.
src/LegacyLens.Ai          Modelo de lenguaje. No determinista → se evalúa con métricas.
src/LegacyLens.Web         Blazor Web App (InteractiveServer).
tests/                     Tests del analizador.
Deploy/infra/                     Terraform.
```

## Comandos

```bash
dotnet build LegacyLens.slnx                  # compila la solución (LegacyLens.slnx)
dotnet test LegacyLens.slnx                   # 15 tests sobre el analizador
dotnet run --project src/LegacyLens.Web       # arranca en local
docker build -t legacylens .                  # valida el contenedor
cd Deploy/infra && terraform validate && terraform fmt -check -recursive
```

El CI ejecuta exactamente esas comprobaciones. Si pasan en local, pasan en CI.

## Convenciones

- **Idioma, y conviene leerlo entero porque no es uniforme:**
  - **Identificadores, nombres de fichero y rutas: en inglés.** Clases, métodos, variables,
    propiedades y las URL de las páginas. Sin excepciones.
  - **Comentarios y documentación: en español.** Son para el equipo, y traducirlos
    perdería matices en los razonamientos largos. Un comentario en español que menciona un
    identificador lo escribe con su nombre real en inglés.
  - **Texto que ve el usuario: en ficheros de recursos, nunca literal en el código.**
    `es-ES` es el idioma por omisión e `en` el alternativo.
  - **Mensajes de log: en español y sin localizar.** Decisión consciente del propietario del
    proyecto. Si algún día se quiere agregarlos o alertar sobre ellos, habrá que revisarla.
  - **En los YAML de pipeline la regla es la misma, y es fácil olvidarla:** nombres de
    trabajo, de paso, de salida, de entrada, de variable de entorno y de variable de shell
    **en inglés**, incluido el `name:` que se ve en la interfaz de Actions. Los comentarios
    siguen en español y los `echo` también, porque son log. El nombre del entorno de GitHub
    cuenta como identificador: cambiarlo obliga a rehacer las credenciales federadas, cuyo
    sujeto lo incluye.
  - El script de ejemplo `samples/legacy-erp.sql` está en español porque es **dato**, no
    código: representa un ERP heredado español y eso es parte del realismo del caso.
- **Comentarios:** explican **por qué**, nunca qué. Si un comentario parafrasea la línea
  siguiente, sobra.
- **Nada de resúmenes de cambios en el código.** No dejes comentarios del tipo «añadido
  para arreglar X»: eso es trabajo del historial de git.
- **Los tests son el criterio.** Si tocas el analizador, el test de diagnóstico
  `Volcado_diagnostico` imprime el análisis completo del script de ejemplo: úsalo para ver
  qué cambió de verdad antes de dar nada por bueno.

## Al tocar los prompts

`src/LegacyLens.Ai/Prompts.cs` es el punto más delicado del proyecto.

- Los hechos verificados se inyectan **siempre**. Un prompt que solo manda código fuente es
  un retroceso.
- La instrucción de no inventar objetos no se toca.
- Cuando un objeto usa SQL dinámico, el prompt debe seguir exigiendo que el modelo lo
  advierta. Reconocer los límites del análisis forma parte del producto.
- Cambiar un prompt sin medir el efecto es adivinar. La fase 1 de la
  [hoja de ruta](docs/hoja-de-ruta.md) introduce el arnés de evaluación precisamente para
  esto.

## Documentación que hay que mantener al día

Al añadir una funcionalidad relevante, actualiza también:

- [`docs/trazabilidad-temario.md`](docs/trazabilidad-temario.md) si cubre un contenido nuevo
- [`docs/hoja-de-ruta.md`](docs/hoja-de-ruta.md) si completa o reordena una fase
- [`docs/adr/`](docs/adr/) si la decisión es estructural y tiene alternativas descartadas
- La sección de limitaciones del README si introduce una nueva

## Lo que no se hace sin preguntar

- Aprovisionar recursos en Azure.
- Cambiar el modelo desplegado o la región.
- Introducir un proveedor de IA nuevo.
- Añadir una dependencia que arrastre medio ecosistema para resolver diez líneas.
