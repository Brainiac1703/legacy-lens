namespace LegacyLens.Web;

/// <summary>
/// Ubicación del script de ejemplo, en un solo sitio.
///
/// Lo usan dos caminos: el botón que lo analiza y el enlace que lo descarga. Si
/// cada uno resolviera la ruta por su cuenta podrían acabar sirviendo ficheros
/// distintos tras un cambio en el csproj, y quien descargue el script para
/// contrastar el análisis vería otra cosa.
///
/// El fichero vive en <c>samples/</c> en la raíz del repositorio; el csproj lo
/// copia a la carpeta de publicación como <c>Samples/legacy-erp.sql</c>.
/// </summary>
internal static class SampleScript
{
    public const string FileName = "legacy-erp.sql";

    public static string FullPath =>
        Path.Combine(AppContext.BaseDirectory, "Samples", FileName);
}
