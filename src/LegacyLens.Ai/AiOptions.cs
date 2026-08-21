using System.Collections.Concurrent;
using LegacyLens.Domain;

namespace LegacyLens.Ai;

/// <summary>
/// Configuración de la capa de IA.
///
/// Se usan dos modelos distintos a propósito: documentar cada objeto es trabajo
/// repetitivo y de contexto corto, mientras que el plan de migración es una
/// sola decisión que exige razonar sobre el grafo completo. Pagar el modelo
/// grande cincuenta veces para lo primero no aporta nada.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Endpoint del recurso de Azure OpenAI.</summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Clave del recurso, solo para desarrollo local. Si se deja vacía se usa
    /// la identidad de Azure del entorno, que es como funciona en producción.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Despliegue económico, para documentar objeto a objeto.</summary>
    public string DocumentationDeployment { get; set; } = "gpt-4.1-mini";

    /// <summary>Despliegue más capaz, solo para el plan de migración global.</summary>
    public string PlanningDeployment { get; set; } = "gpt-4o";

    /// <summary>Llamadas simultáneas al modelo. Protege la cuota del despliegue.</summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>
    /// Recorta el cuerpo enviado al modelo. Un procedimiento de 3.000 líneas no
    /// mejora la documentación, solo la factura.
    /// </summary>
    public int MaxBodyCharacters { get; set; } = 12_000;

    /// <summary>
    /// Precios por modelo, indexados por nombre de despliegue. Si un modelo no
    /// aparece aquí, la aplicación muestra los tokens pero no estima importe:
    /// es preferible no decir nada a inventarse una cifra.
    /// </summary>
    public Dictionary<string, ModelPricing> Pricing { get; set; } = [];

    /// <summary>
    /// La aplicación funciona sin IA: el análisis estático es independiente.
    /// Esto permite ejecutarla en local sin credenciales y sin fingir nada.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint);

    /// <summary>
    /// Sin clave configurada se usa la identidad del entorno: identidad
    /// administrada en Azure, o la sesión de az login en desarrollo. Así no
    /// hay ningún secreto que guardar ni rotar.
    /// </summary>
    public bool UsesManagedIdentity => IsConfigured && string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Precio de un modelo, en dólares por millón de tokens.
///
/// Los precios están en configuración y no en el código porque cambian, y porque
/// dependen de la región y del tipo de despliegue. Los valores por omisión son
/// orientativos: el importe que muestra la aplicación es siempre una estimación.
/// </summary>
public sealed class ModelPricing
{
    public decimal InputPerMillion { get; set; }
    public decimal OutputPerMillion { get; set; }

    public decimal Estimate(long inputTokens, long outputTokens) =>
        inputTokens / 1_000_000m * InputPerMillion +
        outputTokens / 1_000_000m * OutputPerMillion;
}

/// <summary>
/// Consumo acumulado del proceso, desglosado por modelo.
///
/// El desglose por modelo no es un adorno: el proyecto usa dos modelos con precios
/// muy distintos, así que un total agregado no permitiría estimar el coste ni
/// comprobar que cada modelo hace el trabajo que le corresponde.
/// </summary>
public sealed class AiUsage
{
    private readonly ConcurrentDictionary<string, Entry> _byModel = new();

    private sealed class Entry
    {
        public long InputTokens;
        public long OutputTokens;
        public int Calls;
    }

    public void Add(string model, long input, long output)
    {
        var entry = _byModel.GetOrAdd(model, _ => new Entry());

        Interlocked.Add(ref entry.InputTokens, input);
        Interlocked.Add(ref entry.OutputTokens, output);
        Interlocked.Increment(ref entry.Calls);
    }

    public long InputTokens => _byModel.Values.Sum(e => Interlocked.Read(ref e.InputTokens));
    public long OutputTokens => _byModel.Values.Sum(e => Interlocked.Read(ref e.OutputTokens));
    public int Calls => _byModel.Values.Sum(e => Volatile.Read(ref e.Calls));

    public IReadOnlyList<ModelUsage> Snapshot() =>
        [.. _byModel.Select(kv => new ModelUsage(
            kv.Key,
            Interlocked.Read(ref kv.Value.InputTokens),
            Interlocked.Read(ref kv.Value.OutputTokens),
            Volatile.Read(ref kv.Value.Calls)))];
}

/// <summary>
/// Consumo de un único análisis.
///
/// Existe además del acumulado del proceso porque la aplicación necesita decir
/// cuánto costó **este** análisis, y varios pueden estar ejecutándose a la vez.
/// </summary>
public sealed class AiRunUsage
{
    private readonly AiUsage _usage = new();

    public void Add(string model, long input, long output) => _usage.Add(model, input, output);

    public IReadOnlyList<ModelUsage> Snapshot() => _usage.Snapshot();
}
