using LegacyLens.Application.Knowledge;
using LegacyLens.Domain;

namespace LegacyLens.Application.Tests;

/// <summary>
/// Las tres consultas que expone el servidor MCP. Se prueban aquí, en la capa de
/// aplicación, y no contra el servidor: ahí solo hay traducción de MCP a MediatR,
/// y lo que puede estar mal —la resolución de nombres, la separación de lecturas
/// y escrituras, el radio de impacto— vive en estos handlers.
/// </summary>
public class KnowledgeQueryTests
{
    private const string Owner = "usuario-1";
    private const string Intruder = "usuario-2";

    // Sistema de ejemplo. Tres objetos definidos en el script y tres nombres que
    // solo aparecen en el grafo, que es lo normal cuando el esquema está en otro
    // fichero:
    //
    //   Nocturno --calls--> Consolidar --reads---> Existencias
    //                                 --writes--> LogProceso
    //                                 --calls---> Registrar --writes--> Existencias
    //   Resumen  --reads--> Existencias   (por duplicado, con otra caja)
    private static AnalysisResult BuildAnalysis()
    {
        var analysis = new AnalysisResult { SourceFileName = "almacen.sql" };

        analysis.Objects.AddRange(
        [
            Object("usp_Consolidar", SqlObjectKind.Procedure, risk: 80, RiskLevel.Critical),
            Object("usp_Registrar", SqlObjectKind.Procedure, risk: 30, RiskLevel.Medium),
            Object("Existencias", SqlObjectKind.Table, risk: 0, RiskLevel.Low)
        ]);

        analysis.Dependencies.AddRange(
        [
            new Dependency("dbo.usp_Consolidar", "dbo.Existencias", DependencyKind.Reads),
            new Dependency("dbo.usp_Consolidar", "dbo.LogProceso", DependencyKind.Writes),
            new Dependency("dbo.usp_Consolidar", "dbo.usp_Registrar", DependencyKind.Calls),
            new Dependency("dbo.usp_Registrar", "dbo.Existencias", DependencyKind.Writes),
            new Dependency("dbo.vw_Resumen", "dbo.Existencias", DependencyKind.Reads),

            // El mismo uso con otra caja. El analizador puede emitirlo así cuando
            // el script escribe el nombre de dos maneras, y no son dos usos.
            new Dependency("DBO.VW_RESUMEN", "dbo.Existencias", DependencyKind.Reads),

            new Dependency("dbo.usp_Nocturno", "dbo.usp_Consolidar", DependencyKind.Calls)
        ]);

        return analysis;
    }

    private static SqlObject Object(string name, SqlObjectKind kind, int risk, RiskLevel level) =>
        new()
        {
            Name = name,
            Schema = "dbo",
            Kind = kind,
            Body = $"-- {name}",
            Risk = new RiskScore(risk, level, [])
        };

    private static (FakeAnalysisRepository Repository, Guid AnalysisId) Given()
    {
        var analysis = BuildAnalysis();
        var repository = new FakeAnalysisRepository();
        repository.Add(analysis, Owner);
        return (repository, analysis.Id);
    }

    // -----------------------------------------------------------------------
    // find_object
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Find_object_accepts_a_name_without_schema()
    {
        var (repository, id) = Given();

        var card = await new FindObjectHandler(repository)
            .Handle(new FindObjectQuery(id, Owner, "usp_Consolidar"), TestContext.Current.CancellationToken);

        Assert.NotNull(card);
        Assert.Equal("dbo.usp_Consolidar", card.FullName);
    }

    [Fact]
    public async Task Find_object_separates_what_it_reads_writes_and_calls()
    {
        var (repository, id) = Given();

        var card = await new FindObjectHandler(repository)
            .Handle(new FindObjectQuery(id, Owner, "dbo.usp_Consolidar"), TestContext.Current.CancellationToken);

        Assert.NotNull(card);
        Assert.Equal(["dbo.Existencias"], card.Reads.Select(d => d.To));
        Assert.Equal(["dbo.LogProceso"], card.Writes.Select(d => d.To));
        Assert.Equal(["dbo.usp_Registrar"], card.Calls.Select(d => d.To));
    }

    [Fact]
    public async Task Find_object_lists_who_calls_it()
    {
        var (repository, id) = Given();

        var card = await new FindObjectHandler(repository)
            .Handle(new FindObjectQuery(id, Owner, "usp_Consolidar"), TestContext.Current.CancellationToken);

        Assert.NotNull(card);
        Assert.Equal(["dbo.usp_Nocturno"], card.CalledBy);
    }

    [Fact]
    public async Task Find_object_returns_nothing_for_an_unknown_name()
    {
        var (repository, id) = Given();

        var card = await new FindObjectHandler(repository)
            .Handle(new FindObjectQuery(id, Owner, "dbo.NoExiste"), TestContext.Current.CancellationToken);

        Assert.Null(card);
    }

    /// <summary>
    /// La regla que sostiene todo el aislamiento entre usuarios. Si esto se
    /// rompe, el servidor MCP entrega los análisis de otra persona a quien tenga
    /// un identificador.
    /// </summary>
    [Fact]
    public async Task Find_object_does_not_serve_an_analysis_of_another_user()
    {
        var (repository, id) = Given();

        var card = await new FindObjectHandler(repository)
            .Handle(new FindObjectQuery(id, Intruder, "usp_Consolidar"), TestContext.Current.CancellationToken);

        Assert.Null(card);
    }

    [Fact]
    public async Task Find_object_asks_the_repository_for_the_requesting_user()
    {
        var (repository, id) = Given();

        await new FindObjectHandler(repository)
            .Handle(new FindObjectQuery(id, Owner, "usp_Consolidar"), TestContext.Current.CancellationToken);

        Assert.Equal([Owner], repository.RequestedOwners);
    }

