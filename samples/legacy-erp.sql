-- =============================================================================
-- ERP LEGACY - Script de ejemplo para Legacy Lens
--
-- Base de datos sintética que reproduce los patrones que de verdad aparecen en
-- sistemas heredados de SQL Server: lógica de negocio dentro de la base de
-- datos, cursores, SQL dinámico, escrituras sin transacción y procedimientos
-- que se llaman entre ellos formando cadenas difíciles de seguir.
--
-- Es código inventado a propósito para la demo: no procede de ningún sistema
-- real y puede publicarse sin ningún problema.
-- =============================================================================

CREATE TABLE dbo.Clientes (
    ClienteId       INT IDENTITY(1,1) PRIMARY KEY,
    Nombre          NVARCHAR(200)  NOT NULL,
    CIF             VARCHAR(20)    NULL,
    DiasCredito     INT            NOT NULL DEFAULT 30,
    Bloqueado       BIT            NOT NULL DEFAULT 0,
    FechaAlta       DATETIME       NOT NULL DEFAULT GETDATE()
);
GO

CREATE TABLE dbo.Articulos (
    ArticuloId      INT IDENTITY(1,1) PRIMARY KEY,
    Referencia      VARCHAR(30)    NOT NULL,
    Descripcion     NVARCHAR(300)  NULL,
    PrecioBase      DECIMAL(18,4)  NOT NULL,
    Familia         VARCHAR(20)    NULL,
    Descatalogado   BIT            NOT NULL DEFAULT 0
);
GO

CREATE TABLE dbo.Stock (
    ArticuloId      INT            NOT NULL,
    AlmacenId       INT            NOT NULL,
    Cantidad        DECIMAL(18,4)  NOT NULL DEFAULT 0,
    CantidadMinima  DECIMAL(18,4)  NOT NULL DEFAULT 0
);
GO

CREATE TABLE dbo.MovimientosStock (
    MovimientoId    BIGINT IDENTITY(1,1) PRIMARY KEY,
    ArticuloId      INT            NOT NULL,
    AlmacenId       INT            NOT NULL,
    Cantidad        DECIMAL(18,4)  NOT NULL,
    Motivo          VARCHAR(50)    NULL,
    Fecha           DATETIME       NOT NULL DEFAULT GETDATE()
);
GO

CREATE TABLE dbo.Pedidos (
    PedidoId        INT IDENTITY(1,1) PRIMARY KEY,
    ClienteId       INT            NOT NULL,
    Fecha           DATETIME       NOT NULL DEFAULT GETDATE(),
    Estado          VARCHAR(20)    NOT NULL DEFAULT 'ABIERTO',
    Total           DECIMAL(18,4)  NOT NULL DEFAULT 0,
    FechaCierre     DATETIME       NULL
);
GO

CREATE TABLE dbo.LineasPedido (
    LineaId         INT IDENTITY(1,1) PRIMARY KEY,
    PedidoId        INT            NOT NULL,
    ArticuloId      INT            NOT NULL,
    Cantidad        DECIMAL(18,4)  NOT NULL,
    PrecioUnitario  DECIMAL(18,4)  NOT NULL,
    Descuento       DECIMAL(9,4)   NOT NULL DEFAULT 0
);
GO

CREATE TABLE dbo.Facturas (
    FacturaId       INT IDENTITY(1,1) PRIMARY KEY,
    ClienteId       INT            NOT NULL,
    PedidoId        INT            NULL,
    Fecha           DATETIME       NOT NULL DEFAULT GETDATE(),
    BaseImponible   DECIMAL(18,4)  NOT NULL DEFAULT 0,
    Total           DECIMAL(18,4)  NOT NULL DEFAULT 0,
    Vencimiento     DATETIME       NULL,
    Cobrada         BIT            NOT NULL DEFAULT 0
);
GO

CREATE TABLE dbo.LineasFactura (
    LineaFacturaId  INT IDENTITY(1,1) PRIMARY KEY,
    FacturaId       INT            NOT NULL,
    ArticuloId      INT            NOT NULL,
    Cantidad        DECIMAL(18,4)  NOT NULL,
    Importe         DECIMAL(18,4)  NOT NULL
);
GO

CREATE TABLE dbo.Tarifas (
    TarifaId        INT IDENTITY(1,1) PRIMARY KEY,
    ArticuloId      INT            NOT NULL,
    Familia         VARCHAR(20)    NULL,
    Precio          DECIMAL(18,4)  NOT NULL,
    VigenteDesde    DATETIME       NOT NULL
);
GO

