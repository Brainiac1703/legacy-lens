using LegacyLens.Domain;

namespace LegacyLens.Analysis.Tests;

/// <summary>
/// El grafo es la parte del sistema donde un error se propaga a todas las
/// respuestas del servidor MCP, así que se prueba con aristas escritas a mano
/// en lugar de con el script de ejemplo: aquí interesa el recorrido, no el
/// analizador.
/// </summary>
public class DependencyGraphTests
{
    // Cadena con un ciclo y una rama:
    //
    //   Informe -> Facturar -> CalcularIva -> Config
    //                       -> Auditar -----> Facturar   (ciclo)
    //   Pantalla -> Facturar
    private static readonly Dependency[] Graph =
    [
        new("dbo.Informe", "dbo.Facturar", DependencyKind.Calls),
        new("dbo.Pantalla", "dbo.Facturar", DependencyKind.Calls),
        new("dbo.Facturar", "dbo.CalcularIva", DependencyKind.Calls),
        new("dbo.Facturar", "dbo.Auditar", DependencyKind.Calls),
        new("dbo.Auditar", "dbo.Facturar", DependencyKind.Calls),
        new("dbo.CalcularIva", "dbo.Config", DependencyKind.Reads),
        new("dbo.Facturar", "dbo.Facturas", DependencyKind.Writes),
    ];

    [Fact]
    public void Direct_downstream_neighbours_are_what_the_object_needs()
    {
        var neighbours = DependencyGraph.DirectNeighbours(
            Graph, "dbo.Facturar", DependencyGraph.Direction.Downstream);

        Assert.Equal(3, neighbours.Count);
        Assert.Contains(neighbours, d => d.To == "dbo.CalcularIva" && d.Kind == DependencyKind.Calls);
        Assert.Contains(neighbours, d => d.To == "dbo.Facturas" && d.Kind == DependencyKind.Writes);
    }

    [Fact]
    public void Direct_upstream_neighbours_are_who_uses_it()
    {
        var neighbours = DependencyGraph.DirectNeighbours(
            Graph, "dbo.Facturar", DependencyGraph.Direction.Upstream);

        Assert.Equal(3, neighbours.Count);
        Assert.All(neighbours, d => Assert.Equal("dbo.Facturar", d.To));
    }

    [Fact]
    public void Filtering_by_dependency_kind_discards_the_rest()
    {
        var writes = DependencyGraph.DirectNeighbours(
            Graph, "dbo.Facturar", DependencyGraph.Direction.Downstream, DependencyKind.Writes);

        Assert.Single(writes);
        Assert.Equal("dbo.Facturas", writes[0].To);
    }

    [Fact]
    public void Name_matching_ignores_case()
    {
        var neighbours = DependencyGraph.DirectNeighbours(
            Graph, "DBO.FACTURAR", DependencyGraph.Direction.Downstream);

        Assert.Equal(3, neighbours.Count);
    }

    [Fact]
    public void Upstream_transitive_closure_is_the_impact_radius()
    {
        var affected = DependencyGraph.TransitiveClosure(
            Graph, "dbo.CalcularIva", DependencyGraph.Direction.Upstream);

        // Cambiar CalcularIva puede romper a quien lo llama, y a quien llama a
        // aquel, hasta los puntos de entrada.
        Assert.Contains("dbo.Facturar", affected);
        Assert.Contains("dbo.Informe", affected);
        Assert.Contains("dbo.Pantalla", affected);
        Assert.Contains("dbo.Auditar", affected);
    }

    [Fact]
    public void The_starting_object_is_not_part_of_its_own_closure()
    {
        var affected = DependencyGraph.TransitiveClosure(
            Graph, "dbo.Facturar", DependencyGraph.Direction.Upstream);

        Assert.DoesNotContain("dbo.Facturar", affected);
    }

    [Fact]
    public void A_cycle_neither_hangs_the_walk_nor_duplicates_nodes()
    {
        // Facturar y Auditar se llaman mutuamente. Un recorrido ingenuo se
        // quedaría dando vueltas, y en un sistema heredado esto no es raro.
        var affected = DependencyGraph.TransitiveClosure(
            Graph, "dbo.Facturar", DependencyGraph.Direction.Upstream);

        Assert.Equal(affected.Distinct().Count(), affected.Count);
    }

    [Fact]
    public void Max_depth_bounds_the_walk()
    {
        var oneLevel = DependencyGraph.TransitiveClosure(
            Graph, "dbo.CalcularIva", DependencyGraph.Direction.Upstream, maxDepth: 1);

        Assert.Equal(["dbo.Facturar"], oneLevel);
    }

    [Fact]
    public void An_object_absent_from_the_graph_returns_empty()
    {
        Assert.Empty(DependencyGraph.DirectNeighbours(
            Graph, "dbo.NoExiste", DependencyGraph.Direction.Downstream));

        Assert.Empty(DependencyGraph.TransitiveClosure(
            Graph, "dbo.NoExiste", DependencyGraph.Direction.Upstream));
    }

    [Fact]
    public void A_non_positive_depth_is_a_programming_error()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DependencyGraph.TransitiveClosure(
                Graph, "dbo.Facturar", DependencyGraph.Direction.Upstream, maxDepth: 0));
    }
}
