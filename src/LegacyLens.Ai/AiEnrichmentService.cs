using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using LegacyLens.Application.Abstractions;
using LegacyLens.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyLens.Ai;

/// <summary>
/// Enriquece un análisis estático con la interpretación del modelo de lenguaje.
///
/// Es la única parte no determinista del sistema, y está deliberadamente
/// aislada: si falla o no está configurada, el análisis estático sigue siendo
/// válido y la aplicación sigue siendo útil. Esa separación es la que permite
/// que la capa de aplicación dependa de un interface y no de este código.
/// </summary>
public sealed class AiEnrichmentService : IAiEnrichmentService
{
    private readonly AiOptions _options;
    private readonly ILogger<AiEnrichmentService> _logger;
    private readonly AzureOpenAIClient? _client;

    /// <summary>
    /// Separador que no puede aparecer en un nombre de despliegue de Azure OpenAI.
    /// Sin él, un modelo llamado "a" con cuerpo "bc" y otro llamado "ab" con cuerpo
    /// "c" producirían la misma entrada al hash y compartirían entrada de caché.
    /// </summary>
    private const string CacheKeySeparator = "::";

    /// <summary>
    /// Caché por contenido: si el mismo objeto se vuelve a analizar, no se
    /// vuelve a pagar. Sobrevive entre análisis dentro del mismo proceso.
    /// </summary>
    private readonly ConcurrentDictionary<string, ObjectDocumentation> _cache = new();

    public AiEnrichmentService(
        IOptions<AiOptions> options,
        ILogger<AiEnrichmentService> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (_options.IsConfigured)
        {
            var endpoint = new Uri(_options.Endpoint!);

            _client = _options.UsesManagedIdentity
                ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential())
                : new AzureOpenAIClient(endpoint, new AzureKeyCredential(_options.ApiKey!));

            _logger.LogInformation(
                "Azure OpenAI configurado en {Endpoint} usando {Auth}",
                endpoint.Host,
                _options.UsesManagedIdentity ? "identidad de Azure" : "clave");
        }
        else
        {
            _logger.LogWarning(
                "Azure OpenAI no está configurado. El análisis estático funcionará; " +
                "la documentación y el plan de migración quedarán sin generar.");
        }
    }

    public bool IsAvailable => _client is not null;

    public async Task DocumentAllAsync(
        AnalysisResult result,
        IProgress<AiProgress>? progress = null,
        IModelUsageCollector? usage = null,
        CancellationToken cancellationToken = default)
    {
        if (_client is null) return;

        var targets = result.Objects.Where(o => o.IsProgrammable).ToList();
        if (targets.Count == 0) return;

        var chat = _client.GetChatClient(_options.DocumentationDeployment).AsIChatClient();
        var completed = 0;

        await Parallel.ForEachAsync(
            targets,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.MaxConcurrency,
                CancellationToken = cancellationToken
            },
            async (obj, token) =>
            {
                try
                {
                    obj.Documentation = await DocumentObjectAsync(chat, obj, result, usage, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Un objeto que falla no debe tumbar el análisis completo.
                    // Se queda sin documentar y la interfaz lo refleja.
                    _logger.LogError(ex, "No se pudo documentar {Objeto}", obj.FullName);
                }
                finally
                {
                    var done = Interlocked.Increment(ref completed);
                    progress?.Report(new AiProgress(done, targets.Count, obj.FullName));
                }
            });
    }

    private async Task<ObjectDocumentation> DocumentObjectAsync(
        IChatClient chat,
        SqlObject obj,
        AnalysisResult result,
        IModelUsageCollector? usage,
        CancellationToken cancellationToken)
    {
        var model = _options.DocumentationDeployment;
        var cacheKey = CacheKey(model, obj.Body);

        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            _logger.LogDebug("Documentación de {Objeto} servida desde caché", obj.FullName);
            return cached;
        }

        List<ChatMessage> messages =
        [
            new(ChatRole.System, Prompts.DocumentationSystem),
            new(ChatRole.User, Prompts.ForObject(obj, result, _options.MaxBodyCharacters))
        ];

        var response = await chat.GetResponseAsync<ObjectDocumentationDto>(
            messages,
            cancellationToken: cancellationToken);

        RecordUsage(response.Usage, model, usage);

        var dto = response.Result;
        var documentation = new ObjectDocumentation(
            Summary: dto.Summary,
            BusinessRules: dto.BusinessRules,
            SideEffects: dto.SideEffects,
            MigrationTarget: dto.MigrationTarget,
            ModelUsed: model);

        _cache[cacheKey] = documentation;
        return documentation;
    }

    public async Task<MigrationPlan?> BuildPlanAsync(
        AnalysisResult result,
        IModelUsageCollector? usage = null,
        CancellationToken cancellationToken = default)
    {
        if (_client is null) return null;

        var model = _options.PlanningDeployment;

        try
        {
            var chat = _client.GetChatClient(model).AsIChatClient();

            List<ChatMessage> messages =
            [
                new(ChatRole.System, Prompts.PlanningSystem),
                new(ChatRole.User, Prompts.ForPlan(result))
            ];

            var response = await chat.GetResponseAsync<MigrationPlanDto>(
                messages,
                cancellationToken: cancellationToken);

            RecordUsage(response.Usage, model, usage);

            var dto = response.Result;

            return new MigrationPlan(
                Overview: dto.Overview,
                Phases: dto.Phases
                    .Select((p, i) => new MigrationPhase(i + 1, p.Title, p.Rationale, p.Objects, p.Risk))
                    .ToList(),
                GlobalRisks: dto.GlobalRisks,
                ModelUsed: model);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "No se pudo generar el plan de migración");
            return null;
        }
    }

    private static void RecordUsage(UsageDetails? usage, string model, IModelUsageCollector? collector)
    {
        if (usage is null || collector is null) return;

        collector.Add(model, usage.InputTokenCount ?? 0, usage.OutputTokenCount ?? 0);
    }

    private static string CacheKey(string model, string body)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(model + CacheKeySeparator + body));
        return Convert.ToHexStringLower(hash);
    }

    // Tipos de transporte para la salida estructurada. Se mantienen separados
    // del dominio porque su forma la dicta el esquema JSON que espera el modelo.
    private sealed class ObjectDocumentationDto
    {
        public string Summary { get; set; } = string.Empty;
        public List<string> BusinessRules { get; set; } = [];
        public List<string> SideEffects { get; set; } = [];
        public string MigrationTarget { get; set; } = string.Empty;
    }

    private sealed class MigrationPlanDto
    {
        public string Overview { get; set; } = string.Empty;
        public List<MigrationPhaseDto> Phases { get; set; } = [];
        public List<string> GlobalRisks { get; set; } = [];
    }

    private sealed class MigrationPhaseDto
    {
        public string Title { get; set; } = string.Empty;
        public string Rationale { get; set; } = string.Empty;
        public List<string> Objects { get; set; } = [];
        public string Risk { get; set; } = string.Empty;
    }
}
