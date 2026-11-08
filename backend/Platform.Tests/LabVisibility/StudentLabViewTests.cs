using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Persistence;

namespace Platform.Tests.LabVisibility;

public sealed class StudentLabViewTests
{
    private const string StudentId = "22222222-2222-2222-2222-222222222222";
    private const string OtherUserId = "33333333-3333-3333-3333-333333333333";

    private static readonly Guid CourseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly Guid TeacherId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid PublishedLabId = Guid.NewGuid();
    private static readonly Guid DraftLabId = Guid.NewGuid();
    private static readonly Guid MissingLabId = Guid.NewGuid();

    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;

    [SetUp]
    public void SetUp()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development"));

        client = factory.CreateClient();

        var reader = factory.Services.GetRequiredService<InMemoryLabPublicContentReader>();

        SaveLab(reader, PublishedLabId, LabVisibilityState.Published);
        SaveLab(reader, DraftLabId, LabVisibilityState.Draft);
    }

    [TearDown]
    public void TearDown()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Test]
    public async Task EnrolledStudent_GetsOnlyPublicFields()
    {
        using var response = await SendAsync(PublishedLabId, StudentId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var view = json.RootElement;
        var tasks = view.GetProperty("tasks");
        Assert.That(tasks.GetArrayLength(), Is.EqualTo(1));

        var task = tasks[0];

        Assert.Multiple(() =>
        {
            Assert.That(view.GetProperty("labId").GetGuid(), Is.EqualTo(PublishedLabId));
            Assert.That(view.GetProperty("title").GetString(), Is.EqualTo("Test laboratorija"));
            Assert.That(task.GetProperty("prompt").GetString(), Is.EqualTo("Izaberi opciju."));

            Assert.That(
                view.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(new[] { "labId", "title", "goal", "tasks" }));

            Assert.That(
                task.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(new[] { "taskId", "order", "prompt", "options" }));
        });
    }

    [Test]
    public async Task UserOutsideCourse_GetsNotFound()
    {
        using var response = await SendAsync(PublishedLabId, OtherUserId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task EnrolledStudent_RequestingDraft_GetsNotFound()
    {
        using var response = await SendAsync(DraftLabId, StudentId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task EnrolledStudent_RequestingMissingLab_GetsNotFound()
    {
        using var response = await SendAsync(MissingLabId, StudentId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task RequestWithoutIdentity_GetsUnauthorized()
    {
        using var response = await SendAsync(PublishedLabId, null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private async Task<HttpResponseMessage> SendAsync(Guid labId, string? userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/labs/{labId:D}/student-view");

        if (userId is not null)
        {
            request.Headers.Add("X-Stub-User", userId);
        }

        return await client.SendAsync(request);
    }

    private static void SaveLab(InMemoryLabPublicContentReader reader, Guid labId, LabVisibilityState state)
    {
        var task = new PublicTask("task-1", 1, "Izaberi opciju.", new[] { "A", "B" });

        reader.Save(LabPublicContent.Create(labId, CourseId, TeacherId, state, "Test laboratorija", "Provera studentskog prikaza", new[] { task }));
    }
}