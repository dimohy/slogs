using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Slogs.Data;
using Microsoft.Extensions.Configuration;

if (args.Length == 3 && args[0] == "build-corpus")
{
    await AinCorpusPlan.BuildAsync(args[1], args[2]);
    return;
}
if (args.Length == 3 && args[0] is "ingest-corpus" or "status-corpus" or "verify-corpus")
{
    await AinCorpusPlan.IngestAsync(args[1], args[2], args[0]);
    return;
}

// Local operations console: its authority is the operator's protected server/DB access.
// Never expose this executable through HTTP, MCP, or a patient-facing process.
if (args.Length != 6 || args[0] is not ("plan" or "apply" or "reader-token" or "registration-token") || args.Skip(1).Any(string.IsNullOrWhiteSpace))
{
    Console.Error.WriteLine("Usage: Slogs.OrganizationAdmin plan|apply actor slug displayName owner environmentLabel");
    Environment.ExitCode = 2;
    return;
}
if (!System.Text.RegularExpressions.Regex.IsMatch(args[2], "^[a-z0-9](?:[a-z0-9-]{0,78}[a-z0-9])?$"))
    throw new InvalidOperationException("Invalid organization slug.");
var connection = Environment.GetEnvironmentVariable("ConnectionStrings__SlogsDatabase")
    ?? throw new InvalidOperationException("Production database configuration is required; no default is permitted.");
var services = new ServiceCollection();
services.AddDbContextFactory<SlogsDbContext>(o => o.UseNpgsql(connection));
services.AddDbContextFactory<OrganizationDbContext>(o => o.UseNpgsql(connection).UseOpenIddict());
services.AddScoped<OrganizationActorResolver>();
services.AddScoped<OrganizationDirectoryService>();
services.AddScoped<OrganizationTokenService>();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SlogsDbContext>>();
await using var users = await dbFactory.CreateDbContextAsync();
var actorRecord = await users.Users.AsNoTracking().Where(u => u.UserName == args[1])
    .Select(u => new {u.UserName, u.DisplayName}).SingleOrDefaultAsync()
    ?? throw new InvalidOperationException("Administrator account does not exist.");
var actor = new AuthUser {UserName = actorRecord.UserName, DisplayName = actorRecord.DisplayName};
if (!actor.IsAdmin) throw new InvalidOperationException("Actor is not an existing Slogs administrator.");
if (!await users.Users.AnyAsync(u => u.UserName == args[4]))
    throw new InvalidOperationException("Owner account does not exist.");
var directory = scope.ServiceProvider.GetRequiredService<OrganizationDirectoryService>();
var existing = (await directory.ListAllAsync(actor)).SingleOrDefault(o => o.Slug == args[2]);
var orgFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>();
await using var db = await orgFactory.CreateDbContextAsync();
if (existing is not null)
{
    if (existing.DisplayName != args[3] || existing.EnvironmentLabel != args[5]
        || !await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == existing.Id
            && m.UserName == args[4] && m.Role == OrganizationRoles.Owner && m.Status == OrganizationMemberStatuses.Active))
        throw new InvalidOperationException("Existing organization differs; refusing to modify or take ownership.");
}
if (args[0] == "plan")
{
    Console.WriteLine(JsonSerializer.Serialize(new {mode="plan", action=existing is null ? "create" : "reuse", slug=args[2], owner=args[4], organizationId=existing?.Id}));
    return;
}
if (args[0].EndsWith("-token"))
{
    if (existing is null) throw new InvalidOperationException("Create and verify the organization before issuing tokens.");
    var tokenService = scope.ServiceProvider.GetRequiredService<OrganizationTokenService>();
    var registration = args[0] == "registration-token";
    string[] scopes = registration
        ? [OrganizationTokenScopes.Read, OrganizationTokenScopes.Propose, OrganizationTokenScopes.Approve]
        : [OrganizationTokenScopes.Read];
    var token = await tokenService.CreateAsync(existing.Id,
        new($"{existing.Slug}-{args[0]}-{DateTime.UtcNow:yyyyMMddHHmmss}", scopes, DateTime.UtcNow.AddHours(registration ? 1 : 720)),
        SlogsAuthentication.CreatePrincipal(actor));
    // Sensitive stdout must be captured in memory by the trusted provisioning wrapper;
    // never run token modes interactively or redirect them to plaintext files/logs.
    Console.WriteLine(JsonSerializer.Serialize(new {token.Id, token.OrganizationId, token.Token, token.Scopes, token.ExpiresAt}));
    return;
}
var organization = existing ?? await directory.CreateAsync(new(args[2], args[3], args[4], args[5]), actor);
var ownerVerified = await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organization.Id
    && m.UserName == args[4] && m.Role == OrganizationRoles.Owner && m.Status == OrganizationMemberStatuses.Active);
var auditVerified = await db.OrganizationAudits.AnyAsync(a => a.OrganizationId == organization.Id
    && a.Action == "organization.create" && a.Outcome == "success");
if (!ownerVerified || !auditVerified) throw new InvalidOperationException("Post-registration verification failed.");
Console.WriteLine(JsonSerializer.Serialize(new {mode="apply", organizationId=organization.Id, slug=organization.Slug, owner=args[4], ownerVerified, auditVerified, created=existing is null}));
