using LegacyLens.Ai;
using LegacyLens.Analysis;
using LegacyLens.Application;
using LegacyLens.Application.Analyses;
using LegacyLens.Persistence.EF;
using LegacyLens.Persistence.EF.Entities;
using LegacyLens.Web;
using LegacyLens.Web.Components;
using LegacyLens.Web.Components.Account;
using MediatR;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using System.Globalization;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ---------------------------------------------------------------------------
// Idiomas
//
// El español de España es el idioma por omisión y vive en los .resx neutros, así
// que una cultura sin traducir cae en español en lugar de mostrar la clave.
// ---------------------------------------------------------------------------

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    string[] supported = ["es-ES", "en"];

    options.SetDefaultCulture("es-ES")
        .AddSupportedCultures(supported)
        .AddSupportedUICultures(supported);

    // Solo la cookie y la cabecera del navegador. Se descarta el proveedor de
    // cadena de consulta: dejaría que un enlace compartido cambiara el idioma
    // de quien lo abre sin que lo hubiera pedido.
    options.RequestCultureProviders =
    [
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider()
    ];
});

// ---------------------------------------------------------------------------
// Capas de la aplicación.
//
// Cada capa se registra a sí misma y la presentación solo las compone. Este
// bloque es la única parte del proyecto web que conoce la existencia de las
// demás: nada de aquí abajo sabe que hay Blazor delante.
// ---------------------------------------------------------------------------

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddAnalysis();
builder.Services.AddAi(builder.Configuration);

// ---------------------------------------------------------------------------
// Identidad y presentación
// ---------------------------------------------------------------------------

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

// DefaultSignInScheme apuntaba al esquema externo, que es lo que la plantilla
// necesita para guardar la identidad que devuelve un proveedor externo. Al no
// haber ningún proveedor, era configuración que solo despistaba. El inicio de
// sesión con contraseña firma con el esquema de aplicación explícitamente, así
// que no depende de este valor.
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
    })
    .AddIdentityCookies();

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;

        // Desde la capa de persistencia, porque la versión del esquema también
        // la necesita la factoría de tiempo de diseño. Tenerla en dos sitios ya
        // generó una migración sin la tabla de passkeys.
        options.Stores.SchemaVersion = IdentityDefaults.SchemaVersion;
    })
    .AddEntityFrameworkStores<LegacyLensDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// Container Apps termina el TLS en su proxy y reenvía en claro al contenedor.
// Sin esto, la redirección a HTTPS entraría en bucle.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // El proxy de Container Apps no tiene una IP fija conocida, así que no se
    // puede restringir por red de origen. Es aceptable porque el contenedor no
    // es alcanzable desde fuera del entorno más que a través de ese proxy.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// Antes de servir cualquier componente: fija la cultura de la petición, y el
// circuito de Blazor la hereda de ahí.
app.UseRequestLocalization();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

// Cambio de idioma. Va como endpoint y no como componente porque cambiar la
// cultura exige recargar: el circuito de Blazor ya se creó con la anterior.
app.MapGet("/set-culture", (string culture, string? redirectUri, HttpContext http) =>
{
    if (!culture.Equals("es-ES", StringComparison.OrdinalIgnoreCase) &&
        !culture.Equals("en", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest();
    }

    http.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(
            new RequestCulture(new CultureInfo(culture))),
        new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,
            Path = "/"
        });

    // La ruta de vuelta tiene que ser local, o esto sería una redirección
    // abierta. Se comprueba antes en lugar de dejar que LocalRedirect lance:
    // así una petición mal formada responde 400 y no un 500, que además
    // ensuciaría el registro de errores con algo que no es un fallo del sistema.
    var target = string.IsNullOrWhiteSpace(redirectUri) ? "/" : redirectUri;

    if (!IsLocalPath(target)) return Results.BadRequest();

    return Results.LocalRedirect(target);
});

// Descarga del paquete de documentación. Va como endpoint y no por interop con
// JavaScript porque así el navegador gestiona la descarga como cualquier otra y
// el fichero no tiene que pasar por el circuito de SignalR.
//
// El endpoint no construye el documento: solo traduce HTTP a una consulta y la
// respuesta a un fichero. Generar el informe es trabajo de la capa de
// aplicación, y así el mismo documento saldría igual desde una API o una CLI.
// El script de ejemplo, para quien quiera contrastar el análisis con la
// entrada. Es un endpoint y no un fichero estático porque así comparte la
// resolución de ruta con el botón que lo analiza: se descarga exactamente lo
// que se ha analizado, no una copia que podría divergir.
app.MapGet("/samples/{fileName}", (string fileName) =>
    {
        // El nombre llega de la petición, así que no se sanea: se compara con la
        // lista de ejemplos conocidos y se rechaza si no está. Un
        // «../../appsettings.json» no coincide con ninguno y no hay ruta que
        // construir.
        var sample = SampleScript.Find(fileName);
        if (sample is null) return Results.NotFound();

        return File.Exists(sample.FullPath)
            ? Results.File(sample.FullPath, "text/plain; charset=utf-8", sample.FileName)
            : Results.NotFound();
    })
    .RequireAuthorization();

app.MapGet("/analyses/{id:guid}/markdown", async (
        Guid id,
        ClaimsPrincipal user,
        ISender sender,
        CancellationToken ct) =>
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var export = await sender.Send(new ExportAnalysisMarkdownQuery(id, userId), ct);

        return export is null
            ? Results.NotFound()
            : Results.File(
                System.Text.Encoding.UTF8.GetBytes(export.Content),
                "text/markdown; charset=utf-8",
                export.FileName);
    })
    .RequireAuthorization();

await DemoDataSeeder.MigrateAndSeedAsync(
    app.Services,
    app.Configuration,
    app.Services.GetRequiredService<ILogger<Program>>());

app.Run();

/// <summary>
/// Ruta local: empieza por una sola barra. Se rechaza «//» y «/\» porque el
/// navegador los interpreta como host, y serían una redirección fuera del sitio
/// disfrazada de ruta relativa.
/// </summary>
static bool IsLocalPath(string path) =>
    path.StartsWith('/') && !path.StartsWith("//") && !path.StartsWith("/\\");
