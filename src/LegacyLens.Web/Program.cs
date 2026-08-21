using LegacyLens.Ai;
using LegacyLens.Analysis;
using LegacyLens.Web.Components;
using LegacyLens.Web.Components.Account;
using LegacyLens.Web.Data;
using LegacyLens.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Falta la cadena de conexión 'DefaultConnection'.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// Contexto separado para los análisis: ver AnalysisDbContext.
builder.Services.AddDbContext<AnalysisDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("AnalysisConnection")
        ?? "Data Source=analyses.db"));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// ---------------------------------------------------------------------------
// Legacy Lens
// ---------------------------------------------------------------------------

builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));

// El analizador no tiene estado: una instancia basta para todo el proceso.
builder.Services.AddSingleton<TSqlAnalyzer>();

// Singleton para que la caché de documentación y el recuento de tokens
// sobrevivan entre análisis.
builder.Services.AddSingleton<AiUsage>();
builder.Services.AddSingleton<AiEnrichmentService>();

builder.Services.AddSingleton<CostEstimator>();

builder.Services.AddScoped<AnalysisStore>();
builder.Services.AddScoped<AnalysisWorkflow>();

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
app.MapGet("/analisis/{id:guid}/markdown", async (
        Guid id,
        ClaimsPrincipal user,
        AnalysisStore store,
        CancellationToken ct) =>
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var result = await store.GetAsync(id, userId, ct);

        if (result is null) return Results.NotFound();

        var markdown = MarkdownExporter.Export(result);
        var fileName = $"{Path.GetFileNameWithoutExtension(result.SourceFileName)}-legacy-lens.md";

        return Results.File(
            System.Text.Encoding.UTF8.GetBytes(markdown),
            "text/markdown; charset=utf-8",
            fileName);
    })
    .RequireAuthorization();

await DemoDataSeeder.SeedAsync(app.Services, app.Configuration,
    app.Services.GetRequiredService<ILogger<Program>>());

app.Run();
