-- ===========================================================================
-- Sistema de gestión de almacén y expediciones — «GESALM»
--
-- Segundo script de ejemplo. Reproduce un perfil de deuda distinto al del ERP
-- de facturación: aquí no hay cursores ni apenas SQL dinámico. Lo que hay es un
-- proceso por etapas con tablas temporales, lógica repartida entre muchos
-- procedimientos que se llaman en cadena, y un procedimiento central que toca
-- medio modelo de datos sin transacción ni manejo de errores.
--
-- Es el patrón del proceso nocturno que nadie se atreve a tocar.
-- ===========================================================================

CREATE TABLE dbo.Almacenes (
    AlmacenId       INT           NOT NULL PRIMARY KEY,
    Codigo          VARCHAR(10)   NOT NULL,
    Descripcion     VARCHAR(100)  NULL,
    Activo          BIT           NOT NULL DEFAULT 1
);
GO

CREATE TABLE dbo.Ubicaciones (
    UbicacionId     INT           NOT NULL PRIMARY KEY,
    AlmacenId       INT           NOT NULL,
    Pasillo         VARCHAR(5)    NULL,
    Estanteria      VARCHAR(5)    NULL,
    Altura          VARCHAR(5)    NULL,
    CapacidadKg     DECIMAL(18,3) NULL,
    Bloqueada       BIT           NOT NULL DEFAULT 0
);
GO

CREATE TABLE dbo.Articulos (
    ArticuloId      INT           NOT NULL PRIMARY KEY,
    Referencia      VARCHAR(30)   NOT NULL,
    Descripcion     VARCHAR(200)  NULL,
    PesoKg          DECIMAL(18,3) NULL,
    LargoCm         DECIMAL(18,2) NULL,
    AnchoCm         DECIMAL(18,2) NULL,
    AltoCm          DECIMAL(18,2) NULL,
    Peligroso       BIT           NOT NULL DEFAULT 0
);
GO

CREATE TABLE dbo.Existencias (
    ExistenciaId    INT           NOT NULL PRIMARY KEY,
    ArticuloId      INT           NOT NULL,
    UbicacionId     INT           NOT NULL,
    Cantidad        DECIMAL(18,3) NOT NULL,
    CantidadReservada DECIMAL(18,3) NOT NULL DEFAULT 0,
    FechaUltimoRecuento DATETIME  NULL
);
GO

CREATE TABLE dbo.Movimientos (
    MovimientoId    INT           NOT NULL PRIMARY KEY,
    ArticuloId      INT           NOT NULL,
    UbicacionOrigen INT           NULL,
    UbicacionDestino INT          NULL,
    Cantidad        DECIMAL(18,3) NOT NULL,
    Tipo            VARCHAR(20)   NOT NULL,
    Fecha           DATETIME      NOT NULL,
    Usuario         VARCHAR(50)   NULL
);
GO

CREATE TABLE dbo.Expediciones (
    ExpedicionId    INT           NOT NULL PRIMARY KEY,
    AlmacenId       INT           NOT NULL,
    TransportistaId INT           NULL,
    RutaId          INT           NULL,
    Estado          VARCHAR(20)   NOT NULL,
    FechaPrevista   DATETIME      NULL,
    FechaSalida     DATETIME      NULL,
    PesoTotalKg     DECIMAL(18,3) NULL,
    VolumenTotalM3  DECIMAL(18,3) NULL,
    Bultos          INT           NULL
);
GO

CREATE TABLE dbo.LineasExpedicion (
    LineaId         INT           NOT NULL PRIMARY KEY,
    ExpedicionId    INT           NOT NULL,
    ArticuloId      INT           NOT NULL,
    Cantidad        DECIMAL(18,3) NOT NULL,
    UbicacionId     INT           NULL,
    Preparada       BIT           NOT NULL DEFAULT 0
);
GO

CREATE TABLE dbo.Transportistas (
    TransportistaId INT           NOT NULL PRIMARY KEY,
    Nombre          VARCHAR(100)  NOT NULL,
    PesoMaximoKg    DECIMAL(18,3) NULL,
    AdmitePeligroso BIT           NOT NULL DEFAULT 0
);
GO

