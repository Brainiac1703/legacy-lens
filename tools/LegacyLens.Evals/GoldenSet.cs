namespace LegacyLens.Evals;

/// <summary>
/// Conjunto dorado sobre <c>samples/legacy-erp.sql</c>.
///
/// Para cada objeto se enumeran las reglas de negocio que **sabemos** que están en el
/// código, porque el script lo escribimos nosotros. Cada regla se expresa como un conjunto
/// de términos alternativos: se considera cubierta si la documentación generada menciona
/// alguno de ellos.
///
/// No se comparan textos completos a propósito. Dos redacciones distintas de la misma regla
/// son igual de válidas, y exigir coincidencia literal mediría parecido de estilo en lugar
/// de comprensión.
/// </summary>
internal static class GoldenSet
{
    internal sealed record ExpectedRule(string Description, string[] AnyOf);

    internal sealed record ObjectExpectation(
        string ObjectName,
        ExpectedRule[] Rules,
        bool MustWarnAboutDynamicSql = false);

    public static readonly ObjectExpectation[] Expectations =
    [
        new("dbo.usp_CerrarPedido",
        [
            new("No cierra el pedido si hay facturas vencidas hace más de 60 días",
                ["vencid", "60 dia", "60 días", "impagad"]),
            new("Aplica un descuento calculado al total",
                ["descuento"]),
            new("Descuenta stock por cada línea del pedido",
                ["stock", "inventario", "existencias"]),
            new("Genera la factura del pedido",
                ["factura"]),
            new("Puede dejar datos inconsistentes: escribe sin transacción",
                ["inconsistent", "sin transaccion", "sin transacción", "parcialmente", "a medias"])
        ]),

        new("dbo.fn_CalcularDescuento",
        [
            new("El descuento depende de la antigüedad del cliente",
                ["antiguedad", "antigüedad", "fecha de alta", "anos como cliente", "años como cliente"]),
            new("El descuento depende del volumen facturado el año anterior",
                ["facturado", "volumen", "ano anterior", "año anterior"]),
            new("Existe un tope máximo de descuento",
                ["tope", "maximo", "máximo", "limite", "límite", "12"])
        ]),

        new("dbo.usp_FacturarPedidosPendientes",
        [
            new("Recorre los pedidos pendientes uno a uno",
                ["cursor", "uno a uno", "fila a fila", "recorre", "itera"]),
            new("Registra los errores en lugar de abortar el proceso",
                ["error", "continua", "continúa", "registra", "audit"])
        ]),

        new("dbo.usp_InformeVentas",
        [
            new("Los filtros son opcionales",
                ["opcional", "filtro", "parametro", "parámetro"])
        ], MustWarnAboutDynamicSql: true),

        new("dbo.usp_PurgarAuditoria",
        [
            new("Borra registros de auditoría más antiguos que el periodo de retención",
                ["retencion", "retención", "antigu", "borra", "elimina", "purga"])
        ], MustWarnAboutDynamicSql: true),

        new("dbo.usp_RecalcularTarifas",
        [
            new("El incremento se aplica por tramos según el precio base",
                ["tramo", "segun el precio", "según el precio", "rango", "caso"]),
            new("Puede limitarse a una familia de artículos",
                ["familia"]),
            new("Opera dentro de una transacción con control de errores",
                ["transaccion", "transacción", "rollback", "try", "catch"])
        ]),

        new("dbo.trg_LineasPedido_Auditoria",
        [
            new("Registra en auditoría los cambios en las líneas de pedido",
                ["audit", "registra", "traza"])
        ])
    ];
}
