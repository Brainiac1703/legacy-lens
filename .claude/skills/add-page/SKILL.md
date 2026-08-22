---
name: add-page
description: Añadir una página Blazor a Legacy Lens con su ruta, su autorización y sus textos en los dos idiomas. Úsala cuando haya que crear una pantalla nueva del producto.
---

# Añadir una página

## Dónde y con qué nombre

`src/LegacyLens.Web/Components/Pages/<Name>.razor`

**El nombre del fichero y la ruta van en inglés**, sin excepción. Hubo una página
`Analisis.razor` con ruta `/analizar` y hubo que renombrarla, lo que dejó inservible un guion
de vídeo ya escrito y obligó a rehacer credenciales de nada. Las rutas actuales son
`/analyze` y `/analyses`.

## El patrón

```razor
@page "/things"
@using LegacyLens.Application.Analyses
@using MediatR
@using Microsoft.AspNetCore.Authorization
@using Microsoft.Extensions.Localization
@attribute [Authorize]
@rendermode InteractiveServer

@inject ISender Sender
@inject IStringLocalizer<UiText> L

<PageTitle>@L["Things_Title"] | Legacy Lens</PageTitle>

<h1>@L["Things_Title"]</h1>
```

- **`@inject` es lo correcto aquí.** No hay code-behind en este repositorio y no se va a
  introducir; está razonado en `AGENTS.md`.
- **`InteractiveServer`, no `Auto`.** Está decidido en el
  [ADR 0002](../../../docs/adr/0002-blazor-interactive-server.md): con `Auto` la primera
  visita se comporta distinto a las siguientes y eso complica el diagnóstico sin aportar nada
  a este caso.
- **`ISender`, nunca el repositorio ni el `DbContext`.** La presentación manda una petición y
  recorre la respuesta. Si necesitas algo que no existe como caso de uso, créalo con la skill
  `add-use-case`.

## Los textos

Ninguna cadena visible se escribe en el `.razor`. Van en **los dos** ficheros:

- `src/LegacyLens.Web/Resources/UiText.resx` — español, que es el idioma neutro
- `src/LegacyLens.Web/Resources/UiText.en.resx` — inglés

Convención de claves: `<Área>_<Qué>`, como `Analyze_SampleButton` o `Detail_TileObjects`.
Manténlas en orden alfabético; el fichero se lee y se compara a mano más de lo que parece.

**El español vive en el `.resx` neutro y no en un `es-ES.resx`.** Así una cultura sin
traducir cae en español en lugar de mostrar el nombre de la clave.

Los `echo` y los mensajes de log **no** se localizan: van en español y sin recurso, por
decisión del propietario.

## Si la página va en el menú

`Components/Layout/NavMenu.razor`, con una clave `Nav_*` en los dos `.resx`. El menú lateral
son destinos a los que ir; las preferencias globales, como el idioma, van en la barra
superior.

## Estilos

Nada en el atributo `style`. Si el estilo es solo de esta página, un `<Name>.razor.css` al
lado, que Blazor aísla por componente. Si es global, `wwwroot/app.css`. La paleta está en
variables al principio de ese fichero; usa las que hay antes de inventar un color.

## Verificar

Un 200 no demuestra nada: hubo una versión en la que todas las páginas respondían 200 y
ningún botón funcionaba, porque a la imagen le faltaba el runtime de Blazor.

- Arranca contra el **publicado**, no contra `bin/`: desde `bin/Release` los recursos
  estáticos devuelven 500 y verás la página sin CSS.
- Para lo visual, una captura: `chrome --headless --screenshot`.
- Si has añadido un control, púlsalo. Leer el marcado no cuenta.
