using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LegacyLens.Application.Common.Behaviours;

/// <summary>
/// Registra cada petición con su duración y su resultado.
///
/// Dos decisiones que merecen explicación:
///
/// No se registra el contenido de la petición. Los comandos de este sistema
/// llevan scripts SQL completos, que es código de la base de datos de alguien;
/// volcarlo al log lo duplicaría en un sitio con otra política de retención y
/// otros permisos.
///
/// Se avisa de las peticiones lentas con un umbral. No pretende sustituir a la
/// observabilidad, pero deja rastro de la degradación en los mismos registros
/// que ya se consultan cuando algo va mal.
/// </summary>
public sealed class LoggingBehaviour<TRequest, TResponse>(
    ILogger<LoggingBehaviour<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const int UmbralLentoMs = 3_000;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var nombre = typeof(TRequest).Name;
        var reloj = Stopwatch.StartNew();

        logger.LogInformation("Ejecutando {Peticion}", nombre);

        try
        {
            var respuesta = await next();
            reloj.Stop();

            if (reloj.ElapsedMilliseconds > UmbralLentoMs)
                logger.LogWarning(
                    "{Peticion} completada en {Milisegundos} ms, por encima del umbral de {Umbral} ms",
                    nombre, reloj.ElapsedMilliseconds, UmbralLentoMs);
            else
                logger.LogInformation(
                    "{Peticion} completada en {Milisegundos} ms", nombre, reloj.ElapsedMilliseconds);

            return respuesta;
        }
        catch (ValidationException ex)
        {
            reloj.Stop();

            // Una petición inválida no es un fallo del sistema: se registra como
            // aviso y sin traza, que solo añadiría ruido.
            logger.LogWarning(
                "{Peticion} rechazada por validación tras {Milisegundos} ms: {Errores}",
                nombre, reloj.ElapsedMilliseconds, ex.Message);
            throw;
        }
        catch (OperationCanceledException)
        {
            reloj.Stop();
            logger.LogInformation(
                "{Peticion} cancelada tras {Milisegundos} ms", nombre, reloj.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            reloj.Stop();
            logger.LogError(ex,
                "{Peticion} falló tras {Milisegundos} ms", nombre, reloj.ElapsedMilliseconds);
            throw;
        }
    }
}

/// <summary>
/// Equivalente para peticiones que devuelven un flujo.
///
/// Aquí la duración total no dice mucho —el flujo dura lo que el consumidor
/// tarde en recorrerlo—, así que lo que se registra es el número de elementos
/// emitidos, que es lo que permite distinguir un flujo que terminó de uno que
/// se cortó a mitad.
/// </summary>
public sealed class StreamLoggingBehaviour<TRequest, TResponse>(
    ILogger<StreamLoggingBehaviour<TRequest, TResponse>> logger)
    : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nombre = typeof(TRequest).Name;
        var reloj = Stopwatch.StartNew();
        var emitidos = 0;

        logger.LogInformation("Iniciando el flujo {Peticion}", nombre);

        try
        {
            await foreach (var elemento in next().WithCancellation(cancellationToken))
            {
                emitidos++;
                yield return elemento;
            }
        }
        finally
        {
            // En finally para que también quede rastro cuando el consumidor
            // abandona el flujo o se cancela a mitad.
            reloj.Stop();
            logger.LogInformation(
                "Flujo {Peticion} terminado: {Emitidos} elemento(s) en {Milisegundos} ms",
                nombre, emitidos, reloj.ElapsedMilliseconds);
        }
    }
}
