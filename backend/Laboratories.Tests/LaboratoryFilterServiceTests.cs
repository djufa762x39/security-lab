using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Modules.Laboratories.Infrastructure;
using Platform.LabMetadata;
using Platform.LaboratoryPersistence.Application;

namespace Laboratories.Tests;

[TestFixture]
public sealed class LaboratoryFilterServiceTests
{
    private static readonly Caller Teacher = new("marko", PlatformRole.Teacher);
    private static readonly Caller OtherTeacher = new("jelena", PlatformRole.Teacher);
    private InMemoryLaboratoryRepository _repository = null!;
    private MetadataStore _metadata = null!;
    private LaboratorySearchService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new();
        _metadata = new();
        _service = new(_repository, new FakeAccessDecision(),
            NullLogger<LaboratorySearchService>.Instance, _metadata);
    }

    [Test]
    public async Task Search_CombinesFiltersAndRemovingSelectionsBroadensResults()
    {
        Laboratory first = await Add("Lab A", LaboratoryDifficulty.Easy, LaboratoryType.LogAnalysis, ["security", "access"]);
        Laboratory second = await Add("Lab B", LaboratoryDifficulty.Easy, LaboratoryType.LogAnalysis, ["security"]);
        Laboratory third = await Add("Lab C", LaboratoryDifficulty.Easy, LaboratoryType.AccessControl, ["security"]);
        Laboratory fourth = await Add("Lab D", LaboratoryDifficulty.Hard, LaboratoryType.LogAnalysis, ["security", "access"]);
        Laboratory missing = await Add("Lab without metadata");
        Laboratory hidden = await Add("Lab hidden", LaboratoryDifficulty.Easy, LaboratoryType.LogAnalysis, ["security", "access"], OtherTeacher);
        var filters = new LabSearchFilters(LaboratoryDifficulty.Easy, LaboratoryType.LogAnalysis, ["security", "access"]);

        LabSearchResult combined = await Search("lab", filters);
        LabSearchResult oneTag = await Search("lab", filters with { Tags = ["security"] });
        LabSearchResult noType = await Search("lab", filters with { Type = null, Tags = ["security"] });
        LabSearchResult reset = await Search(null, new());

        Assert.Multiple(() =>
        {
            Assert.That(combined.Items.Select(l => l.Id), Is.EqualTo(new[] { first.Id }));
            Assert.That(oneTag.Items.Select(l => l.Id), Is.EquivalentTo(new[] { first.Id, second.Id }));
            Assert.That(noType.Items.Select(l => l.Id), Is.EquivalentTo(new[] { first.Id, second.Id, third.Id }));
            Assert.That(reset.Items.Select(l => l.Id), Is.EquivalentTo(new[] { first.Id, second.Id, third.Id, fourth.Id, missing.Id }));
            Assert.That(_metadata.Reads, Does.Not.Contain(hidden.Id));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task Search_EmptyTitleAllowsFiltersOnlyAndReset(string? query)
    {
        Laboratory easy = await Add("First", LaboratoryDifficulty.Easy);
        Laboratory hard = await Add("Second", LaboratoryDifficulty.Hard);

        LabSearchResult filtered = await Search(query, new(LaboratoryDifficulty.Easy));
        LabSearchResult reset = await Search(query, new());

        Assert.Multiple(() =>
        {
            Assert.That(filtered.Status, Is.EqualTo(LabSearchStatus.Ok));
            Assert.That(filtered.Items.Select(l => l.Id), Is.EqualTo(new[] { easy.Id }));
            Assert.That(reset.Items.Select(l => l.Id), Is.EquivalentTo(new[] { easy.Id, hard.Id }));
        });
    }

    [Test]
    public async Task Search_TagsAreTrimmedCaseInsensitiveExactMatchesAndAllAreRequired()
    {
        Laboratory match = await Add("Lab exact", LaboratoryDifficulty.Easy, tags: ["security", "access"]);
        await Add("Lab partial", LaboratoryDifficulty.Easy, tags: ["security-tools", "access"]);
        await Add("Lab one tag", LaboratoryDifficulty.Easy, tags: ["security"]);

        LabSearchResult result = await Search(null, new(Tags: [" SECURITY ", "Access", "security"]));

        Assert.That(result.Items.Select(l => l.Id), Is.EqualTo(new[] { match.Id }));
    }

    [Test]
    public async Task Search_FilteringPrecedesResultLimitAndTruncation()
    {
        for (int i = 0; i < LaboratorySearchService.MaxResults + 10; i++)
            await Add($"Lab {i:D3}", LaboratoryDifficulty.Hard);
        Laboratory selected = await Add("Lab Z selected", LaboratoryDifficulty.Easy);

        LabSearchResult result = await Search("lab", new(LaboratoryDifficulty.Easy));

        Assert.Multiple(() =>
        {
            Assert.That(result.Items.Select(l => l.Id), Is.EqualTo(new[] { selected.Id }));
            Assert.That(result.Truncated, Is.False);
        });
    }

    [Test]
    public async Task Options_OnlyReadAndExposeMetadataOfVisibleLaboratories()
    {
        Laboratory own = await Add("Own", LaboratoryDifficulty.Easy, tags: ["visible", "shared"]);
        Laboratory hidden = await Add("Hidden", LaboratoryDifficulty.Easy, tags: ["secret", "shared"], owner: OtherTeacher);
        var options = await _service.GetFilterOptionsAsync(Teacher, CancellationToken.None);
        var studentOptions = await _service.GetFilterOptionsAsync(new Caller("marko", PlatformRole.Student), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(options.Difficulties, Is.EqualTo(Enum.GetNames<LaboratoryDifficulty>()));
            Assert.That(options.Types, Is.EqualTo(Enum.GetNames<LaboratoryType>()));
            Assert.That(options.Tags, Is.EqualTo(new[] { "shared", "visible" }));
            Assert.That(options.MetadataAvailable, Is.True);
            Assert.That(studentOptions.Tags, Is.Empty);
            Assert.That(_metadata.Reads, Is.EqualTo(new[] { own.Id }));
            Assert.That(_metadata.Reads, Does.Not.Contain(hidden.Id));
        });
    }

    [Test]
    public async Task MissingStore_DoesNotDisableTitleSearchOrSilentlyIgnoreFilters()
    {
        Laboratory lab = await Add("Lab", LaboratoryDifficulty.Easy);
        var service = new LaboratorySearchService(_repository, new FakeAccessDecision(), NullLogger<LaboratorySearchService>.Instance);

        LabSearchResult title = await service.SearchAsync("lab", Teacher, CancellationToken.None);
        LabSearchResult filtered = await service.SearchAsync(null, Teacher, CancellationToken.None, new(LaboratoryDifficulty.Easy));
        LabSearchFilterOptions options = await service.GetFilterOptionsAsync(Teacher, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(title.Items.Select(l => l.Id), Is.EqualTo(new[] { lab.Id }));
            Assert.That(filtered.Status, Is.EqualTo(LabSearchStatus.FiltersUnavailable));
            Assert.That(filtered.Items, Is.Empty);
            Assert.That(options.MetadataAvailable, Is.False);
            Assert.That(options.Tags, Is.Empty);
        });
    }

    [Test]
    public async Task Search_InvalidFiltersAreRejectedBeforeMetadataReads()
    {
        LabSearchFilters[] invalid =
        [
            new((LaboratoryDifficulty)999), new(Type: (LaboratoryType)999),
            new(Tags: [""]), new(Tags: ["bad\nvalue"]),
        ];
        foreach (var filters in invalid)
            Assert.That((await Search(null, filters)).Status, Is.EqualTo(LabSearchStatus.InvalidQuery));
        Assert.That(_metadata.Reads, Is.Empty);
    }

    [Test]
    public async Task Search_UsesExistingTagsWithoutIntroducingMetadataLengthOrCountRestrictions()
    {
        string[] tags = Enumerable.Range(0, 21).Select(i => new string('a', 201) + i).ToArray();
        Laboratory laboratory = await Add("Lab", LaboratoryDifficulty.Easy, tags: tags);

        LabSearchResult result = await Search(null, new(Tags: tags));
        LabSearchFilterOptions options = await _service.GetFilterOptionsAsync(Teacher, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items.Select(lab => lab.Id), Is.EqualTo(new[] { laboratory.Id }));
            Assert.That(options.Tags, Is.EquivalentTo(tags));
        });
    }

    [Test]
    public async Task Search_StoreFailureReturnsNoPartialResultsAndDoesNotReadHiddenMetadata()
    {
        await Add("First", LaboratoryDifficulty.Easy);
        Laboratory failing = await Add("Second", LaboratoryDifficulty.Easy);
        _metadata.FailOn = failing.Id;

        Assert.ThrowsAsync<InvalidOperationException>(() => Search(null, new(LaboratoryDifficulty.Easy)));
        Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetFilterOptionsAsync(Teacher, CancellationToken.None));
    }

    [Test]
    public async Task Search_AnonymousCallerCannotReadMetadataOrOptions()
    {
        await Add("Lab", LaboratoryDifficulty.Easy);
        var caller = new AnonymousIdentityContext();
        var result = await _service.SearchAsync(null, caller, CancellationToken.None, new(LaboratoryDifficulty.Easy));
        var options = await _service.GetFilterOptionsAsync(caller, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(LabSearchStatus.Unauthenticated));
            Assert.That(options.Status, Is.EqualTo(LabSearchStatus.Unauthenticated));
            Assert.That(_metadata.Reads, Is.Empty);
        });
    }

    private Task<LabSearchResult> Search(string? query, LabSearchFilters filters) =>
        _service.SearchAsync(query, Teacher, CancellationToken.None, filters);

    private async Task<Laboratory> Add(string title, LaboratoryDifficulty? difficulty = null,
        LaboratoryType type = LaboratoryType.LogAnalysis, string[]? tags = null, Caller? owner = null)
    {
        var lab = new Laboratory(title, CallerId.From(owner ?? Teacher)!.Value);
        await _repository.AddAsync(lab);
        if (difficulty is not null)
            _metadata.Items[lab.Id] = new(lab.Id, "Existing LAB-02 objective", difficulty.Value, type, tags ?? []);
        return lab;
    }

    private sealed record Caller(string? UserId, PlatformRole? Role) : IIdentityContext
    {
        public bool IsAuthenticated => true;
    }

    private sealed class MetadataStore : ILaboratoryMetadataStore
    {
        public Dictionary<Guid, LaboratoryMetadata> Items { get; } = [];
        public List<Guid> Reads { get; } = [];
        public Guid? FailOn { get; set; }

        public Task<LaboratoryMetadata?> GetAsync(Guid laboratoryId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Reads.Add(laboratoryId);
            if (FailOn == laboratoryId) throw new InvalidOperationException("Metadata unavailable.");
            return Task.FromResult(Items.GetValueOrDefault(laboratoryId));
        }

        public Task SaveAsync(LaboratoryMetadata metadata, CancellationToken ct = default) =>
            throw new NotSupportedException("LAB-05 must never write metadata.");
    }
}
