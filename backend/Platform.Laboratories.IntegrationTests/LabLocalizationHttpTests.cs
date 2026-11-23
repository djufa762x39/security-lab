using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Modules.Laboratories.Infrastructure;
using Platform.Api.Security.RevisionAuthorization;
using Platform.Api.Services.Identity;
using Platform.Audit.Composition;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;
using Platform.Laboratories.Domain.Models;
using Platform.Laboratories.Infrastructure.Composition;
using Platform.Laboratories.Infrastructure.Persistence;
using Platform.LaboratoryPersistence.Composition;
using Platform.LaboratoryPersistence.Infrastructure.Persistence;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Membership;
using Testcontainers.PostgreSql;

namespace Platform.Laboratories.IntegrationTests;

/// <summary>
/// LAB-07 (П): obe jezicke varijante prikazuju istu reviziju i isti evaluator.
/// Revizije nastaju i objavljuju se kroz REV rute nad stvarnim PostgreSQL-om; prava proverava IAccessDecision.
/// </summary>
[TestFixture, Category("Integration"), Category("LAB-07"), NonParallelizable]
public sealed class LabLocalizationHttpTests
{
    private const string Evaluator = "event-set-v1";

    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("lab07_http").WithUsername("lab07_tests").WithPassword(Guid.NewGuid().ToString("N")).Build();
    private WebApplicationFactory<Program> host = null!;
    private Guid teacher, otherTeacher, student, course, lab;
    private HttpClient teacherClient = null!, otherTeacherClient = null!, studentClient = null!, outsiderClient = null!;