    // -----------------------------------------------------------------------
    // where_used
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Where_used_reports_each_reader_and_writer_once()
    {
        var (repository, id) = Given();

        var usages = await new WhereUsedHandler(repository)
            .Handle(new WhereUsedQuery(id, Owner, "Existencias"), TestContext.Current.CancellationToken);

        Assert.NotNull(usages);
        Assert.Equal(3, usages.Count);
        Assert.Contains(usages, u => u.From == "dbo.usp_Consolidar" && u.Kind == DependencyKind.Reads);
        Assert.Contains(usages, u => u.From == "dbo.usp_Registrar" && u.Kind == DependencyKind.Writes);

        // Las dos aristas de vw_Resumen son el mismo uso escrito con otra caja.
        Assert.Single(usages, u => u.Kind == DependencyKind.Reads && u.From.EndsWith("Resumen", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Una tabla puede no estar definida en el script analizado —es habitual que
    /// el esquema viva en otro fichero— y aun así tener usos. Si esto falla, la
    /// pregunta más útil del servidor MCP responde vacío justo en el caso real.
    /// </summary>
    [Fact]
    public async Task Where_used_finds_a_table_that_the_script_only_references()
    {
        var (repository, id) = Given();

        var usages = await new WhereUsedHandler(repository)
            .Handle(new WhereUsedQuery(id, Owner, "LogProceso"), TestContext.Current.CancellationToken);

        Assert.NotNull(usages);
        Assert.Equal(["dbo.usp_Consolidar"], usages.Select(u => u.From));
        Assert.Equal(DependencyKind.Writes, usages[0].Kind);
    }

    /// <summary>
    /// Vacío y nulo significan cosas distintas y el servidor MCP las traduce
    /// distinto: «no lo usa nadie» frente a «ese análisis no es tuyo o no existe».
    /// </summary>
    [Fact]
    public async Task Where_used_answers_empty_for_an_unknown_name_and_null_for_a_missing_analysis()
    {
        var (repository, id) = Given();
        var handler = new WhereUsedHandler(repository);

        var unknownName = await handler
            .Handle(new WhereUsedQuery(id, Owner, "NoExiste"), TestContext.Current.CancellationToken);
        var missingAnalysis = await handler
            .Handle(new WhereUsedQuery(Guid.NewGuid(), Owner, "Existencias"), TestContext.Current.CancellationToken);

        Assert.Empty(unknownName!);
        Assert.Null(missingAnalysis);
    }

    // -----------------------------------------------------------------------
    // change_risk
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Change_risk_separates_direct_dependents_from_transitive_ones()
    {
        var (repository, id) = Given();

        var impact = await new ChangeRiskHandler(repository)
            .Handle(new ChangeRiskQuery(id, Owner, "Existencias"), TestContext.Current.CancellationToken);

        Assert.NotNull(impact);
        // Ordenados ignorando mayúsculas, y con una sola entrada por vw_Resumen
        // pese a las dos aristas: se conserva la primera forma que apareció.
        Assert.Equal(
            ["dbo.usp_Consolidar", "dbo.usp_Registrar", "dbo.vw_Resumen"],
            impact.DirectDependents);

        // Nocturno no toca la tabla, pero llama a quien la toca: se entera del
        // cambio igualmente.
        Assert.Contains("dbo.usp_Nocturno", impact.TransitiveDependents);
    }

    [Fact]
    public async Task Change_risk_lists_what_has_to_be_migrated_first()
    {
        var (repository, id) = Given();

        var impact = await new ChangeRiskHandler(repository)
            .Handle(new ChangeRiskQuery(id, Owner, "usp_Consolidar"), TestContext.Current.CancellationToken);

        Assert.NotNull(impact);
        Assert.Equal(["dbo.usp_Registrar"], impact.Blockers);
    }

    [Fact]
    public async Task Change_risk_carries_the_score_of_the_object_itself()
    {
        var (repository, id) = Given();

        var impact = await new ChangeRiskHandler(repository)
            .Handle(new ChangeRiskQuery(id, Owner, "usp_Consolidar"), TestContext.Current.CancellationToken);

        Assert.NotNull(impact);
        Assert.Equal(80, impact.Risk.Value);
        Assert.Equal(RiskLevel.Critical, impact.Risk.Level);
    }

    /// <summary>
    /// Documenta una diferencia real entre las dos consultas: <c>where_used</c>
    /// resuelve nombres contra el grafo y <c>change_risk</c> solo contra los
    /// objetos del script. Preguntar por el riesgo de cambiar una tabla que no
    /// está definida en el fichero devuelve nulo aunque tenga usos conocidos.
    ///
    /// No es lo que uno esperaría desde fuera, y por eso está escrito: si algún
    /// día se unifica, este test debe cambiar a propósito y no por accidente.
    /// </summary>
    [Fact]
    public async Task Change_risk_does_not_resolve_names_that_only_exist_in_the_graph()
    {
        var (repository, id) = Given();

        var impact = await new ChangeRiskHandler(repository)
            .Handle(new ChangeRiskQuery(id, Owner, "LogProceso"), TestContext.Current.CancellationToken);

        Assert.Null(impact);
    }

    [Fact]
    public async Task Change_risk_does_not_serve_an_analysis_of_another_user()
    {
        var (repository, id) = Given();

        var impact = await new ChangeRiskHandler(repository)
            .Handle(new ChangeRiskQuery(id, Intruder, "usp_Consolidar"), TestContext.Current.CancellationToken);

        Assert.Null(impact);
    }
}
