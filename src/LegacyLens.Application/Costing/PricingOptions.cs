namespace LegacyLens.Application.Costing;

/// <summary>
/// Precio de un modelo, en dólares por millón de tokens.
///
/// Vive en la capa de aplicación y no en la de IA, que es donde estaba antes.
/// El motivo: un precio no es un detalle de cómo se habla con Azure OpenAI, es
/// una regla del negocio de esta aplicación —cuánto vale un análisis— que
/// seguiría existiendo con otro proveedor detrás.
/// </summary>
public sealed class ModelPricing
{
    public decimal InputPerMillion { get; set; }
    public decimal OutputPerMillion { get; set; }

    public decimal Estimate(long inputTokens, long outputTokens) =>
        inputTokens / 1_000_000m * InputPerMillion +
        outputTokens / 1_000_000m * OutputPerMillion;
}

/// <summary>
/// Precios por modelo, indexados por nombre de despliegue.
///
/// Si un modelo no aparece aquí, la aplicación muestra los tokens consumidos
/// pero no estima importe: es preferible no decir nada a inventarse una cifra.
/// </summary>
public sealed class PricingOptions
{
    public const string SectionName = "Costing";

    public Dictionary<string, ModelPricing> Models { get; set; } = [];
}
