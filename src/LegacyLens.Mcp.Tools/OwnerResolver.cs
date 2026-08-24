using LegacyLens.Persistence.EF;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LegacyLens.Mcp.Tools;

/// <summary>Configuración del servidor MCP.</summary>
public sealed class McpOptions
{
    /// <summary>
    /// Correo del propietario de los análisis que este servidor puede leer.
    ///
    /// El servidor no autentica a nadie: se ejecuta en la máquina de una
    /// persona, lanzado por su propio agente, con las credenciales que esa
    /// persona le da. Lo que sí hace es no salirse de los análisis de un
    /// usuario, porque las consultas de la capa de aplicación exigen el
    /// identificador del propietario y no hay forma de pedirles «todos».
    /// </summary>
    public string OwnerEmail { get; set; } = string.Empty;

    /// <summary>
    /// Token que exige el hospedaje HTTP en la cabecera Authorization. Lo usa
    /// solo ese hospedaje: el ejecutable stdio no autentica porque lo lanza el
    /// propio agente de quien lo instala, con las credenciales que esa persona
    /// le da. Vacío significa que el endpoint HTTP no se publica.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Traduce el correo configurado al identificador de usuario que esperan las
/// consultas. Se resuelve en cada llamada en lugar de una vez al arrancar: el
/// servidor puede vivir horas dentro de una sesión del agente, y cachear un
/// identificador de un usuario que se borre entre medias solo cambiaría un
/// error claro por uno confuso.
/// </summary>
public sealed class OwnerResolver(LegacyLensDbContext db, IOptions<McpOptions> options)
{
    public async Task<string> GetOwnerUserIdAsync(CancellationToken cancellationToken)
    {
        var correo = options.Value.OwnerEmail.Trim();

        var id = await db.Users
            .Where(u => u.NormalizedEmail == correo.ToUpperInvariant())
            .Select(u => u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return id ?? throw new InvalidOperationException(
            $"No hay ningún usuario con el correo {correo} en la base de datos configurada.");
    }
}
