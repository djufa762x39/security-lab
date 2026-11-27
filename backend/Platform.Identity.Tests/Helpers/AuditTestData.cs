using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.Audit.Composition;
using Platform.Audit.Infrastructure.Persistence;
using Platform.Identity.Tests.Integration;

namespace Platform.Identity.Tests.Helpers;

/// <summary>
/// Real Audit persistence on the shared disposable database. Only this helper uses the database owner;
/// the HTTP host writes through a restricted runtime role, including when INSERT is revoked by a test.
/// </summary>
internal static class AuditTestData
{
    private const string RuntimeRole = "identity_audit_test_runtime";
    // Stable for this test process: repeated setup must not invalidate an earlier host's connection string.
    private static readonly string RuntimePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static async Task<string> CreateRestrictedRuntimeAsync()
    {
        await AuditPersistenceExtensions.MigrateAuditDatabaseAsync(TestDatabaseFixture.ConnectionString);
        // The password is generated hexadecimal text and the role name is a fixed test-only identifier.
        await ExecuteAsync($"""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = '{RuntimeRole}') THEN
                    CREATE ROLE {RuntimeRole};
                END IF;
            END;
            $$;
            ALTER ROLE {RuntimeRole} WITH LOGIN PASSWORD '{RuntimePassword}'
                NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS;
            GRANT USAGE ON SCHEMA audit TO {RuntimeRole};
            GRANT SELECT, INSERT ON audit.audit_events TO {RuntimeRole};
            """);
        return new NpgsqlConnectionStringBuilder(TestDatabaseFixture.ConnectionString)
        {
            Username = RuntimeRole,
            Password = RuntimePassword,
            Pooling = false
        }.ConnectionString;
    }

    public static Task SetInsertAllowedAsync(bool allowed) => ExecuteAsync(allowed
        ? $"GRANT INSERT ON audit.audit_events TO {RuntimeRole}"
        : $"REVOKE INSERT ON audit.audit_events FROM {RuntimeRole}");

    public static async Task<List<AuditEventRow>> GetByCorrelationAsync(
        WebApplicationFactory<Program> factory, string correlation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await db.Events.AsNoTracking().Where(row => row.CorrelationId == correlation).ToListAsync();
    }

    public static async Task<List<AuditEventRow>> GetByActorAsync(
        WebApplicationFactory<Program> factory, Guid actorId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await db.Events.AsNoTracking().Where(row => row.ActorId == actorId).ToListAsync();
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(TestDatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