CREATE TABLE dbo.Rutas (
    RutaId          INT           NOT NULL PRIMARY KEY,
    Codigo          VARCHAR(20)   NOT NULL,
    Zona            VARCHAR(50)   NULL,
    HoraCorte       INT           NULL
);
GO

CREATE TABLE dbo.Incidencias (
    IncidenciaId    INT           NOT NULL PRIMARY KEY,
    ExpedicionId    INT           NULL,
    ArticuloId      INT           NULL,
    Motivo          VARCHAR(200)  NULL,
    Fecha           DATETIME      NOT NULL
);
GO

CREATE TABLE dbo.LogProceso (
    LogId           INT           NOT NULL PRIMARY KEY,
    Proceso         VARCHAR(50)   NOT NULL,
    Mensaje         VARCHAR(500)  NULL,
    Fecha           DATETIME      NOT NULL
);
GO

-- ---------------------------------------------------------------------------
-- Función de peso volumétrico. Autocontenida: candidata natural a migrar
-- primero.
-- ---------------------------------------------------------------------------
CREATE FUNCTION dbo.fn_PesoVolumetrico
(
    @LargoCm DECIMAL(18,2),
    @AnchoCm DECIMAL(18,2),
    @AltoCm  DECIMAL(18,2)
)
RETURNS DECIMAL(18,3)
AS
BEGIN
    DECLARE @Volumen DECIMAL(18,3);

    IF @LargoCm IS NULL OR @AnchoCm IS NULL OR @AltoCm IS NULL
        RETURN 0;

    -- Divisor 5000: el que usa media logística española desde los noventa.
    SET @Volumen = (@LargoCm * @AnchoCm * @AltoCm) / 5000.0;

    RETURN @Volumen;
END
GO

-- ---------------------------------------------------------------------------
-- Vista de expediciones pendientes.
-- ---------------------------------------------------------------------------
CREATE VIEW dbo.vw_ExpedicionesPendientes
AS
SELECT  e.ExpedicionId,
        e.AlmacenId,
        a.Codigo AS CodigoAlmacen,
        e.Estado,
        e.FechaPrevista,
        COUNT(l.LineaId) AS Lineas
FROM    dbo.Expediciones e
JOIN    dbo.Almacenes a ON a.AlmacenId = e.AlmacenId
LEFT JOIN dbo.LineasExpedicion l ON l.ExpedicionId = e.ExpedicionId
WHERE   e.Estado IN ('PENDIENTE', 'PREPARANDO')
GROUP BY e.ExpedicionId, e.AlmacenId, a.Codigo, e.Estado, e.FechaPrevista;
GO

-- ---------------------------------------------------------------------------
-- Registro de movimiento. Hoja del grafo: no llama a nada.
-- ---------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_RegistrarMovimiento
    @ArticuloId       INT,
    @UbicacionOrigen  INT,
    @UbicacionDestino INT,
    @Cantidad         DECIMAL(18,3),
    @Tipo             VARCHAR(20),
    @Usuario          VARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Id INT;

    SELECT @Id = ISNULL(MAX(MovimientoId), 0) + 1 FROM dbo.Movimientos;

    INSERT INTO dbo.Movimientos
        (MovimientoId, ArticuloId, UbicacionOrigen, UbicacionDestino, Cantidad, Tipo, Fecha, Usuario)
    VALUES
        (@Id, @ArticuloId, @UbicacionOrigen, @UbicacionDestino, @Cantidad, @Tipo, GETDATE(), @Usuario);
END
GO

-- ---------------------------------------------------------------------------
-- Alta de incidencia. También hoja.
-- ---------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_NotificarIncidencia
    @ExpedicionId INT,
    @ArticuloId   INT,
    @Motivo       VARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Id INT;

    SELECT @Id = ISNULL(MAX(IncidenciaId), 0) + 1 FROM dbo.Incidencias;

    INSERT INTO dbo.Incidencias (IncidenciaId, ExpedicionId, ArticuloId, Motivo, Fecha)
    VALUES (@Id, @ExpedicionId, @ArticuloId, @Motivo, GETDATE());
