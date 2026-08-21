namespace LegacyLens.Domain;

/// <summary>
/// Interpretación generada por el modelo de lenguaje. Es lo único del
/// análisis que no es determinista, y por eso se guarda por separado
/// de las métricas y se etiqueta con el modelo que la produjo.
/// </summary>
public sealed record ObjectDocumentation(
    string Summary,
    IReadOnlyList<string> BusinessRules,
    IReadOnlyList<string> SideEffects,
    string MigrationTarget,
    string ModelUsed);

/// <summary>Una fase del plan de migración.</summary>
public sealed record MigrationPhase(
    int Order,
    string Title,
    string Rationale,
    IReadOnlyList<string> Objects,
    string Risk);

/// <summary>
/// Plan de migración global. Se genera con un modelo más capaz porque es
/// la única decisión que requiere razonar sobre el grafo completo.
/// </summary>
public sealed record MigrationPlan(
    string Overview,
    IReadOnlyList<MigrationPhase> Phases,
    IReadOnlyList<string> GlobalRisks,
    string ModelUsed);

/// <summary>
/// Consumo de un modelo durante un análisis. Se guardan tokens y llamadas, no
/// dinero: los precios cambian y no son un hecho del dominio. El importe se
/// calcula en la capa de presentación con los precios configurados.
/// </summary>
public sealed record ModelUsage(string Model, long InputTokens, long OutputTokens, int Calls);

/// <summary>Resultado completo de analizar un script.</summary>
public sealed class AnalysisResult
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string SourceFileName { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public List<SqlObject> Objects { get; init; } = [];
    public List<Dependency> Dependencies { get; init; } = [];
    public List<string> ParseErrors { get; init; } = [];

    public MigrationPlan? Plan { get; set; }

    /// <summary>Consumo de cada modelo en este análisis concreto.</summary>
    public List<ModelUsage> Usage { get; init; } = [];

    public int ObjectCount => Objects.Count;

    /// <summary>
    /// Objetos que no llaman a ningún otro: son autocontenidos y por eso los
    /// candidatos naturales a migrar primero con un patrón strangler fig.
    /// </summary>
    public IEnumerable<SqlObject> Leaves =>
        Objects.Where(o => o.IsProgrammable &&
                           !Dependencies.Any(d => d.Kind == DependencyKind.Calls && d.From == o.FullName));

    /// <summary>
    /// Objetos a los que nadie llama: son los puntos de entrada del sistema
    /// (procesos programados, pantallas, informes). Migrarlos primero rompería
    /// menos cosas, pero exige tener migrado antes todo lo que cuelga de ellos.
    /// </summary>
    public IEnumerable<SqlObject> EntryPoints =>
        Objects.Where(o => o.IsProgrammable &&
                           !Dependencies.Any(d => d.Kind == DependencyKind.Calls && d.To == o.FullName));

    /// <summary>Objetos más referenciados: tocarlos es lo más peligroso.</summary>
    public IEnumerable<(SqlObject Object, int Referrers)> Hubs =>
        Objects.Where(o => o.IsProgrammable)
               .Select(o => (Object: o,
                             Referrers: Dependencies.Count(d => d.Kind == DependencyKind.Calls && d.To == o.FullName)))
               .Where(x => x.Referrers > 0)
               .OrderByDescending(x => x.Referrers);
}
