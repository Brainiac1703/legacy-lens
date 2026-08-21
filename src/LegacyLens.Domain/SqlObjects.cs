namespace LegacyLens.Domain;

/// <summary>Tipo de objeto de base de datos detectado por el analizador.</summary>
public enum SqlObjectKind
{
    Table,
    View,
    Procedure,
    Function,
    Trigger
}

/// <summary>Naturaleza de la relación entre dos objetos.</summary>
public enum DependencyKind
{
    /// <summary>El origen lee datos del destino.</summary>
    Reads,
    /// <summary>El origen escribe en el destino (INSERT/UPDATE/DELETE/MERGE/SELECT INTO).</summary>
    Writes,
    /// <summary>El origen ejecuta el destino (EXEC).</summary>
    Calls
}

/// <summary>
/// Arista del grafo de dependencias. Se obtiene del árbol sintáctico real,
/// nunca de una inferencia del modelo de lenguaje.
/// </summary>
public sealed record Dependency(string From, string To, DependencyKind Kind);

/// <summary>Un objeto de base de datos con sus métricas, riesgo y documentación.</summary>
public sealed class SqlObject
{
    public required string Name { get; init; }
    public required string Schema { get; init; }
    public required SqlObjectKind Kind { get; init; }

    /// <summary>Texto T-SQL original del objeto, tal cual aparece en el script.</summary>
    public required string Body { get; init; }

    public CodeMetrics Metrics { get; set; } = CodeMetrics.Empty;
    public RiskScore Risk { get; set; } = RiskScore.None;

    /// <summary>Documentación generada por IA. Nula hasta que se ejecuta esa fase.</summary>
    public ObjectDocumentation? Documentation { get; set; }

    /// <summary>Nombre cualificado con esquema, tal como se usa en el grafo.</summary>
    public string FullName => $"{Schema}.{Name}";

    public bool IsProgrammable => Kind is SqlObjectKind.Procedure or SqlObjectKind.Function or SqlObjectKind.Trigger;
}
