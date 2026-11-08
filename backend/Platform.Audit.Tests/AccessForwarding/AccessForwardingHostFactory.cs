// TEST ADAPTERS ONLY (S-13). The probe route below exists only when a test factory adds this
// assembly as an application part; Platform.Api never maps it.
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Access.AspNet;
using Platform.Audit.Composition;
using Platform.Audit.Infrastructure.Persistence;

namespace Platform.Audit.Tests.AccessForwarding;

/// <summary>
/// Real Platform.Api host (Testing environment, ValidateScopes/ValidateOnBuild from Program.cs)
/// with the S-13 forwarder added explicitly through <see cref="AccessAuditForwardingExtensions"/>.
/// Program.cs itself is not changed: wiring there waits for decision D2.
/// </summary>
internal sealed class AccessForwardingHostFactory(string auditConnectionString) : WebApplicationFactory<Program>
{
    internal const string ProbeRoute = "/__test/access-forwarding/courses/{courseId}";

    public CapturingLoggerProvider Logs { get; } = new();

    internal static string ProbePath(Guid courseId) =>
        ProbeRoute.Replace("{courseId}", courseId.ToString("D"), StringComparison.Ordinal);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(Logs);
        });
        builder.ConfigureTestServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(AccessForwardingProbeController).Assembly);
            services.AddDbContext<AuditDbContext>(options => options.UseNpgsql(auditConnectionString));
            services.AddAccessAuditForwarding();
        });
    }
}

// Test-only endpoint protected by the real Access middleware with the central audit.events.read
// action on Course/{courseId}. The body is never reached by an anonymous caller.
[ApiController]
public sealed class AccessForwardingProbeController : ControllerBase
{
    [RequireAccess("audit.events.read", ResourceType = "course", IdRouteParam = "courseId")]
    [HttpGet(AccessForwardingHostFactory.ProbeRoute)]
    public IActionResult Get(Guid courseId) => Ok();
}