END
GO

-- ---------------------------------------------------------------------------
-- Asignación de ubicación. Llama al registro de movimientos.
-- ---------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_AsignarUbicacion
    @ArticuloId   INT,
    @AlmacenId    INT,
    @Cantidad     DECIMAL(18,3),
    @UbicacionId  INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @PesoUnidad DECIMAL(18,3);
    DECLARE @PesoTotal  DECIMAL(18,3);

    SELECT @PesoUnidad = PesoKg FROM dbo.Articulos WHERE ArticuloId = @ArticuloId;

    SET @PesoTotal = ISNULL(@PesoUnidad, 0) * @Cantidad;

    SELECT TOP 1 @UbicacionId = u.UbicacionId
    FROM   dbo.Ubicaciones u
    WHERE  u.AlmacenId = @AlmacenId
      AND  u.Bloqueada = 0
      AND  ISNULL(u.CapacidadKg, 0) >= @PesoTotal
    ORDER BY u.CapacidadKg ASC;

    IF @UbicacionId IS NULL
    BEGIN
        EXEC dbo.usp_NotificarIncidencia NULL, @ArticuloId, 'Sin ubicacion con capacidad suficiente';
        RETURN;
    END

    UPDATE dbo.Existencias
    SET    UbicacionId = @UbicacionId
    WHERE  ArticuloId = @ArticuloId
      AND  UbicacionId IS NULL;

    EXEC dbo.usp_RegistrarMovimiento @ArticuloId, NULL, @UbicacionId, @Cantidad, 'UBICACION', 'GESALM';
END
GO

-- ---------------------------------------------------------------------------
-- Reserva de stock. Otro eslabón de la cadena.
-- ---------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_ReservarStock
    @ExpedicionId INT,
    @ArticuloId   INT,
    @Cantidad     DECIMAL(18,3)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Disponible DECIMAL(18,3);
    DECLARE @UbicacionId INT;

    SELECT @Disponible = SUM(Cantidad - CantidadReservada)
    FROM   dbo.Existencias
    WHERE  ArticuloId = @ArticuloId;

    IF ISNULL(@Disponible, 0) < @Cantidad
    BEGIN
        EXEC dbo.usp_NotificarIncidencia @ExpedicionId, @ArticuloId, 'Stock insuficiente para reservar';
        RETURN;
    END

    UPDATE dbo.Existencias
    SET    CantidadReservada = CantidadReservada + @Cantidad
    WHERE  ArticuloId = @ArticuloId
      AND  Cantidad - CantidadReservada >= @Cantidad;

    SELECT TOP 1 @UbicacionId = UbicacionId
    FROM   dbo.Existencias
    WHERE  ArticuloId = @ArticuloId
    ORDER BY Cantidad DESC;

    UPDATE dbo.LineasExpedicion
    SET    UbicacionId = @UbicacionId
    WHERE  ExpedicionId = @ExpedicionId
      AND  ArticuloId = @ArticuloId;

    EXEC dbo.usp_RegistrarMovimiento @ArticuloId, @UbicacionId, NULL, @Cantidad, 'RESERVA', 'GESALM';
END
GO

