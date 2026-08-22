using System.Text;
using LegacyLens.Domain;

namespace LegacyLens.Analysis.Tests;

/// <summary>
/// El segundo ejemplo existe para ejercitar factores de riesgo que el primero no
/// dispara. Estos tests fijan esa intención: si alguien edita el script y deja
/// de reproducir el patrón, falla aquí en lugar de degradarse en silencio en la
/// demostración.
/// </summary>
public class WarehouseSampleTests
{
    private readonly ITestOutputHelper _output;
    private readonly AnalysisResult _result;

    public WarehouseSampleTests(ITestOutputHelper output)
    {
        _output = output;
        var script = File.ReadAllText(SamplePath("legacy-almacen.sql"));
        _result = new TSqlAnalyzer().Analyze(script, "legacy-almacen.sql");
    }

    private static string SamplePath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "samples")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "samples", fileName);
    }

    private SqlObject Object(string name) =>
        _result.Objects.Single(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Diagnostic_dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Errores de parseo: {_result.ParseErrors.Count}");
        foreach (var e in _result.ParseErrors) sb.AppendLine($"  {e}");
        sb.AppendLine($"Objetos: {_result.ObjectCount}   Aristas: {_result.Dependencies.Count}");

        foreach (var o in _result.Objects.OrderByDescending(x => x.Risk.Value))
        {
            sb.AppendLine($"  [{o.Kind}] {o.FullName}  riesgo={o.Risk.Value} ({o.Risk.Level})");
            sb.AppendLine($"      lineas={o.Metrics.Lines} sent={o.Metrics.StatementCount} " +
                          $"cursores={o.Metrics.CursorCount} dyn={o.Metrics.DynamicSqlCount} " +
                          $"trans={o.Metrics.TransactionCount} temp={o.Metrics.TempTableCount} " +
                          $"catch={o.Metrics.HasErrorHandling} compl={o.Metrics.ControlFlowComplexity} " +
                          $"lee={o.Metrics.TablesRead} escribe={o.Metrics.TablesWritten} " +
                          $"llama={o.Metrics.ObjectsCalled}");
            foreach (var f in o.Risk.Factors)
                sb.AppendLine($"      {f.Code} (+{f.Points})");
        }

        _output.WriteLine(sb.ToString());
    }

    [Fact]
    public void The_script_parses_without_errors()
    {
        Assert.Empty(_result.ParseErrors);
    }

    [Fact]
    public void Contains_the_expected_object_kinds()
    {
        Assert.Contains(_result.Objects, o => o.Kind == SqlObjectKind.Function);
        Assert.Contains(_result.Objects, o => o.Kind == SqlObjectKind.View);
        Assert.Contains(_result.Objects, o => o.Kind == SqlObjectKind.Trigger);
        Assert.True(_result.Objects.Count(o => o.Kind == SqlObjectKind.Procedure) >= 6);
    }

    [Fact]
    public void Uses_no_cursors_on_purpose_to_differ_from_the_other_sample()
    {
        Assert.All(_result.Objects, o => Assert.Equal(0, o.Metrics.CursorCount));
    }

    [Fact]
    public void The_nightly_process_triggers_the_factors_the_other_sample_lacks()
    {
        var codes = Object("usp_ConsolidarExpediciones").Risk.Factors.Select(f => f.Code).ToList();

        Assert.Contains("TEMP_TABLES", codes);
        Assert.Contains("CHAINED_CALLS", codes);
        Assert.Contains("WIDE_SURFACE", codes);
        Assert.Contains("NO_ERROR_HANDLING", codes);
        Assert.Contains("NO_TRANSACTION", codes);
    }

    [Fact]
    public void The_nightly_process_is_the_riskiest_object()
    {
        var riskiest = _result.Objects.OrderByDescending(o => o.Risk.Value).First();

        Assert.Equal("usp_ConsolidarExpediciones", riskiest.Name);
        Assert.Equal(RiskLevel.Critical, riskiest.Risk.Level);
    }

    [Fact]
    public void The_weight_function_is_a_graph_leaf()
    {
        // No llama a nada, así que es candidata natural a migrar primero.
        Assert.Contains(_result.Leaves, o => o.Name == "fn_PesoVolumetrico");
    }

    [Fact]
    public void The_call_chain_has_real_depth()
    {
        // Consolidar -> Reservar -> RegistrarMovimiento son tres niveles, y es el
        // patrón que este ejemplo quiere representar: lógica repartida.
        var reach = DependencyGraph.TransitiveClosure(
            _result.Dependencies,
            "dbo.usp_ConsolidarExpediciones",
            DependencyGraph.Direction.Downstream);

        Assert.Contains("dbo.usp_ReservarStock", reach);
        Assert.Contains("dbo.usp_RegistrarMovimiento", reach);
    }
}
