using LegacyLens.Ai;
using LegacyLens.Domain;
using Microsoft.Extensions.Options;

namespace LegacyLens.Web.Services;

/// <summary>Estimación de coste de un modelo concreto.</summary>
public sealed record ModelCost(ModelUsage Usage, decimal? EstimatedUsd);

/// <summary>
/// Traduce tokens a dinero.
///
/// Vive en la capa de presentación y no en el dominio a propósito: los precios no son un
/// hecho del análisis, cambian con el tiempo y dependen de la región y del tipo de
/// despliegue. Un análisis guardado hace seis meses no debe llevar dentro el precio de
/// entonces.
///
/// Si un modelo no tiene precio configurado, se devuelve el consumo sin importe. Es
/// preferible mostrar tokens sin coste que inventarse una cifra.
/// </summary>
public sealed class CostEstimator(IOptions<AiOptions> options)
{
    private readonly AiOptions _options = options.Value;

    public IReadOnlyList<ModelCost> Breakdown(AnalysisResult result) =>
        [.. result.Usage.Select(usage => new ModelCost(
            usage,
            _options.Pricing.TryGetValue(usage.Model, out var pricing)
                ? pricing.Estimate(usage.InputTokens, usage.OutputTokens)
                : null))];

    /// <summary>Total estimado, o nulo si falta el precio de algún modelo usado.</summary>
    public decimal? TotalUsd(AnalysisResult result)
    {
        var breakdown = Breakdown(result);

        if (breakdown.Count == 0 || breakdown.Any(b => b.EstimatedUsd is null)) return null;

        return breakdown.Sum(b => b.EstimatedUsd!.Value);
    }

    /// <summary>
    /// Formatea importes muy pequeños de forma legible. Un análisis cuesta céntimos, y
    /// «0,00 $» no informa de nada.
    /// </summary>
    public static string Format(decimal usd) => usd switch
    {
        < 0.01m => $"{usd * 100:F2} centavos",
        < 1m => $"{usd:F3} $",
        _ => $"{usd:F2} $"
    };
}
