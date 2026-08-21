namespace LegacyLens.Ai;

/// <summary>
/// Configuración de la capa de IA.
///
/// Se usan dos modelos distintos a propósito: documentar cada objeto es trabajo
/// repetitivo y de contexto corto, mientras que el plan de migración es una sola
/// decisión que exige razonar sobre el grafo completo. La medición del arnés de
/// evaluación confirmó además que para documentar el modelo económico no solo es
/// más barato, sino mejor.
///
/// Los precios de los modelos **no** están aquí: viven en la capa de aplicación,
/// porque cuánto vale un análisis es una regla de este producto y no un detalle
/// de cómo se habla con Azure OpenAI.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Endpoint del recurso de Azure OpenAI.</summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Clave del recurso, solo para desarrollo local. Si se deja vacía se usa
    /// la identidad de Azure del entorno, que es como funciona en producción.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Despliegue económico, para documentar objeto a objeto.</summary>
    public string DocumentationDeployment { get; set; } = "gpt-4.1-mini";

    /// <summary>Despliegue más capaz, solo para el plan de migración global.</summary>
    public string PlanningDeployment { get; set; } = "gpt-4o";

    /// <summary>Llamadas simultáneas al modelo. Protege la cuota del despliegue.</summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>
    /// Recorta el cuerpo enviado al modelo. Un procedimiento de 3.000 líneas no
    /// mejora la documentación, solo la factura.
    /// </summary>
    public int MaxBodyCharacters { get; set; } = 12_000;

    /// <summary>
    /// La aplicación funciona sin IA: el análisis estático es independiente.
    /// Esto permite ejecutarla en local sin credenciales y sin fingir nada.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint);

    /// <summary>
    /// Sin clave configurada se usa la identidad del entorno: identidad
    /// administrada en Azure, o la sesión de az login en desarrollo. Así no
    /// hay ningún secreto que guardar ni rotar.
    /// </summary>
    public bool UsesManagedIdentity => IsConfigured && string.IsNullOrWhiteSpace(ApiKey);
}
