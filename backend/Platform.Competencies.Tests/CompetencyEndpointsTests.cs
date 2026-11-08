using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Platform.Api.Endpoints;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Competencies.Contracts;
using Platform.Competencies.Domain;

namespace Platform.Competencies.Tests;

[TestFixture]
public sealed class CompetencyEndpointsTests
{
    private static readonly Competency AccessControl = new(
        BuiltInCompetencies.AccessControlId,
        "Kontrola pristupa",
        "Opis");

    private static readonly Competency LogAnalysis = new(
        BuiltInCompetencies.LogAnalysisId,
        "Analiza bezbednosnih logova");

    [Test]
    public async Task EveryOperation_RequiresAnIdentity_BeforeItUsesStorage()
    {
        var catalog = new RecordingCatalog(AccessControl, LogAnalysis);
        var store = new RecordingStore(AccessControl, LogAnalysis);
        using var services = Services(catalog, store);
        var context = Context(services);
        var anonymous = new FixedIdentityContext(null);
        Guid laboratoryId = Guid.NewGuid();

        IResult[] results =
        [
            await CompetencyEndpoints.ListCatalogAsync(context, anonymous, CancellationToken.None),
            await CompetencyEndpoints.ListLaboratoryCompetenciesAsync(laboratoryId, context, anonymous, CancellationToken.None),
            await CompetencyEndpoints.AddLaboratoryCompetencyAsync(
                laboratoryId, AccessControl.Id, context, anonymous, CancellationToken.None),
            await CompetencyEndpoints.RemoveLaboratoryCompetencyAsync(
                laboratoryId, AccessControl.Id, context, anonymous, CancellationToken.None),
        ];

        Assert.That(results.Select(Status), Is.All.EqualTo(StatusCodes.Status401Unauthorized));
        Assert.That(catalog.ListCalls, Is.Zero);
        Assert.That(store.ListCalls, Is.Zero);
        Assert.That(store.AddCalls, Is.Zero);
        Assert.That(store.RemoveCalls, Is.Zero);
    }

