using System.Reflection;
using NUnit.Framework;
using Platform.Audit.Contracts;

namespace Platform.Audit.Tests;

// T03: the catalog, its constants and its examples stay in sync.
[TestFixture]
public sealed class AuditEventCatalogTests
{
    [Test]
    public void EveryEventTypeConstant_HasExactlyOneSchema()
    {
        var constants = typeof(AuditEventTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.Name != nameof(AuditEventTypes.ContractVersion))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        Assert.That(AuditEventCatalog.Schemas.Keys, Is.EquivalentTo(constants));
    }

    [Test]
    public void ValidExamples_CoverEverySchema()
    {
        var covered = AuditDraftExamples.ValidPerEventType().Select(example => example.Draft.EventType);

        Assert.That(covered, Is.EquivalentTo(AuditEventCatalog.Schemas.Keys));
    }

    [Test]
    public void NoSchema_AllowsADetailKeyThatLooksLikeASecret()
    {
        var forbidden = new[] { "password", "passwd", "secret", "token", "authorization", "apikey", "cookie", "session" };

        var offending = AuditEventCatalog.Schemas.Values
            .SelectMany(schema => schema.Details.Keys)
            .Where(key => forbidden.Any(part => key.Contains(part, StringComparison.OrdinalIgnoreCase)));

        Assert.That(offending, Is.Empty);
    }

    [Test]
    public void EverySchema_AllowsAtLeastOneOutcomeAndActor()
    {
        foreach (var schema in AuditEventCatalog.Schemas.Values)
        {
            Assert.That(schema.ActorsByOutcome, Is.Not.Empty, schema.EventType);
            Assert.That(schema.ActorsByOutcome.Values, Has.All.Not.Empty, schema.EventType);
        }
    }

    [TestCase("auth.unknown")]
    [TestCase("AUTH.LOGIN")]
    [TestCase("")]
    [TestCase(null)]
    public void TryGet_WhenTypeIsNotInCatalog_ReturnsFalse(string? eventType)
    {
        Assert.That(AuditEventCatalog.TryGet(eventType, out _), Is.False);
    }
}
