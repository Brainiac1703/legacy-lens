using Microsoft.AspNetCore.Identity;

namespace LegacyLens.Persistence.EF;

/// <summary>
/// Opciones de Identity que afectan al **esquema** de la base de datos.
///
/// Están aquí, y no en el arranque de la aplicación, porque las necesitan dos
/// sitios: la configuración de Identity en tiempo de ejecución y la factoría de
/// tiempo de diseño que usa «dotnet ef» para generar migraciones.
///
/// Tenerlas duplicadas ya causó un fallo: la factoría no las conocía, EF generó
/// el esquema con la versión por omisión y la migración salió **sin la tabla de
/// passkeys**, que la plantilla de Identity sí usa. El esquema y el código
/// habrían divergido en silencio hasta que alguien intentara iniciar sesión.
/// </summary>
public static class IdentityDefaults
{
    /// <summary>
    /// Versión 3 del esquema de Identity: la que incluye las passkeys. Cambiarla
    /// es un cambio de esquema y exige una migración nueva.
    ///
    /// No es const porque IdentitySchemaVersions no es una enumeración: expone
    /// instancias de System.Version.
    /// </summary>
    public static readonly Version SchemaVersion = IdentitySchemaVersions.Version3;
}
