# CLAUDE.md

Las instrucciones de este repositorio están en **[AGENTS.md](AGENTS.md)**. Léelo antes de
tocar nada: contiene la idea que sostiene el proyecto, las fronteras que no se cruzan, las
convenciones de idioma y de código, y lo que no se hace sin preguntar.

Este fichero no las repite. Duplicar una convención garantiza que las dos copias se separen,
y ya ha pasado en este proyecto con la cifra de tests, que llegó a estar desfasada en seis
sitios a la vez. `AGENTS.md` es la fuente; aquí solo va lo que es específico de Claude Code.

## Skills disponibles

En `.claude/skills/` hay tres, y cada una existe porque su tarea se repite y tiene trampas
propias de este repositorio:

| Skill | Cuándo |
| --- | --- |
| `add-use-case` | añadir un comando o una consulta a la capa de aplicación |
| `add-page` | añadir una página Blazor localizada |
| `add-test` | añadir un test al proyecto de pruebas |

No hay skills para crear la estructura de la solución ni para añadir el proyecto MCP: son
tareas de una sola vez y ya están hechas. Una skill que no se va a volver a usar es
documentación disfrazada.

## Herramientas

- **No uses PowerShell para modificar ficheros de código.** Da problemas de codificación con
  los acentos y choca con el búfer de Visual Studio, que suele tener el fichero abierto. Usa
  Edit y Write. Para compilar, probar o ejecutar `git` sí es válido.
- **Cuidado con las barras invertidas en los scripts intermedios.** En este repositorio se
  han corrompido ficheros dos veces al pasar cadenas con `\` a través de un *heredoc*, y una
  tercera escribiendo un carácter de control sin darse cuenta. Si hace falta un script,
  escríbelo con Write y ejecútalo, en lugar de incrustarlo en el comando.
- **Comprobar la interfaz exige `dotnet publish`, no `bin/`.** Ejecutar la aplicación desde
  `bin/Release` devuelve 500 en todos los recursos estáticos, `blazor.web.js` incluido,
  porque el manejador de desarrollo busca los `.razor.js` con ámbito en una ruta que solo
  existe en el árbol de fuentes. El publicado los sirve bien, y es lo que usa Docker.
- **Si añades una carpeta al repositorio, añádela al `Dockerfile`.** Copia carpetas concretas
  y no el árbol entero, para que la capa de restauración se reutilice. Un glob del csproj que
  apunta a una carpeta no copiada **no da ningún error**: el publish termina bien, el CI pasa
  y el fichero no está en la imagen. Ya pasó con el logo.

## Verificar de verdad

Un `dotnet build` limpio y un 200 en la portada no demuestran que algo funcione. En este
proyecto han llegado al usuario tres fallos que pasaron todas las comprobaciones previas: la
imagen sin el runtime de Blazor, un acordeón que necesitaba JavaScript que no se cargaba, y
el logo ausente de la imagen.

- Si el cambio toca el empaquetado o la estructura de proyectos: `docker build` y arrancar el
  contenedor.
- Si el cambio es visual: una captura. `chrome --headless --screenshot` contra el publicado
  sirve, y es la única forma de ver un color o un salto de línea.
- Si el cambio es de interfaz: ejercitar el control, no leer el marcado.
