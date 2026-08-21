using LegacyLens.Domain;

namespace LegacyLens.Analysis;

/// <summary>
/// Convierte métricas en una puntuación de riesgo de migración.
///
/// Los pesos son deliberadamente explícitos y cada punto asignado lleva su
/// motivo: la puntuación tiene que poder discutirse con el cliente, y para eso
/// no puede salir de una caja negra.
/// </summary>
public static class RiskScorer
{
    public static RiskScore Score(CodeMetrics m)
    {
        var factors = new List<RiskFactor>();

        if (m.CursorCount > 0)
            factors.Add(new RiskFactor(
                "CURSOR",
                $"Usa {m.CursorCount} cursor(es): lógica fila a fila que hay que replantear, no traducir.",
                Math.Min(30, m.CursorCount * 15)));

        if (m.DynamicSqlCount > 0)
            factors.Add(new RiskFactor(
                "DYNAMIC_SQL",
                $"Construye SQL dinámico en {m.DynamicSqlCount} punto(s): las dependencias reales no son analizables estáticamente.",
                Math.Min(40, m.DynamicSqlCount * 20)));

        if (m.WritesWithoutTransaction)
            factors.Add(new RiskFactor(
                "NO_TRANSACTION",
                $"Escribe en {m.TablesWritten} tabla(s) sin transacción explícita: puede dejar datos inconsistentes.",
                25));

        if (m.TablesWritten > 0 && !m.HasErrorHandling)
            factors.Add(new RiskFactor(
                "NO_ERROR_HANDLING",
                "Modifica datos sin TRY/CATCH: los fallos se propagan sin control.",
                15));

        if (m.Lines > 500)
            factors.Add(new RiskFactor("VERY_LONG", $"{m.Lines} líneas en un solo objeto.", 20));
        else if (m.Lines > 200)
            factors.Add(new RiskFactor("LONG", $"{m.Lines} líneas: por encima de lo razonable para revisar de una vez.", 10));

        if (m.ControlFlowComplexity > 25)
            factors.Add(new RiskFactor("VERY_COMPLEX", $"Complejidad de control {m.ControlFlowComplexity}: muchas ramas que cubrir con pruebas.", 20));
        else if (m.ControlFlowComplexity > 10)
            factors.Add(new RiskFactor("COMPLEX", $"Complejidad de control {m.ControlFlowComplexity}.", 10));

        var tablesTouched = m.TablesRead + m.TablesWritten;
        if (tablesTouched > 8)
            factors.Add(new RiskFactor("WIDE_SURFACE", $"Toca {tablesTouched} tablas: acopla muchas áreas del modelo.", 10));

        if (m.ObjectsCalled > 3)
            factors.Add(new RiskFactor("CHAINED_CALLS", $"Invoca {m.ObjectsCalled} objetos programables: la lógica está repartida.", 5));

        if (m.TempTableCount > 3)
            factors.Add(new RiskFactor("TEMP_TABLES", $"Usa {m.TempTableCount} tablas temporales: proceso por etapas difícil de seguir.", 5));

        var value = Math.Min(100, factors.Sum(f => f.Points));
        return new RiskScore(value, LevelFor(value), factors);
    }

    private static RiskLevel LevelFor(int value) => value switch
    {
        < 20 => RiskLevel.Low,
        < 45 => RiskLevel.Medium,
        < 70 => RiskLevel.High,
        _ => RiskLevel.Critical
    };
}
