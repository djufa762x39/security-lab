using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Platform.Api.Modules.Administration;
using Platform.Api.Modules.Kt1;

namespace Platform.Kt1.Tests;

[TestFixture]
public class RevocationSessionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task RevocationRejectsTheSamePreviouslyIssuedCookie(bool write)
    {
        using var factory = new Kt1Factory();
        using var admin = factory.CreateClient();
        // Disable automatic cookies so the exact pre-revocation cookie is explicitly reused.
        using var delegated = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        admin.DefaultRequestHeaders.Add("X-Kt1-Demo", "1");
        delegated.DefaultRequestHeaders.Add("X-Kt1-Demo", "1");
        Assert.That((await admin.PostAsync("/api/kt1/session/admin", null)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var course = $"/api/kt1/courses/{DemoStore.Course}";
        var created = await admin.PostAsJsonAsync(course + "/delegations",
            new DelegateRequest(DemoStore.DelegateUser, ["ViewAdministration", "ManageSettings"]));
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var grant = (await created.Content.ReadFromJsonAsync<GrantView>())!;
        var signedIn = await delegated.PostAsync("/api/kt1/session/delegate", null);
        Assert.That(signedIn.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var issuedCookie = signedIn.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith("SecurityLab.CourseworkKt1=", StringComparison.Ordinal)).Split(';', 2)[0];
        delegated.DefaultRequestHeaders.Add("Cookie", issuedCookie);
        Assert.That((await delegated.GetAsync(course + "/administration")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var store = factory.Services.GetRequiredService<DemoStore>();
        var before = store.FindCourse(DemoStore.Course).Value!;
        Assert.That((await admin.PostAsJsonAsync(course + $"/delegations/{grant.Id}/revoke", new RevokeRequest(grant.Version))).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
        // No fresh sign-in and no automatic Set-Cookie processing can change the delegated session.
        var denied = write
            ? await delegated.PutAsJsonAsync(course + "/settings", new RenameCourseRequest("must not be stored", before.Version))
            : await delegated.GetAsync(course + "/administration");
        Assert.That(denied.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await denied.Content.ReadAsStringAsync(), Is.EqualTo("{\"code\":\"RESOURCE_UNAVAILABLE\"}"));
        Assert.That(store.FindCourse(DemoStore.Course).Value, Is.EqualTo(before));
        Assert.That(store.FindAuthority(DemoStore.DelegateUser, DemoStore.Course).Value, Is.Null);
        Assert.That(store.Events.Last().ActorId, Is.EqualTo(DemoStore.DelegateUser));
    }
}
