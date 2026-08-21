using FluentValidation;
using MediatR;

namespace LegacyLens.Application.Common.Behaviours;

/// <summary>
/// Valida la petición antes de que llegue a su handler.
///
/// El beneficio de hacerlo en la pipeline y no dentro de cada handler es que la
/// validación deja de ser algo que hay que acordarse de invocar: si existe un
/// validador para una petición, se ejecuta siempre. Y si no existe, esto no
/// hace nada, así que no obliga a escribir validadores vacíos.
/// </summary>
public sealed class ValidationBehaviour<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var failures = await Validate(validators, request, cancellationToken);

        if (failures.Count > 0) throw new ValidationException(failures);

        return await next();
    }

    /// <summary>
    /// Compartido con la variante de streaming: la lógica de validación es la
    /// misma y no tiene sentido duplicarla por el tipo de respuesta.
    /// </summary>
    internal static async Task<Dictionary<string, string[]>> Validate(
        IEnumerable<IValidator<TRequest>> validators,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var list = validators.ToList();
        if (list.Count == 0) return [];

        var context = new ValidationContext<TRequest>(request);

        var results = await Task.WhenAll(
            list.Select(v => v.ValidateAsync(context, cancellationToken)));

        return results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.ToArray());
    }
}

/// <summary>Misma validación para las peticiones que devuelven un flujo.</summary>
public sealed class StreamValidationBehaviour<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var failures = await ValidationBehaviour<TRequest, TResponse>
            .Validate(validators, request, cancellationToken);

        if (failures.Count > 0) throw new ValidationException(failures);

        await foreach (var item in next().WithCancellation(cancellationToken))
            yield return item;
    }
}