CREATE TABLE dbo.Auditoria (
    AuditoriaId     BIGINT IDENTITY(1,1) PRIMARY KEY,
    Tabla           VARCHAR(100)   NOT NULL,
    Operacion       VARCHAR(20)    NOT NULL,
    Detalle         NVARCHAR(MAX)  NULL,
    Usuario         NVARCHAR(100)  NULL,
    Fecha           DATETIME       NOT NULL DEFAULT GETDATE()
);
GO

-- -----------------------------------------------------------------------------
-- Vista de pedidos pendientes de facturar
-- -----------------------------------------------------------------------------
CREATE VIEW dbo.vw_PedidosPendientes
AS
    SELECT  p.PedidoId,
            p.ClienteId,
            c.Nombre AS Cliente,
            p.Fecha,
            p.Total
    FROM    dbo.Pedidos p
    INNER JOIN dbo.Clientes c ON c.ClienteId = p.ClienteId
    WHERE   p.Estado = 'ABIERTO'
      AND   c.Bloqueado = 0;
GO

-- -----------------------------------------------------------------------------
-- Calcula el descuento aplicable a un cliente.
-- Regla de negocio enterrada: el descuento depende de la antigüedad y del
-- volumen facturado el año anterior, con topes por tramos.
-- -----------------------------------------------------------------------------
CREATE FUNCTION dbo.fn_CalcularDescuento (@ClienteId INT, @Importe DECIMAL(18,4))
RETURNS DECIMAL(9,4)
AS
BEGIN
    DECLARE @Descuento DECIMAL(9,4) = 0;
    DECLARE @Antiguedad INT;
    DECLARE @FacturadoAnterior DECIMAL(18,4);

    SELECT @Antiguedad = DATEDIFF(YEAR, FechaAlta, GETDATE())
    FROM   dbo.Clientes
    WHERE  ClienteId = @ClienteId;

    SELECT @FacturadoAnterior = ISNULL(SUM(Total), 0)
    FROM   dbo.Facturas
    WHERE  ClienteId = @ClienteId
      AND  YEAR(Fecha) = YEAR(GETDATE()) - 1;

    IF @Antiguedad >= 10
        SET @Descuento = 0.05;
    ELSE IF @Antiguedad >= 5
        SET @Descuento = 0.03;

    IF @FacturadoAnterior > 100000
        SET @Descuento = @Descuento + 0.04;
    ELSE IF @FacturadoAnterior > 50000
        SET @Descuento = @Descuento + 0.02;

    IF @Importe > 10000
        SET @Descuento = @Descuento + 0.01;

    IF @Descuento > 0.12
        SET @Descuento = 0.12;

    RETURN @Descuento;
END
GO

-- -----------------------------------------------------------------------------
-- Registra un movimiento de stock. Hoja del grafo: no llama a nadie.
-- -----------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_RegistrarMovimientoStock
    @ArticuloId INT,
    @AlmacenId  INT,
    @Cantidad   DECIMAL(18,4),
    @Motivo     VARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRANSACTION;

    BEGIN TRY
        INSERT INTO dbo.MovimientosStock (ArticuloId, AlmacenId, Cantidad, Motivo)
        VALUES (@ArticuloId, @AlmacenId, @Cantidad, @Motivo);

        UPDATE dbo.Stock
        SET    Cantidad = Cantidad + @Cantidad
        WHERE  ArticuloId = @ArticuloId
          AND  AlmacenId = @AlmacenId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

