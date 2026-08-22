namespace LegacyLens.Web;

/// <summary>
/// Los scripts de ejemplo, en un solo sitio.
///
/// Cada uno se usa por dos caminos —el botón que lo analiza y el enlace que lo
/// descarga— y hay una razón para que la ruta se resuelva aquí y no en cada
/// uno: quien descarga el script lo hace para contrastar el análisis, así que
/// tiene que ser el mismo fichero exacto.
///
/// Los ficheros viven en <c>samples/</c> en la raíz del repositorio; el csproj
/// los copia a la carpeta de publicación bajo <c>Samples/</c>.
/// </summary>
internal sealed record SampleScript(string FileName, string NameKey, string BodyKey)
{
    /// <summary>
    /// Dos ejemplos y no uno porque tienen perfiles de deuda distintos, y ver
    /// solo uno da una idea equivocada de lo que mide la herramienta: el primero
    /// carga en iteración fila a fila, el segundo en lógica repartida y ausencia
    /// de garantías. Sus riesgos máximos son 55 y 80.
    /// </summary>
    public static readonly IReadOnlyList<SampleScript> All =
    [
        new("legacy-erp.sql", "Analyze_SampleErpName", "Analyze_SampleErpBody"),
        new("legacy-almacen.sql", "Analyze_SampleWarehouseName", "Analyze_SampleWarehouseBody")
    ];

    public string FullPath => Path.Combine(AppContext.BaseDirectory, "Samples", FileName);

    /// <summary>
    /// Busca por nombre de fichero. Devuelve nulo si no está en la lista, que es
    /// lo que impide que el endpoint de descarga sirva cualquier ruta: el nombre
    /// llega de la petición y solo se acepta si coincide con uno conocido, en
    /// lugar de intentar sanearlo.
    /// </summary>
    public static SampleScript? Find(string fileName) =>
        All.FirstOrDefault(s => string.Equals(s.FileName, fileName, StringComparison.OrdinalIgnoreCase));
}
