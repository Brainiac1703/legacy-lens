namespace LegacyLens.Application.Common;

/// <summary>
/// Errores de validación de una petición, agrupados por propiedad.
///
/// Es propia y no la de FluentValidation a propósito: así la capa de
/// presentación puede tratar los errores de validación sin conocer la librería
/// que los produjo.
/// </summary>
public sealed class ValidationException : Exception
{
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base(Describe(errors))
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    private static string Describe(IReadOnlyDictionary<string, string[]> errors) =>
        errors.Count == 0
            ? "La petición no es válida."
            : string.Join(" ", errors.SelectMany(e => e.Value));
}
