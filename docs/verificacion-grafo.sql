/*
    Verificación del grafo de dependencias contra el catálogo de SQL Server.

    Legacy Lens no ejecuta el SQL que analiza: lo parsea. Esta consulta comprueba que ese
    análisis estático coincide con lo que el propio motor sabe de sus objetos, y por tanto
    que las aristas del grafo son un hecho verificable y no una interpretación.

    Se ejecuta contra la base LegacyERP que levanta docker-compose, que contiene los mismos
    19 objetos que samples/legacy-erp.sql:

      docker exec -i legacy-lens-sqlserver-1 /opt/mssql-tools18/bin/sqlcmd \
        -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d LegacyERP \
        -i docs/verificacion-grafo.sql

    Resultado medido el 2026-08-23: 22 filas, de las que 21 son dependencias reales. La
    restante es la pseudo-tabla "inserted" de un trigger, que no es un objeto del esquema.
    Legacy Lens reporta 21 para el mismo script.
*/
SET NOCOUNT ON;

SELECT  CAST(OBJECT_NAME(d.referencing_id) AS NVARCHAR(128)) COLLATE DATABASE_DEFAULT AS Origen,
        CAST(ISNULL(d.referenced_entity_name, '?') AS NVARCHAR(128)) COLLATE DATABASE_DEFAULT AS Destino,
        CASE WHEN d.referenced_id IS NULL THEN 'no resuelta' ELSE 'ok' END AS Estado
FROM    sys.sql_expression_dependencies AS d
WHERE   OBJECTPROPERTY(d.referencing_id, 'IsMSShipped') = 0
ORDER BY Origen, Destino;

/*
    Los procedimientos que NO aparecen como origen en el resultado anterior son los que
    construyen SQL dinámico: usp_InformeVentas y usp_PurgarAuditoria. El catálogo devuelve
    cero dependencias para ellos, que es indistinguible de «no depende de nada». Legacy Lens
    tampoco puede resolverlas —es la misma limitación—, pero las puntúa como riesgo y lo
    dice: RiskScorer.cs, hasta 40 puntos por SQL dinámico.
*/
SELECT  CAST(o.name AS NVARCHAR(128)) COLLATE DATABASE_DEFAULT AS SinDependenciasVisibles
FROM    sys.objects AS o
WHERE   o.is_ms_shipped = 0
  AND   o.type IN ('P', 'V', 'FN', 'TR')
  AND   NOT EXISTS (SELECT 1 FROM sys.sql_expression_dependencies AS d
                    WHERE d.referencing_id = o.object_id)
ORDER BY SinDependenciasVisibles;