-- ---------------------------------------------------------------------------
-- Cálculo de ruta. Lee bastante y escribe en la expedición.
-- ---------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_CalcularRuta
    @ExpedicionId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @AlmacenId       INT;
    DECLARE @PesoTotal       DECIMAL(18,3);
    DECLARE @TienePeligroso  BIT;
    DECLARE @TransportistaId INT;
    DECLARE @RutaId          INT;
    DECLARE @Hora            INT;

    SELECT @AlmacenId = AlmacenId FROM dbo.Expediciones WHERE ExpedicionId = @ExpedicionId;

    SELECT @PesoTotal = SUM(l.Cantidad * ISNULL(a.PesoKg, 0)),
           @TienePeligroso = MAX(CAST(a.Peligroso AS INT))
    FROM   dbo.LineasExpedicion l
    JOIN   dbo.Articulos a ON a.ArticuloId = l.ArticuloId
    WHERE  l.ExpedicionId = @ExpedicionId;

    SET @Hora = DATEPART(HOUR, GETDATE());

    SELECT TOP 1 @RutaId = RutaId
    FROM   dbo.Rutas
    WHERE  ISNULL(HoraCorte, 23) >= @Hora
    ORDER BY HoraCorte ASC;

    SELECT TOP 1 @TransportistaId = TransportistaId
    FROM   dbo.Transportistas
    WHERE  ISNULL(PesoMaximoKg, 0) >= ISNULL(@PesoTotal, 0)
      AND  (@TienePeligroso = 0 OR AdmitePeligroso = 1)
    ORDER BY PesoMaximoKg ASC;

    IF @TransportistaId IS NULL
    BEGIN
        EXEC dbo.usp_NotificarIncidencia @ExpedicionId, NULL, 'Sin transportista compatible';
        RETURN;
    END

    UPDATE dbo.Expediciones
    SET    TransportistaId = @TransportistaId,
           RutaId          = @RutaId,
           PesoTotalKg     = @PesoTotal
    WHERE  ExpedicionId = @ExpedicionId;
END
GO

