using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace Laboratories.Tests;

[TestFixture]
public sealed class LaboratoriesHttpTests
{
    private const string Route = "/api/laboratories/";
    private const string TestUserHeader = "X-Test-User";

    [TestCase("GET", null, HttpStatusCode.Unauthorized)]
    [TestCase("POST", null, HttpStatusCode.Unauthorized)]
    [TestCase("GET", "ana:Student", HttpStatusCode.OK)]
    [TestCase("POST", "ana:Student", HttpStatusCode.Forbidden)]
    [TestCase("POST", "marko:Teacher", HttpStatusCode.NotImplemented)]
    [TestCase("GET", "ana:Unknown", HttpStatusCode.Unauthorized)]
    [TestCase("GET", "invalid", HttpStatusCode.Unauthorized)]
    public async Task Development_EnforcesAccessBeforeEndpoint(
        string method, string? testUser, HttpStatusCode expectedStatus)
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        using var request = CreateRequest(method, testUser);

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
        if (expectedStatus == HttpStatusCode.OK)
        {
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("[]"));
        }
    }

    [Test]
    public async Task Development_WithMultipleIdentityHeaders_Denies()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        using var request = CreateRequest("POST", null);
        request.Headers.Add(TestUserHeader, new[] { "ana:Student", "marko:Teacher" });

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Development_IdentityDoesNotLeakBetweenRequests()
    {
        using var factory = new LaboratoriesHostFactory("Development");
        using var client = factory.CreateClient();
        using var teacherRequest = CreateRequest("POST", "marko:Teacher");
        using var teacherResponse = await client.SendAsync(teacherRequest);
        Assert.That(teacherResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotImplemented));

        using var studentRequest = CreateRequest("POST", "ana:Student");
        using var studentResponse = await client.SendAsync(studentRequest);
        Assert.That(studentResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        using var anonymousRequest = CreateRequest("POST", null);
        using var anonymousResponse = await client.SendAsync(anonymousRequest);
        Assert.That(anonymousResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase("Production", "GET")]
    [TestCase("Production", "POST")]
    [TestCase("Staging", "GET")]
    [TestCase("Staging", "POST")]
    public async Task OutsideDevelopment_TestTeacherHeaderDoesNotGrantAccess(
        string environment, string method)
    {
        using var factory = new LaboratoriesHostFactory(environment);
        using var client = factory.CreateClient();
        using var request = CreateRequest(method, "marko:Teacher");

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private static HttpRequestMessage CreateRequest(string method, string? testUser)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), Route);
        if (testUser is not null)
        {
            request.Headers.Add(TestUserHeader, testUser);
        }

        return request;
    }

    private sealed class LaboratoriesHostFactory(string environment) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }
}
