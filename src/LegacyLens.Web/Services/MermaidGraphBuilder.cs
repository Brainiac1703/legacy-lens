using System.Text;
using LegacyLens.Domain;

namespace LegacyLens.Web.Services;

/// <summary>
/// Genera el grafo en sintaxis Mermaid.
///
/// Se eligió Mermaid en lugar de una librería de grafos porque el grafo se
/// describe en texto: eso lo hace comparable entre ejecuciones, exportable
/// dentro de la documentación en Markdown y trivial de versionar.
/// </summary>
public static class MermaidGraphBuilder
{
    /// <summary>
    /// Grafo de llamadas entre objetos programables. Es el que importa para
    /// planificar la migración: enseña qué se puede tocar de forma aislada y
    /// qué está en medio de todo.
    /// </summary>
    public static string BuildCallGraph(AnalysisResult result)
    {
        var programmable = result.Objects.Where(o => o.IsProgrammable).ToList();
        if (programmable.Count == 0) return "flowchart LR\n  vacio[\"Sin objetos programables\"]";

        var ids = AssignIds(programmable.Select(o => o.FullName));
        var sb = new StringBuilder();

        sb.AppendLine("flowchart LR");
        AppendRiskStyles(sb);

        foreach (var obj in programmable)
        {
            var label = $"{obj.Name}<br/><small>{obj.Risk.Value}</small>";
            sb.AppendLine($"  {ids[obj.FullName]}[\"{label}\"]:::{StyleFor(obj.Risk.Level)}");
        }

        var calls = result.Dependencies
            .Where(d => d.Kind == DependencyKind.Calls)
            .Where(d => ids.ContainsKey(d.From) && ids.ContainsKey(d.To))
            .ToList();

        foreach (var call in calls)
            sb.AppendLine($"  {ids[call.From]} --> {ids[call.To]}");

        return sb.ToString();
    }

    /// <summary>
    /// Grafo completo con las tablas, distinguiendo lectura de escritura.
    /// Enseña el flujo de datos, no el de control.
    /// </summary>
    public static string BuildDataGraph(AnalysisResult result)
    {
        var involved = result.Dependencies
            .Where(d => d.Kind is DependencyKind.Reads or DependencyKind.Writes)
            .ToList();

        if (involved.Count == 0) return "flowchart LR\n  vacio[\"Sin acceso a tablas\"]";

        var names = involved.Select(d => d.From)
            .Concat(involved.Select(d => d.To))
            .Distinct()
            .ToList();

        var ids = AssignIds(names);
        var byName = result.Objects.ToDictionary(o => o.FullName, o => o);
        var sb = new StringBuilder();

        sb.AppendLine("flowchart LR");
        AppendRiskStyles(sb);
        sb.AppendLine("  classDef tabla fill:#eef2f7,stroke:#8fa3bf,color:#243447;");

        foreach (var name in names)
        {
            var id = ids[name];
            var shortName = name.Contains('.') ? name[(name.IndexOf('.') + 1)..] : name;

            if (byName.TryGetValue(name, out var obj) && obj.IsProgrammable)
                sb.AppendLine($"  {id}[\"{shortName}\"]:::{StyleFor(obj.Risk.Level)}");
            else
                sb.AppendLine($"  {id}[(\"{shortName}\")]:::tabla");
        }

        foreach (var dep in involved.Where(d => d.Kind == DependencyKind.Reads))
            sb.AppendLine($"  {ids[dep.From]} -.->|lee| {ids[dep.To]}");

        foreach (var dep in involved.Where(d => d.Kind == DependencyKind.Writes))
            sb.AppendLine($"  {ids[dep.From]} ==>|escribe| {ids[dep.To]}");

        return sb.ToString();
    }

    /// <summary>
    /// Mermaid exige identificadores sin puntos ni espacios, así que se
    /// numeran y se guarda la correspondencia.
    /// </summary>
    private static Dictionary<string, string> AssignIds(IEnumerable<string> names)
    {
        var ids = new Dictionary<string, string>();
        var index = 0;

        foreach (var name in names)
            if (!ids.ContainsKey(name))
                ids[name] = $"n{index++}";

        return ids;
    }

    private static void AppendRiskStyles(StringBuilder sb)
    {
        sb.AppendLine("  classDef bajo fill:#e7f6ec,stroke:#2f7d4f,color:#12331f;");
        sb.AppendLine("  classDef medio fill:#fff6e0,stroke:#b8860b,color:#4a3606;");
        sb.AppendLine("  classDef alto fill:#fdecea,stroke:#c0392b,color:#4a1410;");
        sb.AppendLine("  classDef critico fill:#f8d7da,stroke:#7b1d15,color:#3d0b07,stroke-width:3px;");
    }

    private static string StyleFor(RiskLevel level) => level switch
    {
        RiskLevel.Low => "bajo",
        RiskLevel.Medium => "medio",
        RiskLevel.High => "alto",
        _ => "critico"
    };
}
