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

var builder = WebApplication.CreateBuilder(args);

DdtOptions options = builder.Configuration.GetSection(DdtOptions.SectionName).Get<DdtOptions>() ?? new DdtOptions();
IReadOnlySet<DeploymentRole> roles = DeploymentRoles.Parse(options.Roles);
DdtConfigurationCheck.Validate(builder.Configuration, options, roles);

builder.AddServiceDefaults();

builder.Services.Configure<DdtOptions>(builder.Configuration.GetSection(DdtOptions.SectionName));

string activeRoles = string.Join(", ", roles.Order());

// Before Kestrel reads Kestrel:Certificates:Default at startup, so the files exist and are current by then.
ServerCertificates? certificates = CertificateBootstrap.Create(builder.Configuration, options);
CertificateCheck? certificateCheck = null;

if (certificates is not null)
{
    certificateCheck = await certificates.CheckAsync(CancellationToken.None);
    builder.AddDdtServerCertificates(certificates);
}

builder.Services.ConfigureHttpJsonOptions(json =>
{
    // The contexts' own option reaches only their own options, not these: a step's "kind" may come after its members.
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
builder.Services.AddDdtAuthentication(builder.Configuration, options);
builder.Services.AddDdtAuthorization();
builder.Services.AddDdtRateLimiting();
builder.Services.AddDdtForwardedHeaders();
builder.Services.AddDdtMachines();
builder.Services.AddDdtImages();
builder.Services.AddDdtDeployments(builder.Configuration);
builder.Services.AddDdtSequences();

string version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
builder.Services.AddSingleton(AboutCatalog.Load(version, Path.Combine(AppContext.BaseDirectory, "legal")));

// After the data services, so hosted services start in dependency order, and before the endpoint
// check, because the Kestrel endpoint the pxe role adds changes which settings Kestrel honours.
PxeSetup? pxe = roles.Contains(DeploymentRole.Pxe) ? builder.AddDdtPxe(options.StorePath) : null;

HttpsConfigurationCheck.Validate(builder.Configuration, options, roles);

var app = builder.Build();

HostLog.ActiveRoles(app.Logger, activeRoles);

if (certificates is not null && certificateCheck is not null)
{
    CertificateLog.Checked(app.Logger, certificates, certificateCheck);
}

app.MapDefaultEndpoints();

// First, so everything after it sees the client's address and scheme rather than the proxy's: the boot file log,
// HSTS, the rate limiter, Secure cookies and the same origin filters.
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

// After authorization, so a request without a valid token never spends the window of the machine its route
// names. Anonymous endpoints pass authorization and stay limited per address.
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// DisableCookieRedirect makes an unauthenticated API call answer 401 instead of redirecting to a
// login page: the redirect is inferred per endpoint from metadata, so it cannot be relied on.
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
api.MapGroup("/about").MapAboutEndpoints();
api.MapGroup("/server").MapServerEndpoints();
api.MapGroup("/users").MapUserEndpoints();
api.MapGroup("/directory").MapDirectoryEndpoints();

app.MapGroup("/api/agents").MapAgentEndpoints().MapAgentDeploymentEndpoints();

app.MapHub<LiveHub>("/hubs/live")
    .DisableCookieRedirect()
    .RequireAuthorization(DdtPolicies.Viewer)
    .AddEndpointFilter(new SameOriginHubFilter());

app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();
