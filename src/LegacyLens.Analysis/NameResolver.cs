using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace LegacyLens.Analysis;

/// <summary>
/// Normaliza nombres de objetos a la forma <c>esquema.objeto</c>, para que el
/// grafo no acabe con la misma tabla duplicada por escribirse de dos maneras.
/// </summary>
internal static class NameResolver
{
    public const string DefaultSchema = "dbo";

    public static string? Resolve(SchemaObjectName? name)
    {
        var baseName = name?.BaseIdentifier?.Value;
        if (string.IsNullOrWhiteSpace(baseName)) return null;

        var schema = name!.SchemaIdentifier?.Value;
        return string.IsNullOrWhiteSpace(schema)
            ? $"{DefaultSchema}.{baseName}"
            : $"{schema}.{baseName}";
    }

    public static bool IsTemporary(SchemaObjectName? name)
    {
        var baseName = name?.BaseIdentifier?.Value;
        return baseName is not null && baseName.StartsWith('#');
    }

    /// <summary>
    /// Tablas virtuales que SQL Server expone dentro de los disparadores. No
    /// son objetos del esquema y no deben aparecer en el grafo.
    /// </summary>
    public static bool IsPseudoTable(SchemaObjectName? name)
    {
        var baseName = name?.BaseIdentifier?.Value;
        if (baseName is null) return false;

        // Solo cuentan si van sin cualificar: dbo.inserted sí sería una tabla real.
        if (name!.SchemaIdentifier?.Value is not null) return false;

        return baseName.Equals("inserted", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("deleted", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Procedimientos del sistema: ruido para un análisis de migración.</summary>
    public static bool IsSystemObject(string qualifiedName)
    {
        var bare = qualifiedName.Split('.').Last();
        return bare.StartsWith("sp_", StringComparison.OrdinalIgnoreCase)
            || bare.StartsWith("xp_", StringComparison.OrdinalIgnoreCase);
    }
}
