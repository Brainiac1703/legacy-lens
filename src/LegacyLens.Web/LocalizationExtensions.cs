using LegacyLens.Application.Analyses;
using LegacyLens.Domain;
using Microsoft.Extensions.Localization;

namespace LegacyLens.Web;

/// <summary>
/// Traduce los valores de enumeración del dominio a texto para el usuario.
///
/// El dominio no sabe de idiomas: sus enumeraciones son conceptos, no rótulos. La
/// traducción vive aquí, en la presentación, y por convención de clave —
/// «Risk_High», «Kind_Procedure»— para no tener un switch por cada enumeración
/// que haya que ampliar cada vez que se añada un valor.
///
/// La contrapartida es que un valor nuevo sin su clave en el .resx no rompe la
/// compilación: se vería el nombre de la clave en pantalla. Es un intercambio
/// aceptable porque estas enumeraciones son estables y cerradas.
/// </summary>
public static class LocalizationExtensions
{
    public static string Text(this IStringLocalizer<UiText> localizer, RiskLevel level) =>
        localizer[$"Risk_{level}"];

    public static string Text(this IStringLocalizer<UiText> localizer, SqlObjectKind kind) =>
        localizer[$"Kind_{kind}"];

    /// <summary>Mensaje de la fase en curso del análisis.</summary>
    public static string Text(this IStringLocalizer<UiText> localizer, AnalysisPhase phase) =>
        localizer[$"Analyze_Msg{phase}"];

    /// <summary>Rótulo de la fase en la lista de pasos.</summary>
    public static string StepLabel(this IStringLocalizer<UiText> localizer, AnalysisPhase phase) =>
        localizer[$"Analyze_Phase{phase}"];

    public static string YesNo(this IStringLocalizer<UiText> localizer, bool value) =>
        value ? localizer["Common_Yes"] : localizer["Common_No"];
}
