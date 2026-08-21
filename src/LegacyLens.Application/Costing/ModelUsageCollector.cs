using System.Collections.Concurrent;
using LegacyLens.Application.Abstractions;
using LegacyLens.Domain;

namespace LegacyLens.Application.Costing;

/// <summary>
/// Acumulador de consumo de un análisis, desglosado por modelo.
///
/// Es una clase de la capa de aplicación y no de la de IA porque no tiene nada
/// que ver con hablar con un proveedor: es contabilidad del caso de uso. La capa
/// de IA solo recibe el interface y anota lo que consume.
///
/// El desglose por modelo no es un adorno: el sistema usa dos modelos con
/// precios muy distintos, así que un total agregado no permitiría estimar el
/// coste ni comprobar que cada modelo hace el trabajo que le toca.
/// </summary>
public sealed class ModelUsageCollector : IModelUsageCollector
{
    private sealed class Entrada
    {
        public long InputTokens;
        public long OutputTokens;
        public int Calls;
    }

    private readonly ConcurrentDictionary<string, Entrada> _porModelo = new();

    public void Add(string model, long inputTokens, long outputTokens)
    {
        var entrada = _porModelo.GetOrAdd(model, _ => new Entrada());

        Interlocked.Add(ref entrada.InputTokens, inputTokens);
        Interlocked.Add(ref entrada.OutputTokens, outputTokens);
        Interlocked.Increment(ref entrada.Calls);
    }

    public IReadOnlyList<ModelUsage> Snapshot() =>
        [.. _porModelo.Select(kv => new ModelUsage(
            kv.Key,
            Interlocked.Read(ref kv.Value.InputTokens),
            Interlocked.Read(ref kv.Value.OutputTokens),
            Volatile.Read(ref kv.Value.Calls)))];
}
