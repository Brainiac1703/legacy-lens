namespace LegacyLens.Domain;

/// <summary>
/// Métricas calculadas por recorrido del árbol sintáctico. Todas son
/// deterministas y reproducibles: dos ejecuciones sobre el mismo script
/// devuelven exactamente los mismos números.
/// </summary>
public sealed record CodeMetrics(
    int Lines,
    int StatementCount,
    int CursorCount,
    int DynamicSqlCount,
    int TransactionCount,
    int TempTableCount,
    bool HasErrorHandling,
    int ControlFlowComplexity,
    int TablesRead,
    int TablesWritten,
    int ObjectsCalled)
{
    public static readonly CodeMetrics Empty =
        new(0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0);

    /// <summary>Escribe en tablas sin abrir ninguna transacción explícita.</summary>
    public bool WritesWithoutTransaction => TablesWritten > 0 && TransactionCount == 0;
}

public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Un motivo concreto que suma puntos de riesgo. Se guarda para que la
/// puntuación sea auditable: el usuario ve siempre de dónde sale el número.
/// </summary>
public sealed record RiskFactor(string Code, string Description, int Points);

/// <summary>Riesgo de migración de un objeto, con su justificación completa.</summary>
public sealed record RiskScore(int Value, RiskLevel Level, IReadOnlyList<RiskFactor> Factors)
{
    public static readonly RiskScore None = new(0, RiskLevel.Low, []);
}
