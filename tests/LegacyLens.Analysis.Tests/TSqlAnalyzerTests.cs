using System.Text;
using LegacyLens.Domain;

// ITestOutputHelper llega por el using implícito de Xunit que declara el csproj:
// en la v3 vive en el espacio de nombres Xunit y no en Xunit.Abstractions.
namespace LegacyLens.Analysis.Tests;

public class TSqlAnalyzerTests
{
    private readonly ITestOutputHelper _output;
    private readonly AnalysisResult _result;

    public TSqlAnalyzerTests(ITestOutputHelper output)
    {
        _output = output;
        var script = File.ReadAllText(SampleScriptPath());
        _result = new TSqlAnalyzer().Analyze(script, "legacy-erp.sql");
    }

    private static string SampleScriptPath()
    {
        // Sube desde bin/Debug/netX.0 hasta la raíz del repositorio.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "samples")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "samples", "legacy-erp.sql");
    }

    private SqlObject Object(string name) =>
        _result.Objects.Single(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Diagnostic_dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Errores de parseo: {_result.ParseErrors.Count}");
        foreach (var e in _result.ParseErrors) sb.AppendLine($"  {e}");

        sb.AppendLine($"\nObjetos: {_result.ObjectCount}");
        foreach (var group in _result.Objects.GroupBy(o => o.Kind))
            sb.AppendLine($"  {group.Key}: {group.Count()} -> {string.Join(", ", group.Select(o => o.Name))}");

        sb.AppendLine("\nMétricas y riesgo de los objetos programables:");
        foreach (var o in _result.Objects.Where(o => o.IsProgrammable).OrderByDescending(o => o.Risk.Value))
        {
            var m = o.Metrics;
            sb.AppendLine($"\n  {o.FullName}  [riesgo {o.Risk.Value} / {o.Risk.Level}]");
            sb.AppendLine($"    lineas={m.Lines} sentencias={m.StatementCount} cursores={m.CursorCount} " +
                          $"dinamico={m.DynamicSqlCount} transacciones={m.TransactionCount} " +
                          $"temporales={m.TempTableCount} trycatch={m.HasErrorHandling} " +
                          $"complejidad={m.ControlFlowComplexity}");
            sb.AppendLine($"    lee={m.TablesRead} escribe={m.TablesWritten} invoca={m.ObjectsCalled}");
            foreach (var f in o.Risk.Factors)
                sb.AppendLine($"    +{f.Points} {f.Code}: {f.Description}");
        }

        sb.AppendLine($"\nDependencias: {_result.Dependencies.Count}");
        foreach (var d in _result.Dependencies.OrderBy(d => d.From).ThenBy(d => d.Kind))
            sb.AppendLine($"  {d.From} --{d.Kind}--> {d.To}");

        sb.AppendLine("\nHojas (no llaman a nadie, candidatas a migrar primero):");
        foreach (var leaf in _result.Leaves) sb.AppendLine($"  {leaf.FullName}");

        sb.AppendLine("\nPuntos de entrada (nadie los llama):");
        foreach (var entry in _result.EntryPoints) sb.AppendLine($"  {entry.FullName}");

        sb.AppendLine("\nNudos (mas referenciados):");
        foreach (var (obj, refs) in _result.Hubs) sb.AppendLine($"  {obj.FullName} <- {refs} referencia(s)");

        _output.WriteLine(sb.ToString());
    }

    [Fact]
    public void Parses_the_script_without_errors()
    {
        Assert.Empty(_result.ParseErrors);
    }

    [Fact]
    public void Detects_every_object_kind()
    {
        Assert.Equal(10, _result.Objects.Count(o => o.Kind == SqlObjectKind.Table));
        Assert.Equal(1, _result.Objects.Count(o => o.Kind == SqlObjectKind.View));
        Assert.Equal(1, _result.Objects.Count(o => o.Kind == SqlObjectKind.Function));
        Assert.Equal(6, _result.Objects.Count(o => o.Kind == SqlObjectKind.Procedure));
        Assert.Equal(1, _result.Objects.Count(o => o.Kind == SqlObjectKind.Trigger));
    }

    [Fact]
    public void Tells_reads_from_writes()
    {
        var closeOrder = Object("usp_CerrarPedido");

        var writes = _result.Dependencies
            .Where(d => d.From == closeOrder.FullName && d.Kind == DependencyKind.Writes)
            .Select(d => d.To)
            .ToList();

        Assert.Contains("dbo.Facturas", writes);
        Assert.Contains("dbo.LineasFactura", writes);
        Assert.Contains("dbo.Pedidos", writes);
        Assert.Contains("dbo.Auditoria", writes);

        var reads = _result.Dependencies
            .Where(d => d.From == closeOrder.FullName && d.Kind == DependencyKind.Reads)
            .Select(d => d.To)
            .ToList();

        // Lee de LineasPedido y Clientes, pero nunca escribe en ellas.
        Assert.Contains("dbo.LineasPedido", reads);
        Assert.Contains("dbo.Clientes", reads);
        Assert.DoesNotContain("dbo.LineasPedido", writes);
    }

