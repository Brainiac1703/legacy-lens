namespace LegacyLens.Persistence.EF.Entities;

/// <summary>
/// Un análisis guardado.
///
/// El resultado completo se almacena serializado en <see cref="Payload"/> en
/// lugar de modelarse con una tabla por entidad. Es una decisión deliberada: el
/// análisis se escribe una vez y se lee entero, nunca se consulta por partes ni
/// se actualiza campo a campo, así que un modelo relacional detallado añadiría
/// bastante trabajo sin resolver ningún problema real. Las columnas de fuera
/// son solo las que hacen falta para listar y filtrar sin abrir el documento.
/// </summary>
public class StoredAnalysis
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Usuario que lo ejecutó. Cada uno ve solo los suyos.</summary>
    public string? OwnerUserId { get; set; }

    public int ObjectCount { get; set; }

    /// <summary>Si llegó a generarse documentación con IA o es solo análisis estático.</summary>
    public bool HasAiDocumentation { get; set; }

    public bool HasPlan { get; set; }

    /// <summary>El <c>AnalysisResult</c> completo en JSON.</summary>
    public string Payload { get; set; } = string.Empty;
}
