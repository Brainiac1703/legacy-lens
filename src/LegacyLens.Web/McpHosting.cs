using System.Security.Cryptography;
using System.Text;
using LegacyLens.Mcp.Tools;
using Microsoft.Extensions.Options;

namespace LegacyLens.Web;

/// <summary>
/// Expone las mismas herramientas MCP que el ejecutable local, pero por HTTP y
/// dentro de la aplicación desplegada.
///
/// El motivo es que la base de datos de producción solo admite identidades de
/// Entra y su cortafuegos solo deja pasar servicios de Azure, así que una
/// herramienta instalada en la máquina de otra persona no puede llegar a ella.
/// Puestas aquí, la credencial que abre la base es la identidad administrada del
/// contenedor y nunca sale de Azure: quien consulta solo presenta un token.
/// </summary>
public static class McpHosting
{
    private const string Path = "/mcp";

    public static IServiceCollection AddMcpHttpServer(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Sin validación al arrancar, a diferencia del hospedaje stdio. Ahí el
        // servidor no tiene otro propósito y sin el correo debe negarse a
        // arrancar; aquí es una función opcional de una aplicación web, y
        // exigirla impedía que la imagen arrancara sin ella. Lo detectó el CI
        // al levantar el contenedor solo con la cadena de conexión.
        //
        // La configuración se comprueba al publicar el endpoint, que es donde
        // se sabe si alguien pretendía activarlo.
        services.AddOptions<McpOptions>()
            .Bind(configuration.GetSection("Mcp"));

        services.AddScoped<OwnerResolver>();

        services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<KnowledgeTools>();

        return services;
    }

    public static WebApplication MapMcpHttpServer(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<McpOptions>>().Value;

        // Sin un token válido el endpoint no se publica. Es deliberado: el
        // valor por omisión de una superficie sin autenticar tiene que ser que
        // no exista, y no que esté abierta porque alguien olvidó una variable.
        //
        // El mínimo de longitud no es cosmético. La infraestructura crea el
        // secreto con un relleno corto porque el valor real lo escribe el
        // pipeline, y si ese paso fallara, un token que está escrito en un
        // repositorio público no debe servir para entrar.
        const int minimumKeyLength = 32;

        if (options.ApiKey.Trim().Length < minimumKeyLength)
        {
            app.Logger.LogWarning(
                "Mcp:ApiKey ausente o de menos de {Minimum} caracteres, así que {Path} no se publica.",
                minimumKeyLength, Path);
            return app;
        }

        // Con un token válido y sin correo sí se falla, y pronto: alguien
        // configuró el token, así que pretendía publicar el endpoint. Arrancar
        // igual lo dejaría respondiendo a todo con una lista vacía, que es
        // indistinguible de «no hay nada analizado».
        if (string.IsNullOrWhiteSpace(options.OwnerEmail))
        {
            throw new InvalidOperationException(
                "Mcp:ApiKey está configurado pero falta Mcp:OwnerEmail. El servidor necesita "
                + "saber de quién son los análisis que puede leer.");
        }

        var expected = Encoding.UTF8.GetBytes(options.ApiKey.Trim());

        app.MapMcp(Path)
            .AddEndpointFilter(async (context, next) =>
            {
                var header = context.HttpContext.Request.Headers.Authorization.ToString();

                const string prefix = "Bearer ";
                var presented = header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? Encoding.UTF8.GetBytes(header[prefix.Length..].Trim())
                    : [];

                // Comparación de tiempo fijo: con un igual normal, el tiempo de
                // respuesta filtra cuántos caracteres iniciales son correctos.
                if (CryptographicOperations.FixedTimeEquals(presented, expected))
                {
                    return await next(context);
                }

                context.HttpContext.Response.Headers.WWWAuthenticate = "Bearer";

                // El 401 lleva cuerpo a propósito. Con la respuesta vacía,
                // UseStatusCodePagesWithReExecute la intercepta y reejecuta la
                // petición contra /not-found; ese POST con application/json
                // contra una página Razor lo rechaza antiforgery, y el cliente
                // recibía un 400 con «The request has an incorrect
                // Content-type» en lugar del 401. Una respuesta con cuerpo no
                // se intercepta.
                return Results.Json(
                    new { error = "unauthorized" },
                    statusCode: StatusCodes.Status401Unauthorized);
            })

            // El endpoint recibe JSON-RPC, no formularios, y el cliente es un
            // agente sin cookies ni token de antifalsificación. Sin esto la
            // primera llamada responde 400 sin explicar por qué.
            .DisableAntiforgery();

        return app;
    }
}