    [Fact]
    public void Detects_calls_between_procedures()
    {
        var calls = _result.Dependencies.Where(d => d.Kind == DependencyKind.Calls).ToList();

        Assert.Contains(calls, d =>
            d.From == "dbo.usp_CerrarPedido" && d.To == "dbo.usp_RegistrarMovimientoStock");

        Assert.Contains(calls, d =>
            d.From == "dbo.usp_FacturarPedidosPendientes" && d.To == "dbo.usp_CerrarPedido");
    }

    [Fact]
    public void Detects_scalar_functions_used_inside_expressions()
    {
        // La función no se invoca con EXEC, sino dentro de un SET. Sigue siendo
        // una dependencia real y tiene que estar en el grafo.
        Assert.Contains(_result.Dependencies, d =>
            d.From == "dbo.usp_CerrarPedido" &&
            d.To == "dbo.fn_CalcularDescuento" &&
            d.Kind == DependencyKind.Calls);

        // Y por tanto la función ya no es un punto de entrada.
        Assert.DoesNotContain(_result.EntryPoints, o => o.Name == "fn_CalcularDescuento");
    }

    [Fact]
    public void Does_not_mistake_builtin_functions_for_dependencies()
    {
        // El script usa GETDATE, COUNT, ISNULL, DATEDIFF, CAST y SUSER_SNAME.
        // Ninguna es un objeto del esquema.
        foreach (var builtin in new[] { "GETDATE", "COUNT", "ISNULL", "DATEDIFF", "SUSER_SNAME" })
            Assert.DoesNotContain(_result.Dependencies,
                d => d.To.Contains(builtin, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Detects_cursors()
    {
        Assert.Equal(1, Object("usp_CerrarPedido").Metrics.CursorCount);
        Assert.Equal(1, Object("usp_FacturarPedidosPendientes").Metrics.CursorCount);
        Assert.Equal(0, Object("usp_RecalcularTarifas").Metrics.CursorCount);
    }

    [Fact]
    public void Detects_dynamic_sql_in_both_forms()
    {
        // EXEC (@sql)
        Assert.True(Object("usp_InformeVentas").Metrics.DynamicSqlCount > 0);

        // EXEC sp_executesql
        Assert.True(Object("usp_PurgarAuditoria").Metrics.DynamicSqlCount > 0);
    }

    [Fact]
    public void Does_not_mistake_sp_executesql_for_a_procedure_call()
    {
        var purge = Object("usp_PurgarAuditoria");

        Assert.DoesNotContain(_result.Dependencies,
            d => d.From == purge.FullName && d.Kind == DependencyKind.Calls);
    }

    [Fact]
    public void Detects_writes_outside_a_transaction()
    {
        // El procedimiento crítico: escribe en cuatro tablas sin transacción.
        var closeOrder = Object("usp_CerrarPedido");
        Assert.True(closeOrder.Metrics.WritesWithoutTransaction);
        Assert.Contains(closeOrder.Risk.Factors, f => f.Code == "NO_TRANSACTION");

        // El que está bien escrito no debe penalizarse.
        var movement = Object("usp_RegistrarMovimientoStock");
        Assert.False(movement.Metrics.WritesWithoutTransaction);
        Assert.DoesNotContain(movement.Risk.Factors, f => f.Code == "NO_TRANSACTION");
    }

    [Fact]
    public void Counts_temp_tables_without_adding_them_to_the_graph()
    {
        var recalculate = Object("usp_RecalcularTarifas");

        Assert.True(recalculate.Metrics.TempTableCount > 0);

        Assert.DoesNotContain(_result.Dependencies, d => d.To.Contains('#'));
    }

    [Fact]
    public void The_well_written_procedure_scores_lower_than_the_critical_one()
    {
        var wellWritten = Object("usp_RegistrarMovimientoStock");
        var critical = Object("usp_CerrarPedido");

        Assert.True(wellWritten.Risk.Value < critical.Risk.Value,
            $"wellWritten={wellWritten.Risk.Value} critical={critical.Risk.Value}");
    }

    [Fact]
    public void Every_risk_point_carries_its_reason()
    {
        foreach (var o in _result.Objects.Where(o => o.Risk.Value > 0))
        {
            Assert.NotEmpty(o.Risk.Factors);
            Assert.Equal(Math.Min(100, o.Risk.Factors.Sum(f => f.Points)), o.Risk.Value);
        }
    }

    [Fact]
    public void Identifies_leaves_entry_points_and_hubs()
    {
        // Hoja: no llama a nadie, así que se puede migrar de forma aislada.
        Assert.Contains(_result.Leaves, o => o.Name == "usp_RegistrarMovimientoStock");
        Assert.DoesNotContain(_result.Leaves, o => o.Name == "usp_CerrarPedido");

        // Punto de entrada: el proceso nocturno, al que nadie llama.
        Assert.Contains(_result.EntryPoints, o => o.Name == "usp_FacturarPedidosPendientes");

        // Nudo: usp_CerrarPedido está en medio, alguien depende de él.
        Assert.Contains(_result.Hubs, h => h.Object.Name == "usp_CerrarPedido");
    }
}
