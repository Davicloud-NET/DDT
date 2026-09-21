// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Authentication;
using DDT.Contracts.Images;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Images;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DDT.Server.Tests;

// The real host on an in-memory server, with its own store directory and SQLite database, so tests
// exercise the actual pipeline: authentication schemes, the fallback policy and the CSRF filters.
public class DdtApplication : WebApplicationFactory<Program>
{
    private readonly SemaphoreSlim _administratorLock = new(1, 1);
    private SignedInClient? _administrator;

    public const string Password = "Correct horse battery staple 42";

    public string StorePath { get; } = Path.Combine(Path.GetTempPath(), "ddt-server-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");
        // UseSetting rather than ConfigureAppConfiguration: Program reads these before it builds the host.
        builder.UseSetting("DDT:Roles", "web");
        builder.UseSetting("DDT:StorePath", StorePath);
        builder.UseSetting("DDT:RequireHttps", "false");
        builder.UseSetting("ConnectionStrings:ddtdb", string.Empty);
        // First, so the address is in place before any other startup filter's middleware runs, as a connection's is.
        builder.ConfigureServices(services => services.Insert(0, ServiceDescriptor.Transient<IStartupFilter, TestRemoteAddress>()));

        ConfigureTestHost(builder);
    }

    protected virtual void ConfigureTestHost(IWebHostBuilder builder)
    {
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

    public async Task<string> CreateUserAsync(string role)
    {
        string userName = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}";

        using IServiceScope scope = Services.CreateScope();
        UserManager<DdtUser> users = scope.ServiceProvider.GetRequiredService<UserManager<DdtUser>>();
        DdtUser user = new() { UserName = userName, CreatedUtc = DateTimeOffset.UtcNow };

        IdentityResult created = await users.CreateAsync(user, Password);

        if (!created.Succeeded || !(await users.AddToRoleAsync(user, role)).Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", created.Errors.Select(e => e.Description)));
        }

        return userName;
    }

    public Task<RegisteredMachine> RegisterMachineAsync(string? remoteAddress = null) =>
        RegisterMachineAsync(new AgentClient(CreateDefaultClient(), remoteAddress ?? TestRemoteAddress.Unique()));

    public async Task<RegisteredMachine> RegisterMachineAsync(AgentClient agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        AgentRegistration registration = AgentClient.Registration(
            Guid.NewGuid().ToString("D"),
            "02" + Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 5));

        AgentRegistrationResult registered = await RegisteredMachine.ReadAsync<AgentRegistrationResult>(
            await agent.RegisterAsync(registration));

        return new RegisteredMachine(agent, registration, registered);
    }

    // Puts a file straight into the library, for tests that need an image but not the upload protocol.
    public async Task<Image> SeedImageAsync(byte[] content, string? architecture = "x64", int wimIndex = 1, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        string sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        ImageStore store = Services.GetRequiredService<ImageStore>();
        Directory.CreateDirectory(store.ObjectsDirectory);
        await File.WriteAllBytesAsync(store.ObjectPath(sha256), content, TestContext.Current.CancellationToken);

        Image image = new()
        {
            Id = Guid.CreateVersion7(),
            Name = name ?? $"Test image {sha256[..8]}",
            Kind = ImageKind.Wim,
            Sha256 = sha256,
            SizeBytes = content.Length,
            WimIndex = wimIndex,
            Edition = "Professional",
            Architecture = architecture,
            Version = "10.0.26200.1",
            Language = "en-US",
            InstalledBytes = content.Length * 4L,
            UploadedUtc = DateTimeOffset.UtcNow,
        };

        using IServiceScope scope = Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        database.Images.Add(image);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        return image;
    }

    public async Task<SignedInClient> SignInAsync(string role)
    {
        string userName = await CreateUserAsync(role);

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

        if (disposing && Directory.Exists(StorePath))
        {
            // Pooled SQLite connections keep the database file open after the host stops.
            SqliteConnection.ClearAllPools();
            Directory.Delete(StorePath, recursive: true);
        }
    }
}
