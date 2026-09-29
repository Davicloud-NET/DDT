// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Reflection;
using DDT.Contracts;
using DDT.Contracts.Agents;
using DDT.Host.Logging;
using DDT.Host.Startup;
using DDT.Pxe;
using DDT.Server.About;
using DDT.Server.Authentication;
using DDT.Server.Certificates;
using DDT.Server.Configuration;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Endpoints;
using DDT.Server.Images;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Security;
using DDT.Server.Sequences;
using DDT.Server.Settings;

// The console verbs run next to a running server. They don't start a server themselves.
if (SettingsConsole.Handles(args))
{
    Environment.ExitCode = await SettingsConsole.RunAsync(args, Console.Out);

    return;
}

var builder = WebApplication.CreateBuilder(args);

DdtOptions options = builder.Configuration.GetSection(DdtOptions.SectionName).Get<DdtOptions>() ?? new DdtOptions();
IReadOnlySet<DeploymentRole> roles = DeploymentRoles.Parse(options.Roles);
DdtConfigurationCheck.Validate(builder.Configuration, options, roles);

builder.AddServiceDefaults();

builder.Services.Configure<DdtOptions>(builder.Configuration.GetSection(DdtOptions.SectionName));

string activeRoles = string.Join(", ", roles.Order());

// Runs before Kestrel reads Kestrel:Certificates:Default at startup, so the files exist and are current by then.
ServerCertificates? certificates = CertificateBootstrap.Create(builder.Configuration, options);
CertificateCheck? certificateCheck = null;

if (certificates is not null)
{
    certificateCheck = await certificates.CheckAsync(CancellationToken.None);
    builder.AddDdtServerCertificates(certificates);
}

builder.Services.ConfigureHttpJsonOptions(json =>
{
    // A step's "kind" may come after its other members. Setting this on the JSON contexts only changes their own
    // options, not these.
    json.SerializerOptions.AllowOutOfOrderMetadataProperties = true;
    json.SerializerOptions.TypeInfoResolverChain.Insert(0, DdtJsonContext.Default);
    json.SerializerOptions.TypeInfoResolverChain.Insert(0, AgentJsonContext.Default);
});

builder.Services.AddAntiforgery(antiforgery =>
{
    antiforgery.HeaderName = CsrfHeaderNames.RequestToken;
    antiforgery.Cookie.Name = options.RequireHttps ? "__Host-ddt-csrf" : "ddt-csrf";
    antiforgery.Cookie.SameSite = SameSiteMode.Strict;
    antiforgery.Cookie.SecurePolicy = options.RequireHttps
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddDdtData(builder.Configuration, options);
builder.Services.AddDdtSettings();
builder.Services.AddDdtAuthentication(builder.Configuration, options);
builder.Services.AddDdtAuthorization();
builder.Services.AddDdtRateLimiting();
builder.Services.AddDdtForwardedHeaders();
builder.Services.AddDdtMachines();
builder.Services.AddDdtImages();
builder.Services.AddDdtDeployments();
builder.Services.AddDdtSequences();

string version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
builder.Services.AddSingleton(AboutCatalog.Load(version, Path.Combine(AppContext.BaseDirectory, "legal")));

// Comes after the data services, so hosted services start in dependency order. Comes before the endpoint check,
// because the pxe role adds a Kestrel endpoint, and that changes which settings Kestrel honours.
PxeBootstrap? pxe = roles.Contains(DeploymentRole.Pxe) ? builder.AddDdtPxe(options.StorePath, PxeSettingsSource.Create) : null;

HttpsConfigurationCheck.Validate(builder.Configuration, options, roles);

var app = builder.Build();

HostLog.ActiveRoles(app.Logger, activeRoles);

if (certificates is not null && certificateCheck is not null)
{
    CertificateLog.Checked(app.Logger, certificates, certificateCheck);
}

app.MapDefaultEndpoints();

// Goes first, so everything after it sees the client's address and scheme instead of the proxy's. That includes the
// boot file log, HSTS, the rate limiter, Secure cookies and the same origin filters.
app.UseDdtForwardedHeaders();

app.UseRouting();

if (pxe is not null)
{
    app.UseDdtBootListener(pxe);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseDefaultFiles();
app.UseStaticFiles();

// The rate limiter runs after authorization, so a request without a valid token can't use up the window of the
// machine named in its route. Anonymous endpoints pass authorization and are still limited per address.
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// DisableCookieRedirect makes an unauthenticated API call return 401 instead of redirecting to a login page.
// ASP.NET Core infers the redirect per endpoint from metadata, so we can't rely on it.
RouteGroupBuilder api = app.MapGroup("/api")
    .DisableCookieRedirect()
    .AddEndpointFilter<SameOriginEndpointFilter>()
    .AddEndpointFilter<AntiforgeryEndpointFilter>();

api.MapGroup("/auth").MapAuthEndpoints();
api.MapGroup("/auth/2fa").MapTwoFactorEndpoints();
api.MapGroup("/auth/external").MapExternalLoginEndpoints();
api.MapGroup("/machines").MapMachineEndpoints();
api.MapGroup("/images").MapImageEndpoints();
api.MapGroup("/deployments").MapDeploymentEndpoints();
api.MapGroup("/sequences").MapSequenceEndpoints();
api.MapGroup("/packages").MapPackageEndpoints();
api.MapGroup("/rules").MapRuleEndpoints();
api.MapGroup("/machine-roles").MapMachineRoleEndpoints();
api.MapGroup("/accounts").MapAccountEndpoints();
api.MapGroup("/about").MapAboutEndpoints();
api.MapGroup("/server").MapServerEndpoints();
api.MapGroup("/users").MapUserEndpoints();
api.MapGroup("/directory").MapDirectoryEndpoints();
api.MapGroup("/audit").MapAuditEndpoints();
api.MapGroup("/tokens").MapApiTokenEndpoints();
api.MapGroup("/boot-image").MapBootImageEndpoints();
api.MapGroup("/settings").MapSettingsEndpoints();

app.MapGroup("/api/agents").MapAgentEndpoints().MapAgentDeploymentEndpoints();

app.MapHub<LiveHub>("/hubs/live")
    .DisableCookieRedirect()
    .RequireAuthorization(DdtPolicies.Viewer)
    .AddEndpointFilter(new SameOriginHubFilter());

app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();