    [OneTimeSetUp]
    public async Task Start()
    {
        await database.StartAsync();
        host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Identity"] = database.GetConnectionString(),
                    ["ConnectionStrings:Laboratories"] = database.GetConnectionString(),
                    ["ConnectionStrings:Authoring"] = database.GetConnectionString()
                }));
            // Isti razlog kao u RevisionHttpWorkflowTests: minimalni host cita neke connection string-ove
            // pre odlozene test konfiguracije, pa se stvarni adapteri vezuju za izolovanu bazu.
            builder.ConfigureTestServices(services =>
            {
                var connection = database.GetConnectionString();
                services.AddDbContext<PlatformIdentityDbContext>(options => options.UseNpgsql(connection,
                    postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "identity")));
                services.AddLaboratoriesPersistence(connection);
                services.AddRevisionSecurityPersistence(connection);
                services.AddAuditPersistence(connection);
                services.AddLaboratoryPersistence(new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?> { ["ConnectionStrings:Authoring"] = connection }).Build());
                services.AddScoped<ILaboratoryRepository, PostgresLaboratoryRepository>();
            });
        });
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<LaboratoryDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<LaboratoriesDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<RevisionSecurityDbContext>().Database.MigrateAsync();
        await AuditPersistenceExtensions.MigrateAuditDatabaseAsync(database.GetConnectionString());
    }

    [SetUp]
    public async Task Seed()
    {
        teacher = Guid.NewGuid(); otherTeacher = Guid.NewGuid(); student = Guid.NewGuid(); course = Guid.NewGuid();
        var outsider = Guid.NewGuid();
        using var scope = host.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        foreach (var user in new[] { teacher, otherTeacher, student, outsider })
            identity.Accounts.Add(new UserAccount(user, $"{user:N}@example.test"));
        teacherClient = Client(AddSession(identity, teacher));
        otherTeacherClient = Client(AddSession(identity, otherTeacher));
        studentClient = Client(AddSession(identity, student));
        outsiderClient = Client(AddSession(identity, outsider));
        await identity.SaveChangesAsync();

        var membership = host.Services.GetRequiredService<InMemoryCourseMembership>();
        membership.Set(course, teacher, CourseRole.Teacher);
        membership.Set(course, otherTeacher, CourseRole.Teacher);
        membership.Set(course, student, CourseRole.Student);

        lab = await AddLaboratory();
        var revisions = scope.ServiceProvider.GetRequiredService<LaboratoriesDbContext>();
        // Kurs dozvoljava da autor sam objavi; nezavisno odobrenje pokrivaju REV-05 testovi.
        revisions.CoursePublishPolicies.Add(((CoursePublishPolicy.CreationResult.Success)
            CoursePublishPolicy.Create(course, false)).Value);
        await revisions.SaveChangesAsync();
        var security = scope.ServiceProvider.GetRequiredService<RevisionSecurityDbContext>();
        foreach (var action in new[] { RevisionActions.Create, RevisionActions.Read, RevisionActions.Submit, RevisionActions.Approve })
            security.Permissions.Add(new RevisionCoursePermission
                { UserId = teacher, CourseId = course, Action = action, IsActive = true });
        await security.SaveChangesAsync();
    }

    [TearDown]
    public void CloseClients()
    {
        teacherClient.Dispose(); otherTeacherClient.Dispose(); studentClient.Dispose(); outsiderClient.Dispose();
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        if (host is not null) await host.DisposeAsync();
        await database.DisposeAsync();
    }

    // ------------------------------------------------------------------ citanje

    [Test]
    public async Task SerbianAndEnglish_ShowTheSameRevisionAndEvaluator()
    {
        var revision = await PublishRevision();
        await AddTranslation(revision, "sr", "Analiza logova");
        await AddTranslation(revision, "en", "Log analysis");

        var serbian = await ReadContent(studentClient, "?lang=sr");
        var english = await ReadContent(studentClient, "?lang=en");

        Assert.Multiple(() =>
        {
            Assert.That(serbian.RevisionId, Is.EqualTo(revision));
            Assert.That(english.RevisionId, Is.EqualTo(serbian.RevisionId));
            Assert.That(serbian.EvaluatorVersion, Is.EqualTo(Evaluator));
            Assert.That(english.EvaluatorVersion, Is.EqualTo(serbian.EvaluatorVersion));
            Assert.That((serbian.Language, serbian.Title), Is.EqualTo(("sr", "Analiza logova")));
            Assert.That((english.Language, english.Title), Is.EqualTo(("en", "Log analysis")));
        });
    }

    [TestCase("?lang=en")]
    [TestCase("?lang=")]
    [TestCase("")]
    public async Task MissingTranslationOrLanguage_ReturnsSerbianTextOfTheSameRevision(string query)
    {
        var revision = await PublishRevision();
        await AddTranslation(revision, "sr", "Analiza logova");

        var content = await ReadContent(studentClient, query);

        Assert.Multiple(() =>
        {
            Assert.That(content.RevisionId, Is.EqualTo(revision));
            Assert.That(content.EvaluatorVersion, Is.EqualTo(Evaluator));
            Assert.That((content.Language, content.Title), Is.EqualTo(("sr", "Analiza logova")));
        });
    }

    [TestCase("?lang=xx")]
    [TestCase("?lang=1")]
    [TestCase("?lang=sr,en")]
    [TestCase("?lang=sr&lang=en")]
    public async Task InvalidLanguage_Returns400(string query)
    {
        var revision = await PublishRevision();
        await AddTranslation(revision, "sr", "Analiza logova");

        using var response = await studentClient.GetAsync(ContentUrl(lab) + query);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await ErrorCode(response), Is.EqualTo("INVALID_LANGUAGE"));
    }

    [Test]
    public async Task NewPublishedRevision_ReplacesTheOldOneInBothLanguages()
    {
        var first = await PublishRevision();
        await AddTranslation(first, "sr", "Prva verzija");
        await AddTranslation(first, "en", "First version");
        var second = await PublishRevision();
        await AddTranslation(second, "sr", "Druga verzija");
        await AddTranslation(second, "en", "Second version");

        var serbian = await ReadContent(studentClient, "?lang=sr");
        var english = await ReadContent(studentClient, "?lang=en");

        Assert.Multiple(() =>
        {
            Assert.That(serbian.RevisionId, Is.EqualTo(second));
            Assert.That(english.RevisionId, Is.EqualTo(second));
            Assert.That(serbian.Title, Is.EqualTo("Druga verzija"));
            Assert.That(english.Title, Is.EqualTo("Second version"));
        });
    }

    [Test]
    public async Task DraftTranslation_IsNotVisibleUntilTheRevisionIsPublished()
    {
        var (draft, hash) = await CreateDraft();
        await AddTranslation(draft, "sr", "Tekst nacrta");
        using (var before = await studentClient.GetAsync(ContentUrl(lab)))
        {
            Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(await before.Content.ReadAsStringAsync(), Does.Not.Contain("Tekst nacrta"));
        }

        await Publish(draft, hash);

        Assert.That((await ReadContent(studentClient, "")).RevisionId, Is.EqualTo(draft));
    }

    [Test]
    public async Task NewDraft_DoesNotReplaceThePublishedRevision()
    {
        var published = await PublishRevision();
        await AddTranslation(published, "sr", "Objavljeno");
        var (draft, _) = await CreateDraft();
        await AddTranslation(draft, "sr", "Tekst nacrta");

        var content = await ReadContent(studentClient, "?lang=sr");

        Assert.That((content.RevisionId, content.Title), Is.EqualTo((published, "Objavljeno")));
    }

    [Test]
    public async Task NonexistentLaboratory_Returns404()
    {
        using var response = await studentClient.GetAsync(ContentUrl(Guid.NewGuid()) + "?lang=sr");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await ErrorCode(response), Is.EqualTo("NOT_FOUND"));
    }

    [Test]
    public async Task ReadWithoutRightToTheLaboratory_IsIndistinguishableFromNonexistentLaboratory()
    {
        var revision = await PublishRevision();
        await AddTranslation(revision, "sr", "Analiza logova");
        using var nonexistent = await outsiderClient.GetAsync(ContentUrl(Guid.NewGuid()));
        var expected = await nonexistent.Content.ReadAsStringAsync();

        foreach (var client in new[] { outsiderClient, otherTeacherClient })
        {
            // Neispravan jezik ne sme da otkrije postojanje laboratorije.
            foreach (var query in new[] { "", "?lang=xx" })
            {
                using var response = await client.GetAsync(ContentUrl(lab) + query);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(expected));
            }
        }
    }

    [Test]
    public async Task LaboratoryAuthor_ReadsPublishedContent()
    {
        var revision = await PublishRevision();
        await AddTranslation(revision, "sr", "Analiza logova");

        Assert.That((await ReadContent(teacherClient, "?lang=sr")).RevisionId, Is.EqualTo(revision));
    }

    // ------------------------------------------------------------------ dodavanje prevoda

    [Test]
    public async Task StudentAddsTranslation_Returns403_StoresNothingAndIsAudited()
    {
        var revision = await PublishRevision();
        await AddTranslation(revision, "sr", "Analiza logova");

        using var response = await PostTranslation(studentClient, lab, revision, "en", "Student text");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await ReadContent(studentClient, "?lang=en")).Language, Is.EqualTo("sr"));
        var correlation = response.Headers.GetValues("X-Correlation-ID").Single();
        Assert.That(await Denials(student, correlation), Is.EqualTo(new[] { ("lab.localization.translate", "policy_denied") }));
    }

    [Test]
    public async Task TeacherWhoIsNotAuthorOrMember_CannotAddTranslation()
    {
        var revision = await PublishRevision();

        foreach (var client in new[] { otherTeacherClient, outsiderClient })
        {
            using var response = await PostTranslation(client, lab, revision, "sr", "Analiza logova");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }
    }

    [Test]
    public async Task NonexistentRevision_CannotBeTranslated()
    {
        // Isti odgovor kao za tudju reviziju, da se ID-jevi ne mogu pogadjati.
        using var response = await PostTranslation(teacherClient, lab, Guid.NewGuid(), "sr", "Analiza logova");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task RevisionOfAnotherLaboratory_Returns404()
    {
        var revision = await PublishRevision();
        var otherLab = await AddLaboratory();

        foreach (var target in new[] { otherLab, Guid.NewGuid() })
        {
            using var response = await PostTranslation(teacherClient, target, revision, "sr", "Analiza logova");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
    }

    [Test]
    public async Task ExistingTranslation_IsNotOverwritten()
    {
        var revision = await PublishRevision();
        await AddTranslation(revision, "sr", "Analiza logova");

        using var response = await PostTranslation(teacherClient, lab, revision, "sr", "Novi naslov");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await ReadContent(studentClient, "?lang=sr")).Title, Is.EqualTo("Analiza logova"));
    }

    [TestCase("xx", "Naslov", "INVALID_LANGUAGE")]
    [TestCase(null, "Naslov", "INVALID_LANGUAGE")]
    [TestCase("sr", "", "INVALID_TITLE")]
    public async Task InvalidTranslation_Returns400(string? language, string title, string code)
    {
        var revision = await PublishRevision();

        using var response = await PostTranslation(teacherClient, lab, revision, language, title);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await ErrorCode(response), Is.EqualTo(code));
    }

    [Test]
    public async Task RequestWithoutSession_Returns401()
    {
        var revision = await PublishRevision();
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var read = await client.GetAsync(ContentUrl(lab));
        using var write = await PostTranslation(client, lab, revision, "sr", "Analiza logova");

        Assert.Multiple(() =>
        {
            Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(write.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        });
    }

    // ------------------------------------------------------------------ pomocno

    private sealed record Content(Guid RevisionId, string EvaluatorVersion, string Language, string Title);

    private static string ContentUrl(Guid laboratoryId) => $"/api/laboratories/{laboratoryId}/content";

    private async Task<Content> ReadContent(HttpClient client, string query)
    {
        using var response = await client.GetAsync(ContentUrl(lab) + query);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Content>())!;
    }

    private static Task<HttpResponseMessage> PostTranslation(HttpClient client, Guid laboratoryId, Guid revision,
        string? language, string title) =>
        client.PostAsJsonAsync($"/api/laboratories/{laboratoryId}/revisions/{revision}/translations",
            new { language, title, description = "Opis", instructions = "Uputstvo" });

    private async Task AddTranslation(Guid revision, string language, string title)
    {
        using var response = await PostTranslation(teacherClient, lab, revision, language, title);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private async Task<Guid> AddLaboratory()
    {
        using var scope = host.Services.CreateScope();
        var laboratory = new Laboratory(course, "Localized laboratory", teacher);
        await scope.ServiceProvider.GetRequiredService<ILaboratoryRepository>().AddAsync(laboratory);
        return laboratory.Id;
    }

    private async Task<Guid> PublishRevision()
    {
        var (id, hash) = await CreateDraft();
        await Publish(id, hash);
        return id;
    }

    private async Task Publish(Guid id, string hash)
    {
        await Command(id, hash, "submit", 1);
        await Command(id, hash, "approve", 2);
    }

    private async Task<(Guid Id, string Hash)> CreateDraft()
    {
        using var response = await teacherClient.PostAsJsonAsync("/api/revisions/", new
        {
            laboratoryId = lab, courseId = course,
            definition = new { type = "LogAnalysis", title = "Localized lab", goal = "Find failed logins", question = "Which event failed?",
                events = new[] { new { id = "event-1", timestamp = DateTimeOffset.UnixEpoch, message = "Failed login" } },
                evaluator = new { kind = "event-set", version = 1, expectedEventIds = new[] { "event-1" } } }
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), body.GetProperty("contentHash").GetString()!);
    }

    private async Task Command(Guid id, string hash, string action, int version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/revisions/{id}/{action}")
            { Content = JsonContent.Create(new { rowVersion = version.ToString(), contentHash = hash, comment = (string?)null }) };
        request.Headers.Add("Idempotency-Key", $"{action}-{id:N}");
        using var response = await teacherClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
    }

    private async Task<List<(string Action, string Reason)>> Denials(Guid actor, string correlation)
    {
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var sql = new NpgsqlCommand("""
            SELECT details->>'action', details->>'denialReason'
            FROM audit.audit_events
            WHERE event_type = 'access.denied' AND actor_id = @actor AND correlation_id = @correlation
            """, connection);
        sql.Parameters.AddWithValue("actor", actor);
        sql.Parameters.AddWithValue("correlation", correlation);
        await using var reader = await sql.ExecuteReaderAsync();
        var denials = new List<(string, string)>();
        while (await reader.ReadAsync()) denials.Add((reader.GetString(0), reader.GetString(1)));
        return denials;
    }

    private HttpClient Client(string token)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={token}");
        return client;
    }

    private static string AddSession(PlatformIdentityDbContext db, Guid user)
    {
        var token = Guid.NewGuid().ToString("N");
        db.Sessions.Add(new Session(Guid.NewGuid(), user, SHA256.HashData(Encoding.UTF8.GetBytes(token)),
            Guid.NewGuid(), AuthLevel.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));
        return token;
    }
}
