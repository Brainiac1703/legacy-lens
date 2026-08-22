---
name: add-use-case
description: Añadir un comando o una consulta a la capa de aplicación de Legacy Lens, con su validador y su handler. Úsala cuando haya que exponer una operación nueva a la web o al servidor MCP.
---

# Añadir un caso de uso

Un caso de uso en este proyecto son **tres tipos en un mismo fichero**: la petición, su
validador y su handler. Van juntos porque se leen juntos y porque separarlos obliga a abrir
tres ficheros para entender una operación.

## Dónde

`src/LegacyLens.Application/<Área>/<NombreDelCasoDeUso>.cs`

Las áreas que ya existen son `Analyses` —lo que produce un análisis— y `Knowledge` —lo que
se pregunta sobre uno ya hecho—. Si lo nuevo no encaja en ninguna, crea una carpeta antes de
meterlo a la fuerza en la que menos se parezca.

## El patrón

```csharp
/// <summary>
/// Devuelve nulo si no existe o si pertenece a otro usuario. No se distinguen los
/// dos casos a propósito: hacerlo revelaría qué identificadores existen.
/// </summary>
public sealed record FindThingQuery(Guid AnalysisId, string OwnerUserId, string Name)
    : IRequest<ThingCard?>;

public sealed class FindThingValidator : AbstractValidator<FindThingQuery>
{
    public FindThingValidator(IStringLocalizer<ValidationText> localizer)
    {
        RuleFor(x => x.AnalysisId).NotEmpty();
        RuleFor(x => x.OwnerUserId).NotEmpty();
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(_ => localizer["Name_Missing"])
            .MaximumLength(256);
    }
}

public sealed class FindThingHandler(IAnalysisRepository repository)
    : IRequestHandler<FindThingQuery, ThingCard?>
{
    public async Task<ThingCard?> Handle(FindThingQuery request, CancellationToken cancellationToken)
    {
        var analysis = await repository.GetAsync(request.AnalysisId, request.OwnerUserId, cancellationToken);
        return analysis is null ? null : /* ... */;
    }
}
```

## Lo que se registra solo, y lo que no

**No hay que registrar nada.** `AddApplication` escanea el ensamblado: `RegisterServicesFromAssembly`
recoge los handlers y `AddValidatorsFromAssembly(..., includeInternalTypes: true)` los
validadores. Si tu handler no se ejecuta, el problema es otro; revisa la firma antes de tocar
`DependencyInjection.cs`.

## Reglas que este proyecto sí impone

- **El caso de uso no produce texto para el usuario.** Devuelve datos; el mensaje lo compone
  la presentación. Hubo un handler que devolvía una cadena en español y con eso un caso de
  uso decidía la redacción de la interfaz.
- **El identificador del propietario es un parámetro de la petición, no algo que el handler
  averigüe.** La regla de que cada usuario ve solo lo suyo vive en la firma del repositorio;
  no la esquives.
- **Los mensajes de validación van en `ValidationText.resx`** —los dos idiomas— y se leen con
  `IStringLocalizer<ValidationText>`. Las reglas estructurales (`NotEmpty` sobre un `Guid`
  interno) no necesitan mensaje: nunca las va a ver una persona.
- **Nada de lógica de grafo aquí.** Los recorridos están en `LegacyLens.Domain.DependencyGraph`,
  que es puro y tiene tests. Si necesitas uno nuevo, añádelo allí con su test.
- **`async`/`await` de principio a fin.** Ni `.Result` ni `.Wait()`.

## Después

- Un test del recorrido si has añadido lógica al dominio. La skill `add-test` lo cubre.
- Si el caso de uso se expone por MCP, la herramienta va en `src/LegacyLens.Mcp/KnowledgeTools.cs`
  y **solo traduce**: resolver el propietario, mandar la petición y serializar. Si aparece
  lógica en el servidor MCP, está en el sitio equivocado.
