using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Administration;
using Platform.Api.Modules.Kt1;
using Microsoft.Extensions.DependencyInjection;

namespace Platform.Kt1.Tests;

[TestFixture]
public class StoreAndContractTests
{
    private static DemoStore Seed() => new(NullLogger<DemoStore>.Instance);
    [Test] public void RepeatedSeedHasSameOwnershipAndAdministrativeBoundaries()
    {
        var a = Seed(); var b = Seed();
        Assert.That(a.FindAttempt(DemoStore.DraftAttempt), Is.EqualTo(b.FindAttempt(DemoStore.DraftAttempt)));
        Assert.That(a.FindAuthority(DemoStore.Admin, DemoStore.Course).Value!.IsRoot, Is.True);
        Assert.That(a.FindAuthority(DemoStore.Admin, DemoStore.OtherCourse).Status, Is.EqualTo(PortStatus.NotFound));
    }
    [Test] public void ConcurrentDraftWritesWithSameVersionHaveExactlyOneWinner()
    {
        var store = Seed(); var snapshot = store.FindAttempt(DemoStore.DraftAttempt).Value!;
        var results = new System.Collections.Concurrent.ConcurrentBag<WriteStatus>();
        Parallel.For(0, 16, i => results.Add(store.UpdateDraft(snapshot, 1, $"answer {i}").Status));
        Assert.That(results.Count(s => s == WriteStatus.Updated), Is.EqualTo(1));
        Assert.That(results.Count(s => s == WriteStatus.Conflict), Is.EqualTo(15));
        Assert.That(store.FindAttempt(snapshot.Id).Value!.Version, Is.EqualTo(2));
        Assert.That(store.ReadAnswer(snapshot).Status, Is.EqualTo(PortStatus.NotFound));
    }
    [Test] public void RevocationRejectsPreviouslyCapturedAuthorityAtCommitBoundary()
    {
        var store = Seed(); var root = store.FindAuthority(DemoStore.Admin, DemoStore.Course).Value!;
        var delegated = (GrantView)store.Delegate(root, DemoStore.DelegateUser,
            AdminCapability.ViewAdministration | AdminCapability.ManageSettings).Value!;
        var captured = store.FindAuthority(DemoStore.DelegateUser, DemoStore.Course).Value!;
        Assert.That(store.Revoke(root, delegated.Id, delegated.Version).Status, Is.EqualTo(WriteStatus.Updated));
        Assert.That(store.Rename(captured, new("stale authority", 1)).Status, Is.EqualTo(WriteStatus.Denied));
        Assert.That(store.ReadAdministration(captured).Status, Is.EqualTo(PortStatus.NotFound));
    }
    [Test] public void AdministrativeGrantDoesNotCreateTeachingMembership()
    {
        var store = Seed(); var root = store.FindAuthority(DemoStore.Admin, DemoStore.Course).Value!;
        store.Delegate(root, DemoStore.DelegateUser, AdminCapability.ViewAdministration);
        Assert.That(store.FindMembership(DemoStore.DelegateUser, DemoStore.Course).Status, Is.EqualTo(PortStatus.NotFound));
        Assert.That(store.FindAttempt(DemoStore.AnaAttempt).Value!.OwnerId, Is.EqualTo(DemoStore.Ana));
    }
    [Test] public async Task SharedDefaultDenyContractHandlesUnknownActionAndNoSubject()
    {
        using var factory = new Kt1Factory();
        var engine = factory.Services.GetRequiredService<IAccessDecision>();
        var unknown = await engine.DecideAsync(new(new(DemoStore.Ana.ToString(), AccountState.Active), "attempt.delete", new("attempt", DemoStore.AnaAttempt.ToString()), "contract-unknown"));
        Assert.That(unknown.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(unknown.DenialReason, Is.EqualTo(AccessDenialReason.UnknownAction));
        var anonymous = await engine.DecideAsync(new(null, AttemptPolicy.Read, new("attempt", DemoStore.AnaAttempt.ToString()), "contract-anonymous"));
        Assert.That(anonymous.DenialReason, Is.EqualTo(AccessDenialReason.Unauthenticated));
    }
    [Test] public void ProviderContractSupportsFoundMissingAndUnavailableWithoutAnswerInMetadata()
    {
        IAttemptStore port = Seed();
        Assert.That(port.FindAttempt(DemoStore.AnaAttempt).Status, Is.EqualTo(PortStatus.Ok));
        Assert.That(port.FindAttempt(DemoStore.MissingAttempt).Status, Is.EqualTo(PortStatus.NotFound));
        ((DemoStore)port).AttemptsUnavailable = true;
        Assert.That(port.FindAttempt(DemoStore.AnaAttempt).Status, Is.EqualTo(PortStatus.Unavailable));
        Assert.That(typeof(AttemptSnapshot).GetProperty("Answer"), Is.Null);
    }
    [TestCase("attempt.read", "course")] [TestCase("course.admin.read", "attempt")]
    public async Task ResourceTypeCannotBeConfusedAcrossPolicies(string action, string type)
    {
        using var factory = new Kt1Factory();
        var decision = await factory.Services.GetRequiredService<IAccessDecision>().DecideAsync(new(
            new(DemoStore.Admin.ToString(), AccountState.Active), action, new(type, DemoStore.Course.ToString()), "wrong-type"));
        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }
}
