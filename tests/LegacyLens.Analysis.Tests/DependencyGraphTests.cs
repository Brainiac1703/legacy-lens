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
    private static readonly Dependency[] Grafo =
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
    public void Vecinos_directos_hacia_abajo_son_lo_que_el_objeto_necesita()
    {
        var vecinos = DependencyGraph.DirectNeighbours(
            Grafo, "dbo.Facturar", DependencyGraph.Direction.Downstream);

        Assert.Equal(3, vecinos.Count);
        Assert.Contains(vecinos, d => d.To == "dbo.CalcularIva" && d.Kind == DependencyKind.Calls);
        Assert.Contains(vecinos, d => d.To == "dbo.Facturas" && d.Kind == DependencyKind.Writes);
    }

    [Fact]
    public void Vecinos_directos_hacia_arriba_son_quien_lo_usa()
    {
        var vecinos = DependencyGraph.DirectNeighbours(
            Grafo, "dbo.Facturar", DependencyGraph.Direction.Upstream);

        Assert.Equal(3, vecinos.Count);
        Assert.All(vecinos, d => Assert.Equal("dbo.Facturar", d.To));
    }

    [Fact]
    public void El_filtro_por_tipo_de_relacion_descarta_las_demas()
    {
        var escrituras = DependencyGraph.DirectNeighbours(
            Grafo, "dbo.Facturar", DependencyGraph.Direction.Downstream, DependencyKind.Writes);

        Assert.Single(escrituras);
        Assert.Equal("dbo.Facturas", escrituras[0].To);
    }

    [Fact]
    public void El_nombre_no_distingue_mayusculas()
    {
        var vecinos = DependencyGraph.DirectNeighbours(
            Grafo, "DBO.FACTURAR", DependencyGraph.Direction.Downstream);

        Assert.Equal(3, vecinos.Count);
    }

    [Fact]
    public void El_cierre_transitivo_hacia_arriba_es_el_radio_de_impacto()
    {
        var afectados = DependencyGraph.TransitiveClosure(
            Grafo, "dbo.CalcularIva", DependencyGraph.Direction.Upstream);

        // Cambiar CalcularIva puede romper a quien lo llama, y a quien llama a
        // aquel, hasta los puntos de entrada.
        Assert.Contains("dbo.Facturar", afectados);
        Assert.Contains("dbo.Informe", afectados);
        Assert.Contains("dbo.Pantalla", afectados);
        Assert.Contains("dbo.Auditar", afectados);
    }

    [Fact]
    public void El_objeto_de_partida_no_se_incluye_en_su_propio_cierre()
    {
        var afectados = DependencyGraph.TransitiveClosure(
            Grafo, "dbo.Facturar", DependencyGraph.Direction.Upstream);

        Assert.DoesNotContain("dbo.Facturar", afectados);
    }

    [Fact]
    public void Un_ciclo_no_cuelga_el_recorrido_ni_duplica_nodos()
    {
        // Facturar y Auditar se llaman mutuamente. Un recorrido ingenuo se
        // quedaría dando vueltas, y en un sistema heredado esto no es raro.
        var afectados = DependencyGraph.TransitiveClosure(
            Grafo, "dbo.Facturar", DependencyGraph.Direction.Upstream);

        Assert.Equal(afectados.Distinct().Count(), afectados.Count);
    }

    [Fact]
    public void La_profundidad_maxima_acota_el_recorrido()
    {
        var unNivel = DependencyGraph.TransitiveClosure(
            Grafo, "dbo.CalcularIva", DependencyGraph.Direction.Upstream, maxDepth: 1);

        Assert.Equal(["dbo.Facturar"], unNivel);
    }

    [Fact]
    public void Un_objeto_que_no_esta_en_el_grafo_devuelve_vacio()
    {
        Assert.Empty(DependencyGraph.DirectNeighbours(
            Grafo, "dbo.NoExiste", DependencyGraph.Direction.Downstream));

        Assert.Empty(DependencyGraph.TransitiveClosure(
            Grafo, "dbo.NoExiste", DependencyGraph.Direction.Upstream));
    }

    [Fact]
    public void Una_profundidad_no_positiva_es_un_error_de_programacion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DependencyGraph.TransitiveClosure(
                Grafo, "dbo.Facturar", DependencyGraph.Direction.Upstream, maxDepth: 0));
    }
}
