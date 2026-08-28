using LegacyLens.Application.Knowledge;

namespace LegacyLens.Application.Tests;

/// <summary>
/// Los validadores son la primera barrera de las consultas que expone el
/// servidor MCP, y ahí los argumentos llegan de un modelo de lenguaje: cadenas
/// vacías, nombres larguísimos o identificadores inventados no son un caso raro,
/// son el caso esperable.
/// </summary>
public class KnowledgeValidatorTests
{
    private static readonly Guid Analysis = Guid.NewGuid();
    private const string Owner = "usuario-1";

    [Fact]
    public void Find_object_requires_an_analysis_an_owner_and_a_name()
    {
        var validator = new FindObjectValidator();

        Assert.False(validator.Validate(new FindObjectQuery(Guid.Empty, Owner, "x")).IsValid);
        Assert.False(validator.Validate(new FindObjectQuery(Analysis, "", "x")).IsValid);
        Assert.False(validator.Validate(new FindObjectQuery(Analysis, Owner, "")).IsValid);
        Assert.True(validator.Validate(new FindObjectQuery(Analysis, Owner, "dbo.Facturas")).IsValid);
    }

    /// <summary>
    /// El tope de longitud existe porque el nombre llega de fuera y acaba en una
    /// comparación contra cada objeto del análisis. Un identificador de SQL
    /// Server no pasa de 128 caracteres, así que 256 ya es holgado.
    /// </summary>
    [Fact]
    public void A_name_longer_than_any_real_identifier_is_rejected()
    {
        var validator = new FindObjectValidator();
        var tooLong = new string('a', 257);

        Assert.False(validator.Validate(new FindObjectQuery(Analysis, Owner, tooLong)).IsValid);
        Assert.True(validator.Validate(new FindObjectQuery(Analysis, Owner, new string('a', 256))).IsValid);
    }

    [Fact]
    public void Where_used_requires_an_analysis_an_owner_and_a_name()
    {
        var validator = new WhereUsedValidator();

        Assert.False(validator.Validate(new WhereUsedQuery(Guid.Empty, Owner, "x")).IsValid);
        Assert.False(validator.Validate(new WhereUsedQuery(Analysis, "", "x")).IsValid);
        Assert.False(validator.Validate(new WhereUsedQuery(Analysis, Owner, "   ")).IsValid);
        Assert.True(validator.Validate(new WhereUsedQuery(Analysis, Owner, "Existencias")).IsValid);
    }

    [Fact]
    public void Change_risk_requires_an_analysis_an_owner_and_a_name()
    {
        var validator = new ChangeRiskValidator();

        Assert.False(validator.Validate(new ChangeRiskQuery(Guid.Empty, Owner, "x")).IsValid);
        Assert.False(validator.Validate(new ChangeRiskQuery(Analysis, "", "x")).IsValid);
        Assert.False(validator.Validate(new ChangeRiskQuery(Analysis, Owner, "")).IsValid);
        Assert.True(validator.Validate(new ChangeRiskQuery(Analysis, Owner, "dbo.Existencias")).IsValid);
    }
}
