using System.Net;
using System.Net.Http.Json;
using DDT.Contracts.Authentication;
using DDT.Server.Authentication;
using DDT.Server.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DDT.Server.Tests;

// The real host on an in-memory server, with its own store directory and SQLite database, so tests
// exercise the actual pipeline: authentication schemes, the fallback policy and the CSRF filters.
public sealed class DdtApplication : WebApplicationFactory<Program>
{
    private readonly string _store = Path.Combine(Path.GetTempPath(), "ddt-server-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SemaphoreSlim _administratorLock = new(1, 1);
    private SignedInClient? _administrator;

    public const string Password = "Correct horse battery staple 42";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");
        // UseSetting rather than ConfigureAppConfiguration: Program reads these before it builds the host.
        builder.UseSetting("DDT:Roles", "web");
        builder.UseSetting("DDT:StorePath", _store);
        builder.UseSetting("DDT:RequireHttps", "false");
        builder.UseSetting("ConnectionStrings:ddtdb", string.Empty);
    }

    // Scope validation is on only in Development by default, and tests run as Testing.
    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseDefaultServiceProvider(provider =>
        {
            provider.ValidateScopes = true;
            provider.ValidateOnBuild = true;
        });

        return base.CreateHost(builder);
    }

    // Sign in is rate limited per address, and every test client shares one, so the administrator
    // session is reused across a test class.
    public async Task<SignedInClient> AdministratorAsync()
    {
        await _administratorLock.WaitAsync();

        try
        {
            return _administrator ??= await SignInAsync(DdtRoleNames.Administrator);
        }
        finally
        {
            _administratorLock.Release();
        }
    }

    public async Task<SignedInClient> SignInAsync(string role)
    {
        string userName = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}";

        using (IServiceScope scope = Services.CreateScope())
        {
            UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
            DdtUser user = new() { UserName = userName, CreatedUtc = DateTimeOffset.UtcNow };

            IdentityResult created = await users.CreateAsync(user, Password);

            if (!created.Succeeded || !(await users.AddToRoleAsync(user, role)).Succeeded)
            {
                throw new InvalidOperationException(string.Join(", ", created.Errors.Select(e => e.Description)));
            }
        }

        CookieContainer cookies = new();
        HttpClient client = CreateDefaultClient(new CookieContainerHandler(cookies));
        SignedInClient signedIn = new(client, cookies);

        HttpResponseMessage login = await signedIn.PostAsync("/api/auth/login", new LoginRequest(userName, Password, null, null));
        LoginResponse? result = await login.Content.ReadFromJsonAsync<LoginResponse>(TestJson.Options);

        if (result?.Status != LoginStatus.Succeeded)
        {
            throw new InvalidOperationException($"Sign in as {role} failed: {login.StatusCode} {result?.Status}.");
        }

        return signedIn;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _administrator?.Dispose();
            _administratorLock.Dispose();
        }

        if (disposing && Directory.Exists(_store))
        {
            // Pooled SQLite connections keep the database file open after the host stops.
            SqliteConnection.ClearAllPools();
            Directory.Delete(_store, recursive: true);
        }
    }
}