    [Test]
    public async Task AuthenticatedCaller_CanReadCatalogAndLinks_ForAnArbitraryLaboratoryId()
    {
        var catalog = new RecordingCatalog(AccessControl, LogAnalysis);
        var store = new RecordingStore(AccessControl, LogAnalysis);
        Guid arbitraryLaboratoryId = Guid.NewGuid();
        store.Seed(arbitraryLaboratoryId, AccessControl.Id);
        using var services = Services(catalog, store);
        var context = Context(services);
        var caller = AuthenticatedIdentity();

        var catalogResult = await CompetencyEndpoints.ListCatalogAsync(context, caller, CancellationToken.None);
        var linksResult = await CompetencyEndpoints.ListLaboratoryCompetenciesAsync(
            arbitraryLaboratoryId, context, caller, CancellationToken.None);

        Assert.That(Status(catalogResult), Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(Body<CompetencyCatalogResponse>(catalogResult).Competencies.Select(x => x.Id),
            Is.EquivalentTo(new[] { AccessControl.Id, LogAnalysis.Id }));
        Assert.That(Status(linksResult), Is.EqualTo(StatusCodes.Status200OK));
        var links = Body<LaboratoryCompetenciesResponse>(linksResult);
        Assert.That(links.LaboratoryId, Is.EqualTo(arbitraryLaboratoryId));
        Assert.That(links.Competencies.Select(x => x.Id), Is.EqualTo(new[] { AccessControl.Id }));
        Assert.That(store.LastListedLaboratoryId, Is.EqualTo(arbitraryLaboratoryId));
    }

    [Test]
    public async Task AuthenticatedCaller_CanAddAndRemoveALink_WithoutResourceAuthorization()
    {
        var catalog = new RecordingCatalog(AccessControl, LogAnalysis);
        var store = new RecordingStore(AccessControl, LogAnalysis);
        using var services = Services(catalog, store);
        var context = Context(services);
        var caller = AuthenticatedIdentity();
        Guid arbitraryLaboratoryId = Guid.NewGuid();

        var firstAdd = await CompetencyEndpoints.AddLaboratoryCompetencyAsync(
            arbitraryLaboratoryId, AccessControl.Id, context, caller, CancellationToken.None);
        var repeatedAdd = await CompetencyEndpoints.AddLaboratoryCompetencyAsync(
            arbitraryLaboratoryId, AccessControl.Id, context, caller, CancellationToken.None);
        var remove = await CompetencyEndpoints.RemoveLaboratoryCompetencyAsync(
            arbitraryLaboratoryId, AccessControl.Id, context, caller, CancellationToken.None);

        Assert.That(Status(firstAdd), Is.EqualTo(StatusCodes.Status201Created));
        Assert.That(Status(repeatedAdd), Is.EqualTo(StatusCodes.Status204NoContent));
        Assert.That(Status(remove), Is.EqualTo(StatusCodes.Status204NoContent));
        Assert.That(store.AddCalls, Is.EqualTo(2));
        Assert.That(store.RemoveCalls, Is.EqualTo(1));
        Assert.That(store.HasLink(arbitraryLaboratoryId, AccessControl.Id), Is.False);
    }

    [Test]
    public async Task InvalidOrUnknownIdentifiers_AreSafeHttpErrors()
    {
        var catalog = new RecordingCatalog(AccessControl, LogAnalysis);
        var store = new RecordingStore(AccessControl, LogAnalysis);
        using var services = Services(catalog, store);
        var context = Context(services);
        var caller = AuthenticatedIdentity();

        var emptyLaboratory = await CompetencyEndpoints.AddLaboratoryCompetencyAsync(
            Guid.Empty, AccessControl.Id, context, caller, CancellationToken.None);
        var emptyCompetency = await CompetencyEndpoints.AddLaboratoryCompetencyAsync(
            Guid.NewGuid(), Guid.Empty, context, caller, CancellationToken.None);
        var unknownCompetency = await CompetencyEndpoints.AddLaboratoryCompetencyAsync(
            Guid.NewGuid(), Guid.NewGuid(), context, caller, CancellationToken.None);

        Assert.That(Status(emptyLaboratory), Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(Body<CompetencyErrorResponse>(emptyLaboratory).Error, Is.EqualTo("invalid_laboratory_id"));
        Assert.That(Status(emptyCompetency), Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(Body<CompetencyErrorResponse>(emptyCompetency).Error, Is.EqualTo("invalid_competency_id"));
        Assert.That(Status(unknownCompetency), Is.EqualTo(StatusCodes.Status404NotFound));
        Assert.That(Body<CompetencyErrorResponse>(unknownCompetency).Error, Is.EqualTo("competency_not_found"));
        Assert.That(store.AddCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task AuthenticatedCaller_Gets503WhenCompetencyPersistenceIsNotConfigured()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var caller = AuthenticatedIdentity();

        var result = await CompetencyEndpoints.ListCatalogAsync(
            Context(services), caller, CancellationToken.None);

        Assert.That(Status(result), Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        Assert.That(Body<CompetencyErrorResponse>(result).Error, Is.EqualTo("competency_storage_unavailable"));
    }

    [Test]
    public async Task EmptySessionUserId_IsNotTreatedAsAuthenticated()
    {
        var catalog = new RecordingCatalog(AccessControl);
        using var services = Services(catalog, new RecordingStore(AccessControl));

        var result = await CompetencyEndpoints.ListCatalogAsync(
            Context(services), new FixedIdentityContext(new CallerIdentity(
                true, Guid.Empty, AuthenticationLevel.Active, Guid.NewGuid())), CancellationToken.None);

        Assert.That(Status(result), Is.EqualTo(StatusCodes.Status401Unauthorized));
        Assert.That(catalog.ListCalls, Is.Zero);
    }

    private static ServiceProvider Services(ICompetencyCatalog catalog, ILaboratoryCompetencyStore store)
    {
        var services = new ServiceCollection();
        services.AddSingleton(catalog);
        services.AddSingleton(store);
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext Context(IServiceProvider services) => new()
    {
        RequestServices = services,
    };

    private static FixedIdentityContext AuthenticatedIdentity() => new(new CallerIdentity(
        true, Guid.NewGuid(), AuthenticationLevel.Active, Guid.NewGuid()));

    private static int Status(IResult result) =>
        ((IStatusCodeHttpResult)result).StatusCode
        ?? throw new AssertionException("The endpoint result has no HTTP status code.");

    private static T Body<T>(IResult result) where T : class =>
        (T)((IValueHttpResult)result).Value!;

    private sealed class FixedIdentityContext(CallerIdentity? current) : IIdentityContext
    {
        public CallerIdentity? Current { get; } = current;
    }

    private sealed class RecordingCatalog(params Competency[] competencies) : ICompetencyCatalog
    {
        private readonly IReadOnlyList<Competency> _competencies = competencies;

        public int ListCalls { get; private set; }

        public Task<IReadOnlyList<Competency>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return Task.FromResult(_competencies);
        }
    }

    private sealed class RecordingStore(params Competency[] competencies) : ILaboratoryCompetencyStore
    {
        private readonly IReadOnlyDictionary<Guid, Competency> _catalog = competencies.ToDictionary(x => x.Id);
        private readonly HashSet<(Guid LaboratoryId, Guid CompetencyId)> _links = [];

        public int AddCalls { get; private set; }
        public int RemoveCalls { get; private set; }
        public int ListCalls { get; private set; }
        public Guid? LastListedLaboratoryId { get; private set; }

        public void Seed(Guid laboratoryId, Guid competencyId) => _links.Add((laboratoryId, competencyId));

        public bool HasLink(Guid laboratoryId, Guid competencyId) => _links.Contains((laboratoryId, competencyId));

        public Task<bool> AddAsync(Guid laboratoryId, Guid competencyId, CancellationToken cancellationToken = default)
        {
            AddCalls++;
            if (!_catalog.ContainsKey(competencyId))
                throw new KeyNotFoundException();

            return Task.FromResult(_links.Add((laboratoryId, competencyId)));
        }

        public Task<bool> RemoveAsync(Guid laboratoryId, Guid competencyId, CancellationToken cancellationToken = default)
        {
            RemoveCalls++;
            return Task.FromResult(_links.Remove((laboratoryId, competencyId)));
        }

        public Task<IReadOnlyList<Competency>> GetForLaboratoryAsync(
            Guid laboratoryId,
            CancellationToken cancellationToken = default)
        {
            ListCalls++;
            LastListedLaboratoryId = laboratoryId;
            IReadOnlyList<Competency> result = _links
                .Where(x => x.LaboratoryId == laboratoryId)
                .Select(x => _catalog[x.CompetencyId])
                .OrderBy(x => x.Name)
                .ToList();
            return Task.FromResult(result);
        }
    }
}
