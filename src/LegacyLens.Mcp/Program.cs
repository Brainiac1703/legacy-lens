using LegacyLens.Application;
using LegacyLens.Mcp;
using LegacyLens.Persistence.EF;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ---------------------------------------------------------------------------
// Servidor MCP de Legacy Lens.
//
// Expone el conocimiento de un sistema heredado ya analizado como herramientas
// de Model Context Protocol, para que un agente pueda consultarlo mientras
// escribe el código de la migración. Es la diferencia entre una herramienta que
// consultas y contexto que tu agente ya tiene.
//
// Lee la misma base de datos que la aplicación web y reutiliza sus consultas
// CQRS: aquí no hay lógica de negocio, solo traducción de MCP a MediatR. Si
// hubiera que reimplementar algo, sería señal de que la consulta debería estar
// en la capa de aplicación y no aquí.
// ---------------------------------------------------------------------------

// La raíz de contenido se fija al directorio del ejecutable y no se deja en el
// de trabajo. Un servidor MCP lo lanza el agente desde donde le convenga —la
// carpeta del proyecto que se está migrando, normalmente— y con el valor por
// omisión no encontraría su propio appsettings.json. El síntoma era una
// excepción de cadena de conexión ausente que no tenía nada que ver con la
// causa.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// Detalle que rompe el protocolo si se pasa por alto: en el transporte stdio la
// salida estándar ES el canal de mensajes JSON-RPC. Cualquier línea de log que
// caiga ahí corrompe la conversación con el cliente, y el síntoma es un
// servidor que «no responde» sin ningún error visible. Todo a stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddPersistence(builder.Configuration);

builder.Services.AddOptions<McpOptions>()
    .Bind(builder.Configuration.GetSection("Mcp"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.OwnerEmail),
        "Falta Mcp:OwnerEmail. El servidor necesita saber de quién son los análisis que puede leer.")
    .ValidateOnStart();

builder.Services.AddScoped<OwnerResolver>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