-- -----------------------------------------------------------------------------
-- Cierra un pedido: valida crédito, descuenta stock y genera la factura.
--
-- Este es el procedimiento crítico del sistema. Escribe en cuatro tablas sin
-- una sola transacción, así que un fallo a mitad deja el pedido cerrado sin
-- factura o el stock descontado sin pedido cerrado.
-- -----------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_CerrarPedido
    @PedidoId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ClienteId INT;
    DECLARE @Total DECIMAL(18,4) = 0;
    DECLARE @Descuento DECIMAL(9,4);
    DECLARE @FacturaId INT;
    DECLARE @Vencidas INT;

    SELECT @ClienteId = ClienteId
    FROM   dbo.Pedidos
    WHERE  PedidoId = @PedidoId;

    IF @ClienteId IS NULL
    BEGIN
        RAISERROR('El pedido no existe', 16, 1);
        RETURN;
    END

    -- Regla de negocio: no se cierra un pedido si el cliente tiene facturas
    -- vencidas hace más de 60 días.
    SELECT @Vencidas = COUNT(*)
    FROM   dbo.Facturas
    WHERE  ClienteId = @ClienteId
      AND  Cobrada = 0
      AND  DATEDIFF(DAY, Vencimiento, GETDATE()) > 60;

    IF @Vencidas > 0
    BEGIN
        RAISERROR('Cliente con facturas vencidas', 16, 1);
        RETURN;
    END

    SELECT @Total = SUM(Cantidad * PrecioUnitario)
    FROM   dbo.LineasPedido
    WHERE  PedidoId = @PedidoId;

    SET @Descuento = dbo.fn_CalcularDescuento(@ClienteId, @Total);
    SET @Total = @Total * (1 - @Descuento);

    INSERT INTO dbo.Facturas (ClienteId, PedidoId, BaseImponible, Total, Vencimiento)
    SELECT @ClienteId,
           @PedidoId,
           @Total,
           @Total * 1.21,
           DATEADD(DAY, c.DiasCredito, GETDATE())
    FROM   dbo.Clientes c
    WHERE  c.ClienteId = @ClienteId;

    SET @FacturaId = SCOPE_IDENTITY();

    INSERT INTO dbo.LineasFactura (FacturaId, ArticuloId, Cantidad, Importe)
    SELECT @FacturaId, ArticuloId, Cantidad, Cantidad * PrecioUnitario
    FROM   dbo.LineasPedido
    WHERE  PedidoId = @PedidoId;

    -- Descuenta el stock línea a línea llamando al procedimiento de movimientos.
    DECLARE @ArticuloId INT, @Cantidad DECIMAL(18,4), @Salida DECIMAL(18,4);

    DECLARE cur_lineas CURSOR FOR
        SELECT ArticuloId, Cantidad
        FROM   dbo.LineasPedido
        WHERE  PedidoId = @PedidoId;

    OPEN cur_lineas;
    FETCH NEXT FROM cur_lineas INTO @ArticuloId, @Cantidad;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @Salida = -@Cantidad;
        EXEC dbo.usp_RegistrarMovimientoStock @ArticuloId, 1, @Salida, 'VENTA';
        FETCH NEXT FROM cur_lineas INTO @ArticuloId, @Cantidad;
    END

    CLOSE cur_lineas;
    DEALLOCATE cur_lineas;

    UPDATE dbo.Pedidos
    SET    Estado = 'CERRADO',
           Total = @Total,
           FechaCierre = GETDATE()
    WHERE  PedidoId = @PedidoId;

    INSERT INTO dbo.Auditoria (Tabla, Operacion, Detalle, Usuario)
    VALUES ('Pedidos', 'CIERRE', 'Pedido ' + CAST(@PedidoId AS VARCHAR(20)), SUSER_SNAME());
END
GO

-- -----------------------------------------------------------------------------
-- Proceso nocturno que factura todos los pedidos pendientes.
-- Recorre con cursor y llama al procedimiento de cierre uno por uno.
-- -----------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_FacturarPedidosPendientes
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @PedidoId INT;
    DECLARE @Errores INT = 0;

    DECLARE cur_pedidos CURSOR FOR
        SELECT PedidoId
        FROM   dbo.vw_PedidosPendientes
        ORDER BY Fecha;

    OPEN cur_pedidos;
    FETCH NEXT FROM cur_pedidos INTO @PedidoId;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        BEGIN TRY
            EXEC dbo.usp_CerrarPedido @PedidoId;
        END TRY
        BEGIN CATCH
            SET @Errores = @Errores + 1;

            INSERT INTO dbo.Auditoria (Tabla, Operacion, Detalle)
            VALUES ('Pedidos', 'ERROR_CIERRE', ERROR_MESSAGE());
        END CATCH

        FETCH NEXT FROM cur_pedidos INTO @PedidoId;
    END

    CLOSE cur_pedidos;
    DEALLOCATE cur_pedidos;

    SELECT @Errores AS ErroresDetectados;
END
GO

