using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using LegacyLens.Application.Analyses;
using LegacyLens.Application.Knowledge;
using MediatR;
using ModelContextProtocol.Server;

namespace LegacyLens.Mcp;

/// <summary>
/// Las herramientas que ve el agente.
///
/// Cada una es una traducción de tres líneas: resolver el propietario, mandar
/// una consulta por MediatR y serializar. Deliberadamente no hay lógica aquí —
/// si aparece, es que le corresponde a la capa de aplicación, donde la web
/// también podría usarla y donde hay tests.
///
/// Las descripciones importan más de lo que parece: son el único contexto que
/// el modelo tiene para decidir qué herramienta usar y con qué argumentos. Una
/// descripción vaga se traduce en llamadas equivocadas.
/// </summary>
[McpServerToolType]
public sealed class KnowledgeTools(ISender sender, OwnerResolver owner)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // Enumeraciones como texto, por el mismo motivo que en la persistencia:
        // «Kind: 1» no le dice nada a quien lee esto, y aquí quien lee es un
        // modelo de lenguaje que tiene que decidir si esa relación es una
        // lectura o una escritura. Un número le obliga a adivinar.
        Converters = { new JsonStringEnumConverter() }
    };

    [McpServerTool(Name = "list_analyses")]
    [Description("""
        Lista los sistemas heredados ya analizados, del más reciente al más antiguo.
        Es el punto de partida: el resto de herramientas necesitan el identificador
        de análisis que devuelve esta. Cada análisis corresponde a un script T-SQL
        completo, es decir a un sistema.
        """)]
    public async Task<string> ListAnalysesAsync(CancellationToken cancellationToken)
    {
        var ownerId = await owner.GetOwnerUserIdAsync(cancellationToken);
        var analyses = await sender.Send(new ListAnalysesQuery(ownerId), cancellationToken);

        return Serialize(analyses);
    }

    [McpServerTool(Name = "find_object")]
    [Description("""
        Ficha completa de un objeto de base de datos: métricas, riesgo con sus
        motivos, documentación y dependencias directas.

        Distingue dos naturalezas de dato y conviene tratarlas distinto: las
        métricas y el riesgo se CALCULAN del árbol sintáctico y son hechos
        verificables; la documentación la GENERA un modelo de lenguaje y es una
        interpretación. Si las dos se contradicen, manda la métrica.

        El nombre acepta con esquema ('dbo.Facturar') o sin él ('Facturar').
        """)]
    public async Task<string> FindObjectAsync(
        [Description("Identificador del análisis, obtenido de list_analyses.")] Guid analysisId,
        [Description("Nombre del objeto, con esquema o sin él.")] string name,
        CancellationToken cancellationToken)
    {
        var ownerId = await owner.GetOwnerUserIdAsync(cancellationToken);
        var card = await sender.Send(new FindObjectQuery(analysisId, ownerId, name), cancellationToken);

        return card is null
            ? $"No se encontró ningún objeto llamado '{name}' en ese análisis."
            : Serialize(card);
    }

    [McpServerTool(Name = "where_used")]
    [Description("""
        Quién referencia a una tabla u objeto, y si la lee, la escribe o la
        ejecuta. Es la pregunta que hay que hacerse antes de cambiar una tabla:
        devuelve todo lo que habría que revisar.

        Funciona también con tablas que el script no define pero sí referencia,
        que es el caso habitual cuando el esquema está en otro fichero.
        """)]
    public async Task<string> WhereUsedAsync(
        [Description("Identificador del análisis, obtenido de list_analyses.")] Guid analysisId,
        [Description("Nombre de la tabla u objeto.")] string name,
        CancellationToken cancellationToken)
    {
        var ownerId = await owner.GetOwnerUserIdAsync(cancellationToken);
        var usages = await sender.Send(new WhereUsedQuery(analysisId, ownerId, name), cancellationToken);

        if (usages is null) return "No existe ese análisis.";

        return usages.Count == 0
            ? $"Nada referencia a '{name}' en ese análisis."
            : Serialize(usages);
    }

    [McpServerTool(Name = "change_risk")]
    [Description("""
        Radio de impacto de cambiar un objeto. Devuelve tres cosas que conviene
        no confundir:

        - El riesgo PROPIO del objeto, con los motivos que suman puntos: es lo
          difícil que es traducirlo.
        - Los dependientes, directos y transitivos: es lo que se puede romper al
          tocarlo. Un procedimiento trivial con veinte dependientes es poco
          riesgo de traducir y mucho de cambiar.
        - Los bloqueantes: lo que este objeto llama y por tanto habría que haber
          migrado antes.
        """)]
    public async Task<string> ChangeRiskAsync(
        [Description("Identificador del análisis, obtenido de list_analyses.")] Guid analysisId,
        [Description("Nombre del objeto que se quiere cambiar.")] string name,
        CancellationToken cancellationToken)
    {
        var ownerId = await owner.GetOwnerUserIdAsync(cancellationToken);
        var impact = await sender.Send(new ChangeRiskQuery(analysisId, ownerId, name), cancellationToken);

        return impact is null
            ? $"No se encontró ningún objeto llamado '{name}' en ese análisis."
            : Serialize(impact);
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
}
