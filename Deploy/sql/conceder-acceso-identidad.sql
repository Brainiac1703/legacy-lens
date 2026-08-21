-- ---------------------------------------------------------------------------
-- Da de alta la identidad administrada de la aplicación como usuario de la
-- base de datos.
--
-- Crear el Container App con identidad y asignarle roles de Azure no basta:
-- Azure SQL es un plano de datos aparte, y el permiso hay que concederlo con
-- T-SQL desde dentro. Es la única pieza del despliegue que no puede expresarse
-- en Terraform, y por eso vive aquí y la ejecuta el pipeline.
--
-- Lo ejecuta la identidad de infraestructura, que Terraform ha dejado como
-- administradora de Entra del servidor.
--
-- Idempotente: se puede lanzar en cada despliegue sin efectos.
-- ---------------------------------------------------------------------------

SET NOCOUNT ON;

-- EngineEdition 5 es Azure SQL Database. En un SQL Server normal —el de
-- docker-compose, por ejemplo— no existe el proveedor externo y esto no aplica.
IF SERVERPROPERTY('EngineEdition') <> 5
BEGIN
    PRINT 'No es Azure SQL Database: no hay identidad que dar de alta.';
END
ELSE
BEGIN
    DECLARE @identidad SYSNAME = '$(APP_IDENTITY)';
    DECLARE @sql NVARCHAR(MAX);

    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @identidad)
    BEGIN
        -- SQL dinámico porque CREATE USER no admite el nombre como parámetro.
        -- QUOTENAME evita la inyección a través del nombre del recurso.
        SET @sql = N'CREATE USER ' + QUOTENAME(@identidad) + N' FROM EXTERNAL PROVIDER;';
        EXEC sp_executesql @sql;

        PRINT 'Usuario ' + @identidad + ' creado.';
    END
    ELSE
    BEGIN
        PRINT 'El usuario ' + @identidad + ' ya existía.';
    END

    -- Solo lectura y escritura de datos. La aplicación no necesita modificar el
    -- esquema: de las migraciones se encarga el pipeline con la identidad de
    -- infraestructura, así que dar db_owner al contenedor sería regalar
    -- permisos que nunca va a usar.
    IF NOT EXISTS (
        SELECT 1
        FROM sys.database_role_members rm
        JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
        JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
        WHERE r.name = 'db_datareader' AND m.name = @identidad)
    BEGIN
        SET @sql = N'ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@identidad) + N';';
        EXEC sp_executesql @sql;
    END

    IF NOT EXISTS (
        SELECT 1
        FROM sys.database_role_members rm
        JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
        JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
        WHERE r.name = 'db_datawriter' AND m.name = @identidad)
    BEGIN
        SET @sql = N'ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@identidad) + N';';
        EXEC sp_executesql @sql;
    END

    PRINT 'Permisos de lectura y escritura garantizados.';
END
GO
