namespace LegacyLens.Domain;

/// <summary>
/// Recorridos sobre el grafo de dependencias.
///
/// Vive en el dominio y no en la capa de aplicación porque no consulta nada:
/// son operaciones puras sobre una lista de aristas. Eso las hace comprobables
/// sin base de datos ni modelo de lenguaje, que es justo lo que hace falta en
/// la parte del sistema donde un error se propaga a todas las respuestas.
///
/// Las aristas salen del árbol sintáctico real, así que lo que se calcula aquí
/// es un hecho verificable y no una inferencia.
/// </summary>
public static class DependencyGraph
{
    /// <summary>Sentido en el que se recorre el grafo.</summary>
    public enum Direction
    {
        /// <summary>De quién depende el objeto: lo que necesita para funcionar.</summary>
        Downstream,

        /// <summary>Quién depende del objeto: lo que se rompería al cambiarlo.</summary>
        Upstream
    }

    /// <summary>
    /// Vecinos directos de un objeto en el sentido indicado, opcionalmente
    /// filtrados por tipo de relación.
    /// </summary>
    public static IReadOnlyList<Dependency> DirectNeighbours(
        IEnumerable<Dependency> dependencies,
        string fullName,
        Direction direction,
        DependencyKind? kind = null)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        return [.. dependencies
            .Where(d => kind is null || d.Kind == kind)
            .Where(d => Matches(d, fullName, direction))];
    }

    /// <summary>
    /// Cierre transitivo desde un objeto, sin incluirlo. Es el radio de impacto
    /// real: en sentido ascendente, todo lo que puede romperse al cambiarlo.
    ///
    /// El conjunto de visitados protege de los ciclos, que en un sistema
    /// heredado no son una rareza: dos procedimientos que se llaman
    /// mutuamente colgarían un recorrido ingenuo.
    /// </summary>
    public static IReadOnlyList<string> TransitiveClosure(
        IEnumerable<Dependency> dependencies,
        string fullName,
        Direction direction,
        int maxDepth = 10)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDepth);

        var aristas = dependencies.ToList();
        var visitados = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { fullName };
        var resultado = new List<string>();
        var frontera = new List<string> { fullName };

        for (var profundidad = 0; profundidad < maxDepth && frontera.Count > 0; profundidad++)
        {
            var siguiente = new List<string>();

            foreach (var actual in frontera)
            {
                foreach (var arista in aristas.Where(d => Matches(d, actual, direction)))
                {
                    var vecino = direction == Direction.Downstream ? arista.To : arista.From;

                    if (visitados.Add(vecino))
                    {
                        resultado.Add(vecino);
                        siguiente.Add(vecino);
                    }
                }
            }

            frontera = siguiente;
        }

        return resultado;
    }

    private static bool Matches(Dependency dependency, string fullName, Direction direction) =>
        direction == Direction.Downstream
            ? string.Equals(dependency.From, fullName, StringComparison.OrdinalIgnoreCase)
            : string.Equals(dependency.To, fullName, StringComparison.OrdinalIgnoreCase);
}
