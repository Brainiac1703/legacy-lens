using System.Text;
using LegacyLens.Domain;
using Xunit.Abstractions;

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
    public void Volcado_diagnostico()
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
    public void Parsea_el_script_sin_errores()
    {
        Assert.Empty(_result.ParseErrors);
    }

    [Fact]
    public void Detecta_todos_los_tipos_de_objeto()
    {
        Assert.Equal(10, _result.Objects.Count(o => o.Kind == SqlObjectKind.Table));
        Assert.Equal(1, _result.Objects.Count(o => o.Kind == SqlObjectKind.View));
        Assert.Equal(1, _result.Objects.Count(o => o.Kind == SqlObjectKind.Function));
        Assert.Equal(6, _result.Objects.Count(o => o.Kind == SqlObjectKind.Procedure));
        Assert.Equal(1, _result.Objects.Count(o => o.Kind == SqlObjectKind.Trigger));
    }

    [Fact]
    public void Distingue_lecturas_de_escrituras()
    {
        var cerrar = Object("usp_CerrarPedido");

        var escrituras = _result.Dependencies
            .Where(d => d.From == cerrar.FullName && d.Kind == DependencyKind.Writes)
            .Select(d => d.To)
            .ToList();

        Assert.Contains("dbo.Facturas", escrituras);
        Assert.Contains("dbo.LineasFactura", escrituras);
        Assert.Contains("dbo.Pedidos", escrituras);
        Assert.Contains("dbo.Auditoria", escrituras);

        var lecturas = _result.Dependencies
            .Where(d => d.From == cerrar.FullName && d.Kind == DependencyKind.Reads)
            .Select(d => d.To)
            .ToList();

        // Lee de LineasPedido y Clientes, pero nunca escribe en ellas.
        Assert.Contains("dbo.LineasPedido", lecturas);
        Assert.Contains("dbo.Clientes", lecturas);
        Assert.DoesNotContain("dbo.LineasPedido", escrituras);
    }

    [Fact]
    public void Detecta_llamadas_entre_procedimientos()
    {
        var llamadas = _result.Dependencies.Where(d => d.Kind == DependencyKind.Calls).ToList();

        Assert.Contains(llamadas, d =>
            d.From == "dbo.usp_CerrarPedido" && d.To == "dbo.usp_RegistrarMovimientoStock");

        Assert.Contains(llamadas, d =>
            d.From == "dbo.usp_FacturarPedidosPendientes" && d.To == "dbo.usp_CerrarPedido");
    }

    [Fact]
    public void Detecta_funciones_escalares_usadas_en_expresiones()
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
    public void No_confunde_funciones_del_motor_con_dependencias()
    {
        // El script usa GETDATE, COUNT, ISNULL, DATEDIFF, CAST y SUSER_SNAME.
        // Ninguna es un objeto del esquema.
        foreach (var builtin in new[] { "GETDATE", "COUNT", "ISNULL", "DATEDIFF", "SUSER_SNAME" })
            Assert.DoesNotContain(_result.Dependencies,
                d => d.To.Contains(builtin, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Detecta_cursores()
    {
        Assert.Equal(1, Object("usp_CerrarPedido").Metrics.CursorCount);
        Assert.Equal(1, Object("usp_FacturarPedidosPendientes").Metrics.CursorCount);
        Assert.Equal(0, Object("usp_RecalcularTarifas").Metrics.CursorCount);
    }

    [Fact]
    public void Detecta_sql_dinamico_en_sus_dos_formas()
    {
        // EXEC (@sql)
        Assert.True(Object("usp_InformeVentas").Metrics.DynamicSqlCount > 0);

        // EXEC sp_executesql
        Assert.True(Object("usp_PurgarAuditoria").Metrics.DynamicSqlCount > 0);
    }

    [Fact]
    public void No_confunde_sp_executesql_con_una_llamada_a_procedimiento()
    {
        var purgar = Object("usp_PurgarAuditoria");

        Assert.DoesNotContain(_result.Dependencies,
            d => d.From == purgar.FullName && d.Kind == DependencyKind.Calls);
    }

    [Fact]
    public void Detecta_escrituras_sin_transaccion()
    {
        // El procedimiento crítico: escribe en cuatro tablas sin transacción.
        var cerrar = Object("usp_CerrarPedido");
        Assert.True(cerrar.Metrics.WritesWithoutTransaction);
        Assert.Contains(cerrar.Risk.Factors, f => f.Code == "NO_TRANSACTION");

        // El que está bien escrito no debe penalizarse.
        var movimiento = Object("usp_RegistrarMovimientoStock");
        Assert.False(movimiento.Metrics.WritesWithoutTransaction);
        Assert.DoesNotContain(movimiento.Risk.Factors, f => f.Code == "NO_TRANSACTION");
    }

    [Fact]
    public void Cuenta_tablas_temporales_sin_meterlas_en_el_grafo()
    {
        var recalcular = Object("usp_RecalcularTarifas");

        Assert.True(recalcular.Metrics.TempTableCount > 0);

        Assert.DoesNotContain(_result.Dependencies, d => d.To.Contains('#'));
    }

    [Fact]
    public void El_procedimiento_bien_escrito_puntua_menos_que_el_critico()
    {
        var bueno = Object("usp_RegistrarMovimientoStock");
        var critico = Object("usp_CerrarPedido");

        Assert.True(bueno.Risk.Value < critico.Risk.Value,
            $"bueno={bueno.Risk.Value} critico={critico.Risk.Value}");
    }

    [Fact]
    public void Cada_punto_de_riesgo_tiene_su_justificacion()
    {
        foreach (var o in _result.Objects.Where(o => o.Risk.Value > 0))
        {
            Assert.NotEmpty(o.Risk.Factors);
            Assert.Equal(Math.Min(100, o.Risk.Factors.Sum(f => f.Points)), o.Risk.Value);
        }
    }

    [Fact]
    public void Identifica_hojas_puntos_de_entrada_y_nudos()
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