-- ---------------------------------------------------------------------------
-- El proceso nocturno. Aquí está toda la deuda:
--
--   · doce parámetros de entrada
--   · cinco tablas temporales como etapas del proceso
--   · llama a cuatro procedimientos distintos
--   · toca la mayor parte del modelo
--   · ni una transacción, ni un TRY/CATCH
--   · anidamiento profundo con banderas que se reutilizan
--
-- No usa cursores, que es justo lo que lo hace distinto del otro ejemplo: la
-- deuda no está en la iteración fila a fila, está en el reparto de la lógica y
-- en la ausencia de garantías.
-- ---------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_ConsolidarExpediciones
    @AlmacenId         INT,
    @FechaDesde        DATETIME,
    @FechaHasta        DATETIME,
    @SoloPreparadas    BIT           = 0,
    @IncluirPeligrosos BIT           = 1,
    @PesoMinimoKg      DECIMAL(18,3) = 0,
    @PesoMaximoKg      DECIMAL(18,3) = 99999,
    @MaxBultos         INT           = 100,
    @ReasignarUbicacion BIT          = 1,
    @RecalcularRutas   BIT           = 1,
    @Usuario           VARCHAR(50)   = 'BATCH',
    @Traza             BIT           = 0
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @LogId          INT;
    DECLARE @ExpedicionId   INT;
    DECLARE @ArticuloId     INT;
    DECLARE @Cantidad       DECIMAL(18,3);
    DECLARE @UbicacionId    INT;
    DECLARE @Peso           DECIMAL(18,3);
    DECLARE @Volumen        DECIMAL(18,3);
    DECLARE @Bultos         INT;
    DECLARE @Procesadas     INT = 0;
    DECLARE @Descartadas    INT = 0;
    DECLARE @Fila           INT;
    DECLARE @Total          INT;
    DECLARE @Continuar      BIT = 1;

    SELECT @LogId = ISNULL(MAX(LogId), 0) + 1 FROM dbo.LogProceso;

    INSERT INTO dbo.LogProceso (LogId, Proceso, Mensaje, Fecha)
    VALUES (@LogId, 'CONSOLIDAR', 'Inicio', GETDATE());

    -- Etapa 0: validaciones previas, cada una con su bandera y su salida
    DECLARE @AlmacenActivo   BIT;
    DECLARE @UbicacionesLibres INT;
    DECLARE @StockDescuadrado  INT;
    DECLARE @Transportistas    INT;

    SELECT @AlmacenActivo = Activo FROM dbo.Almacenes WHERE AlmacenId = @AlmacenId;

    IF @AlmacenActivo IS NULL
    BEGIN
        INSERT INTO dbo.LogProceso (LogId, Proceso, Mensaje, Fecha)
        VALUES (@LogId + 1, 'CONSOLIDAR', 'Almacen inexistente', GETDATE());
        RETURN;
    END

    IF @AlmacenActivo = 0
    BEGIN
        INSERT INTO dbo.LogProceso (LogId, Proceso, Mensaje, Fecha)
        VALUES (@LogId + 1, 'CONSOLIDAR', 'Almacen inactivo', GETDATE());
        RETURN;
    END

    SELECT @UbicacionesLibres = COUNT(*)
    FROM   dbo.Ubicaciones
    WHERE  AlmacenId = @AlmacenId AND Bloqueada = 0;

    IF @UbicacionesLibres = 0 AND @ReasignarUbicacion = 1
    BEGIN
        SET @ReasignarUbicacion = 0;

        INSERT INTO dbo.LogProceso (LogId, Proceso, Mensaje, Fecha)
        VALUES (@LogId + 1, 'CONSOLIDAR', 'Sin ubicaciones libres: no se reasigna', GETDATE());
    END

    SELECT @StockDescuadrado = COUNT(*)
    FROM   dbo.Existencias
    WHERE  CantidadReservada > Cantidad;

    IF @StockDescuadrado > 0
    BEGIN
        IF @Traza = 1
        BEGIN
            INSERT INTO dbo.LogProceso (LogId, Proceso, Mensaje, Fecha)
            VALUES (@LogId + 1, 'CONSOLIDAR',
                    'Existencias descuadradas: ' + CAST(@StockDescuadrado AS VARCHAR(10)), GETDATE());
        END
    END

    SELECT @Transportistas = COUNT(*)
    FROM   dbo.Transportistas
    WHERE  ISNULL(PesoMaximoKg, 0) >= @PesoMinimoKg;

    IF @Transportistas = 0 AND @RecalcularRutas = 1
    BEGIN
        SET @RecalcularRutas = 0;
    END

    -- Etapa 1: candidatas
    CREATE TABLE #Candidatas (
        Fila         INT IDENTITY(1,1),
        ExpedicionId INT,
        Estado       VARCHAR(20),
        Lineas       INT,
        Peso         DECIMAL(18,3)
    );

    INSERT INTO #Candidatas (ExpedicionId, Estado, Lineas, Peso)
    SELECT  e.ExpedicionId,
            e.Estado,
            COUNT(l.LineaId),
            SUM(l.Cantidad * ISNULL(a.PesoKg, 0))
    FROM    dbo.Expediciones e
    LEFT JOIN dbo.LineasExpedicion l ON l.ExpedicionId = e.ExpedicionId
    LEFT JOIN dbo.Articulos a ON a.ArticuloId = l.ArticuloId
    WHERE   e.AlmacenId = @AlmacenId
      AND   e.FechaPrevista BETWEEN @FechaDesde AND @FechaHasta
      AND   e.Estado <> 'ENVIADA'
    GROUP BY e.ExpedicionId, e.Estado;

    -- Etapa 2: descartes por peso
    CREATE TABLE #Descartadas (
        ExpedicionId INT,
        Motivo       VARCHAR(100)
    );

    INSERT INTO #Descartadas (ExpedicionId, Motivo)
    SELECT ExpedicionId, 'Peso fuera de rango'
    FROM   #Candidatas
    WHERE  ISNULL(Peso, 0) < @PesoMinimoKg
       OR  ISNULL(Peso, 0) > @PesoMaximoKg;

    DELETE FROM #Candidatas
    WHERE  ExpedicionId IN (SELECT ExpedicionId FROM #Descartadas);

    IF @SoloPreparadas = 1
    BEGIN
        INSERT INTO #Descartadas (ExpedicionId, Motivo)
        SELECT c.ExpedicionId, 'Tiene lineas sin preparar'
        FROM   #Candidatas c
        WHERE  EXISTS (SELECT 1
                       FROM   dbo.LineasExpedicion l
                       WHERE  l.ExpedicionId = c.ExpedicionId
                         AND  l.Preparada = 0);

        DELETE FROM #Candidatas
        WHERE  ExpedicionId IN (SELECT ExpedicionId FROM #Descartadas);
    END

    IF @IncluirPeligrosos = 0
    BEGIN
        INSERT INTO #Descartadas (ExpedicionId, Motivo)
        SELECT DISTINCT c.ExpedicionId, 'Contiene mercancia peligrosa'
        FROM   #Candidatas c
        JOIN   dbo.LineasExpedicion l ON l.ExpedicionId = c.ExpedicionId
        JOIN   dbo.Articulos a ON a.ArticuloId = l.ArticuloId
        WHERE  a.Peligroso = 1;

        DELETE FROM #Candidatas
        WHERE  ExpedicionId IN (SELECT ExpedicionId FROM #Descartadas);
    END

    -- Etapa 3: lineas a tratar
    CREATE TABLE #Lineas (
        Fila         INT IDENTITY(1,1),
        ExpedicionId INT,
        ArticuloId   INT,
        Cantidad     DECIMAL(18,3),
        UbicacionId  INT
    );

    INSERT INTO #Lineas (ExpedicionId, ArticuloId, Cantidad, UbicacionId)
    SELECT l.ExpedicionId, l.ArticuloId, l.Cantidad, l.UbicacionId
    FROM   dbo.LineasExpedicion l
    JOIN   #Candidatas c ON c.ExpedicionId = l.ExpedicionId;

    -- Etapa 4: volúmenes
    CREATE TABLE #Volumenes (
        ExpedicionId INT,
        Volumen      DECIMAL(18,3),
        Bultos       INT
    );

    INSERT INTO #Volumenes (ExpedicionId, Volumen, Bultos)
    SELECT  l.ExpedicionId,
            SUM(dbo.fn_PesoVolumetrico(a.LargoCm, a.AnchoCm, a.AltoCm) * l.Cantidad),
            COUNT(DISTINCT l.ArticuloId)
    FROM    #Lineas l
    JOIN    dbo.Articulos a ON a.ArticuloId = l.ArticuloId
    GROUP BY l.ExpedicionId;

    -- Etapa 5: resultado
    CREATE TABLE #Resultado (
        ExpedicionId INT,
        Peso         DECIMAL(18,3),
        Volumen      DECIMAL(18,3),
        Bultos       INT
    );

    SET @Fila = 1;
    SELECT @Total = COUNT(*) FROM #Lineas;

    WHILE @Fila <= @Total AND @Continuar = 1
    BEGIN
        SELECT @ExpedicionId = ExpedicionId,
               @ArticuloId   = ArticuloId,
               @Cantidad     = Cantidad,
               @UbicacionId  = UbicacionId
        FROM   #Lineas
        WHERE  Fila = @Fila;

        IF @Cantidad IS NULL OR @Cantidad <= 0
        BEGIN
            EXEC dbo.usp_NotificarIncidencia @ExpedicionId, @ArticuloId, 'Cantidad no valida';
            SET @Descartadas = @Descartadas + 1;
        END
        ELSE
        BEGIN
            IF @UbicacionId IS NULL
            BEGIN
                IF @ReasignarUbicacion = 1
                BEGIN
                    EXEC dbo.usp_AsignarUbicacion @ArticuloId, @AlmacenId, @Cantidad, @UbicacionId OUTPUT;

                    IF @UbicacionId IS NULL
                    BEGIN
                        EXEC dbo.usp_NotificarIncidencia @ExpedicionId, @ArticuloId, 'No se pudo ubicar';
                        SET @Descartadas = @Descartadas + 1;
                    END
                    ELSE
                    BEGIN
                        EXEC dbo.usp_ReservarStock @ExpedicionId, @ArticuloId, @Cantidad;
                        SET @Procesadas = @Procesadas + 1;
                    END
                END
                ELSE
                BEGIN
                    EXEC dbo.usp_NotificarIncidencia @ExpedicionId, @ArticuloId, 'Sin ubicacion y sin reasignar';
                    SET @Descartadas = @Descartadas + 1;
                END
            END
            ELSE
            BEGIN
                EXEC dbo.usp_ReservarStock @ExpedicionId, @ArticuloId, @Cantidad;
                SET @Procesadas = @Procesadas + 1;
            END
        END

        IF @Procesadas > @MaxBultos
        BEGIN
            SET @Continuar = 0;
        END

        SET @Fila = @Fila + 1;
    END

    INSERT INTO #Resultado (ExpedicionId, Peso, Volumen, Bultos)
    SELECT  c.ExpedicionId,
            c.Peso,
            v.Volumen,
            v.Bultos
    FROM    #Candidatas c
    LEFT JOIN #Volumenes v ON v.ExpedicionId = c.ExpedicionId;

    UPDATE  e
    SET     e.PesoTotalKg    = r.Peso,
            e.VolumenTotalM3 = r.Volumen,
            e.Bultos         = r.Bultos,
            e.Estado         = 'CONSOLIDADA'
    FROM    dbo.Expediciones e
    JOIN    #Resultado r ON r.ExpedicionId = e.ExpedicionId;

    IF @RecalcularRutas = 1
    BEGIN
        SET @Fila = 1;
        SELECT @Total = COUNT(*) FROM #Resultado;

        WHILE @Fila <= @Total
        BEGIN
            SELECT TOP 1 @ExpedicionId = ExpedicionId
            FROM   #Resultado
            ORDER BY ExpedicionId;

            EXEC dbo.usp_CalcularRuta @ExpedicionId;

            DELETE FROM #Resultado WHERE ExpedicionId = @ExpedicionId;

            SET @Fila = @Fila + 1;
        END
    END

    INSERT INTO dbo.Incidencias (IncidenciaId, ExpedicionId, ArticuloId, Motivo, Fecha)
    SELECT  ISNULL((SELECT MAX(IncidenciaId) FROM dbo.Incidencias), 0)
            + ROW_NUMBER() OVER (ORDER BY d.ExpedicionId),
            d.ExpedicionId,
            NULL,
            d.Motivo,
            GETDATE()
    FROM    #Descartadas d;

    -- Etapa 6: movimiento de resumen del proceso, para cuadrar el inventario
    IF @Procesadas > 0
    BEGIN
        INSERT INTO dbo.Movimientos
            (MovimientoId, ArticuloId, UbicacionOrigen, UbicacionDestino, Cantidad, Tipo, Fecha, Usuario)
        SELECT  ISNULL((SELECT MAX(MovimientoId) FROM dbo.Movimientos), 0)
                + ROW_NUMBER() OVER (ORDER BY l.ArticuloId),
                l.ArticuloId,
                NULL,
                NULL,
                SUM(l.Cantidad),
                'CONSOLIDACION',
                GETDATE(),
                @Usuario
        FROM    #Lineas l
        GROUP BY l.ArticuloId;
    END

    SET @LogId = @LogId + 1;

    INSERT INTO dbo.LogProceso (LogId, Proceso, Mensaje, Fecha)
    VALUES (@LogId, 'CONSOLIDAR',
            'Fin. Procesadas ' + CAST(@Procesadas AS VARCHAR(10))
            + ', descartadas ' + CAST(@Descartadas AS VARCHAR(10)), GETDATE());

    DROP TABLE #Resultado;
    DROP TABLE #Volumenes;
    DROP TABLE #Lineas;
    DROP TABLE #Descartadas;
    DROP TABLE #Candidatas;
END
GO

-- ---------------------------------------------------------------------------
-- Disparador de auditoría. Escribe en el log en cada movimiento.
-- ---------------------------------------------------------------------------
CREATE TRIGGER dbo.tr_Movimientos_Auditoria
ON dbo.Movimientos
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.LogProceso (LogId, Proceso, Mensaje, Fecha)
    SELECT  ISNULL((SELECT MAX(LogId) FROM dbo.LogProceso), 0)
            + ROW_NUMBER() OVER (ORDER BY i.MovimientoId),
            'MOVIMIENTO',
            'Articulo ' + CAST(i.ArticuloId AS VARCHAR(10))
            + ' cantidad ' + CAST(i.Cantidad AS VARCHAR(20)),
            GETDATE()
    FROM    inserted i;
END
GO
