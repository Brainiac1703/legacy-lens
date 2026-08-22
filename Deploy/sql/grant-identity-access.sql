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
-- Por qué SID y no FROM EXTERNAL PROVIDER, que sería lo obvio:
--
--   FROM EXTERNAL PROVIDER hace que el servidor resuelva el nombre contra
--   Entra, y para eso el propio servidor SQL necesita una identidad con el rol
--   de directorio «Directory Readers». Ese rol da lectura de todo el directorio
--   del tenant, y concederlo a un servidor por un único usuario de base de
--   datos es desproporcionado en un directorio corporativo. Sin él, el intento
--   falla con Msg 33134, «Server identity is not configured».
--
--   Creando el usuario con su SID no hace falta resolver nada: el SID de una
--   identidad administrada es su client_id, y lo sabemos de antemano porque lo
--   produce Terraform. Cero permisos de Entra, cero identidad en el servidor.
--
-- Idempotente: se puede lanzar en cada despliegue sin efectos.
-- ---------------------------------------------------------------------------

SET NOCOUNT ON;

-- EngineEdition 5 es Azure SQL Database. En un SQL Server normal —el de
-- docker-compose, por ejemplo— no existen los usuarios externos y esto no
-- aplica.
IF SERVERPROPERTY('EngineEdition') <> 5
BEGIN
    PRINT 'No es Azure SQL Database: no hay identidad que dar de alta.';
END
ELSE
BEGIN
    DECLARE @identidad SYSNAME = '$(APP_IDENTITY)';
    DECLARE @clientId NVARCHAR(100) = '$(APP_CLIENT_ID)';
    DECLARE @sql NVARCHAR(MAX);

    -- El orden de bytes importa y no es el de la representación en texto: SQL
    -- Server invierte los tres primeros grupos del GUID. Este CAST produce
    -- exactamente los mismos bytes que Guid.ToByteArray() en .NET, que es el
    -- orden que Azure SQL espera para un principal externo. Comprobado
    -- ejecutando las dos conversiones y comparándolas.
    DECLARE @sid VARBINARY(16) = CAST(CAST(@clientId AS UNIQUEIDENTIFIER) AS VARBINARY(16));

    DECLARE @sidActual VARBINARY(85) =
        (SELECT sid FROM sys.database_principals WHERE name = @identidad);

    -- Si la identidad administrada se recrea, conserva el nombre pero cambia de
    -- client_id. El usuario de la base de datos seguiría existiendo apuntando al
    -- identificador viejo, así que este script no haría nada y la aplicación no
    -- podría autenticarse. Un fallo silencioso y muy difícil de rastrear: el
    -- usuario está, los roles están, y el login se rechaza.
    IF @sidActual IS NOT NULL AND @sidActual <> @sid
    BEGIN
        SET @sql = N'DROP USER ' + QUOTENAME(@identidad) + N';';
        EXEC sp_executesql @sql;
        SET @sidActual = NULL;

        PRINT 'El usuario existía con un SID distinto: se recrea.';
    END

    IF @sidActual IS NULL
    BEGIN
        -- SQL dinámico porque CREATE USER no admite el nombre como parámetro.
        -- QUOTENAME evita la inyección a través del nombre del recurso.
        SET @sql = N'CREATE USER ' + QUOTENAME(@identidad)
                 + N' WITH SID = 0x' + CONVERT(VARCHAR(100), @sid, 2)
                 + N', TYPE = E;';
        EXEC sp_executesql @sql;

        PRINT 'Usuario ' + @identidad + ' creado.';
    END
    ELSE
    BEGIN
        PRINT 'El usuario ' + @identidad + ' ya existía con el SID correcto.';
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

    -- Testigo para el pipeline, y no es decorativo: sqlcmd devuelve 0 aunque no
    -- consiga conectarse —-b solo cubre errores de T-SQL— así que el paso
    -- comprueba que esta línea aparece en la salida. Se emite solo si el usuario
    -- existe con el SID correcto y está en los dos roles, de modo que el testigo
    -- afirma el resultado y no solo que el script llegó al final.
    IF EXISTS (
        SELECT 1
        FROM sys.database_principals u
        WHERE u.name = @identidad
          AND u.sid = @sid
          AND EXISTS (
              SELECT 1 FROM sys.database_role_members rm
              JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
              WHERE r.name = 'db_datareader' AND rm.member_principal_id = u.principal_id)
          AND EXISTS (
              SELECT 1 FROM sys.database_role_members rm
              JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
              WHERE r.name = 'db_datawriter' AND rm.member_principal_id = u.principal_id))
    BEGIN
        SELECT 'GRANT_CONFIRMED' AS resultado;
    END
END
GO
