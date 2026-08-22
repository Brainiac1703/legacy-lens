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
    public void Volcado_diagnostico()
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
    public void El_script_parsea_sin_errores()
    {
        Assert.Empty(_result.ParseErrors);
    }

    [Fact]
    public void Contiene_los_tipos_de_objeto_esperados()
    {
        Assert.Contains(_result.Objects, o => o.Kind == SqlObjectKind.Function);
        Assert.Contains(_result.Objects, o => o.Kind == SqlObjectKind.View);
        Assert.Contains(_result.Objects, o => o.Kind == SqlObjectKind.Trigger);
        Assert.True(_result.Objects.Count(o => o.Kind == SqlObjectKind.Procedure) >= 6);
    }

    [Fact]
    public void No_usa_cursores_a_proposito_para_diferenciarse_del_otro_ejemplo()
    {
        Assert.All(_result.Objects, o => Assert.Equal(0, o.Metrics.CursorCount));
    }

    [Fact]
    public void El_proceso_nocturno_dispara_los_factores_que_el_otro_ejemplo_no_tiene()
    {
        var codigos = Object("usp_ConsolidarExpediciones").Risk.Factors.Select(f => f.Code).ToList();

        Assert.Contains("TEMP_TABLES", codigos);
        Assert.Contains("CHAINED_CALLS", codigos);
        Assert.Contains("WIDE_SURFACE", codigos);
        Assert.Contains("NO_ERROR_HANDLING", codigos);
        Assert.Contains("NO_TRANSACTION", codigos);
    }

    [Fact]
    public void El_proceso_nocturno_es_el_objeto_de_mayor_riesgo()
    {
        var mayor = _result.Objects.OrderByDescending(o => o.Risk.Value).First();

        Assert.Equal("usp_ConsolidarExpediciones", mayor.Name);
        Assert.Equal(RiskLevel.Critical, mayor.Risk.Level);
    }

    [Fact]
    public void La_funcion_de_peso_es_una_hoja_del_grafo()
    {
        // No llama a nada, así que es candidata natural a migrar primero.
        Assert.Contains(_result.Leaves, o => o.Name == "fn_PesoVolumetrico");
    }

    [Fact]
    public void La_cadena_de_llamadas_tiene_profundidad_real()
    {
        // Consolidar -> Reservar -> RegistrarMovimiento son tres niveles, y es el
        // patrón que este ejemplo quiere representar: lógica repartida.
        var alcance = DependencyGraph.TransitiveClosure(
            _result.Dependencies,
            "dbo.usp_ConsolidarExpediciones",
            DependencyGraph.Direction.Downstream);

        Assert.Contains("dbo.usp_ReservarStock", alcance);
        Assert.Contains("dbo.usp_RegistrarMovimiento", alcance);
    }
}
