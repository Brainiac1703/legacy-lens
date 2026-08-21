using LegacyLens.Ai;
using LegacyLens.Analysis;
using LegacyLens.Application;
using LegacyLens.Application.Analyses;
using LegacyLens.Persistence.EF;
using LegacyLens.Persistence.EF.Entities;
using LegacyLens.Web.Components;
using LegacyLens.Web.Components.Account;
using MediatR;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
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

// Descarga del paquete de documentación. Va como endpoint y no por interop con
// JavaScript porque así el navegador gestiona la descarga como cualquier otra y
// el fichero no tiene que pasar por el circuito de SignalR.
//
// El endpoint no construye el documento: solo traduce HTTP a una consulta y la
// respuesta a un fichero. Generar el informe es trabajo de la capa de
// aplicación, y así el mismo documento saldría igual desde una API o una CLI.
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