-- -----------------------------------------------------------------------------
-- Informe de ventas con filtros opcionales.
-- Construye la consulta concatenando cadenas: las dependencias reales de este
-- procedimiento no se pueden determinar leyendo el código.
-- -----------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_InformeVentas
    @Desde      DATETIME = NULL,
    @Hasta      DATETIME = NULL,
    @Familia    VARCHAR(20) = NULL,
    @OrdenarPor VARCHAR(50) = 'Fecha'
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @sql NVARCHAR(MAX);

    SET @sql = N'SELECT f.FacturaId, f.Fecha, c.Nombre, f.Total
                 FROM dbo.Facturas f
                 INNER JOIN dbo.Clientes c ON c.ClienteId = f.ClienteId
                 WHERE 1 = 1 ';

    IF @Desde IS NOT NULL
        SET @sql = @sql + N' AND f.Fecha >= ''' + CONVERT(VARCHAR(10), @Desde, 120) + N''' ';

    IF @Hasta IS NOT NULL
        SET @sql = @sql + N' AND f.Fecha <= ''' + CONVERT(VARCHAR(10), @Hasta, 120) + N''' ';

    IF @Familia IS NOT NULL
        SET @sql = @sql + N' AND EXISTS (SELECT 1 FROM dbo.LineasFactura lf
                                         INNER JOIN dbo.Articulos a ON a.ArticuloId = lf.ArticuloId
                                         WHERE lf.FacturaId = f.FacturaId
                                           AND a.Familia = ''' + @Familia + N''') ';

    SET @sql = @sql + N' ORDER BY ' + @OrdenarPor;

    EXEC (@sql);
END
GO

-- -----------------------------------------------------------------------------
-- Recalcula las tarifas por familia. Proceso por etapas con tablas temporales.
-- -----------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_RecalcularTarifas
    @Familia    VARCHAR(20) = NULL,
    @Incremento DECIMAL(9,4) = 0
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        SELECT a.ArticuloId, a.Referencia, a.Familia, a.PrecioBase
        INTO   #Base
        FROM   dbo.Articulos a
        WHERE  a.Descatalogado = 0
          AND  (@Familia IS NULL OR a.Familia = @Familia);

        SELECT b.ArticuloId,
               b.Familia,
               CASE
                   WHEN b.PrecioBase > 1000 THEN b.PrecioBase * (1 + @Incremento * 0.5)
                   WHEN b.PrecioBase > 100  THEN b.PrecioBase * (1 + @Incremento)
                   ELSE b.PrecioBase * (1 + @Incremento * 1.5)
               END AS PrecioNuevo
        INTO   #Calculado
        FROM   #Base b;

        SELECT c.ArticuloId, c.Familia, c.PrecioNuevo, t.Precio AS PrecioAnterior
        INTO   #Comparado
        FROM   #Calculado c
        LEFT JOIN dbo.Tarifas t ON t.ArticuloId = c.ArticuloId;

        INSERT INTO dbo.Tarifas (ArticuloId, Familia, Precio, VigenteDesde)
        SELECT ArticuloId, Familia, PrecioNuevo, GETDATE()
        FROM   #Comparado
        WHERE  PrecioAnterior IS NULL
           OR  ABS(PrecioNuevo - PrecioAnterior) > 0.01;

        INSERT INTO dbo.Auditoria (Tabla, Operacion, Detalle)
        SELECT 'Tarifas', 'RECALCULO', CAST(COUNT(*) AS VARCHAR(20)) + ' tarifas actualizadas'
        FROM   #Comparado;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

-- -----------------------------------------------------------------------------
-- Purga la auditoría antigua. Usa sp_executesql para montar el DELETE.
-- -----------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_PurgarAuditoria
    @DiasRetencion INT = 365
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @sql NVARCHAR(MAX);
    DECLARE @Limite DATETIME = DATEADD(DAY, -@DiasRetencion, GETDATE());

    SET @sql = N'DELETE FROM dbo.Auditoria WHERE Fecha < @Limite';

    EXEC sp_executesql @sql, N'@Limite DATETIME', @Limite = @Limite;
END
GO

-- -----------------------------------------------------------------------------
-- Disparador de auditoría sobre las líneas de pedido.
-- -----------------------------------------------------------------------------
CREATE TRIGGER dbo.trg_LineasPedido_Auditoria
ON dbo.LineasPedido
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Auditoria (Tabla, Operacion, Detalle, Usuario)
    SELECT 'LineasPedido',
           'MODIFICACION',
           'Linea ' + CAST(i.LineaId AS VARCHAR(20)) + ' pedido ' + CAST(i.PedidoId AS VARCHAR(20)),
           SUSER_SNAME()
    FROM   inserted i;
END
GO
